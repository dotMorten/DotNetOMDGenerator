using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using NuGet.Common;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Generator
{
    internal static class GeneratorSettings
    {
        public static bool ShowPrivateMembers { get; set; } = false;
        public static bool ShowInternalMembers { get; set; } = false;
        public static string OutputLocation { get; set; } = "./";
    }

    class Program
    {

        static async Task Main(string[] args)
        {
            Console.WriteLine("*********************** Object Model Generator ***********************");

            var arg = ArgumentParser.Parse(args);
            List<ICodeGenerator> generators = new List<ICodeGenerator>();
            if (arg.ContainsKey("format"))
            {
                var formats = arg["format"].Split(';', StringSplitOptions.RemoveEmptyEntries);
                foreach (var format in formats)
                    if (format == "md")
                        generators.Add(new Generators.MarkdownGenerator());
                    else if (format == "html")
                        generators.Add(new Generators.HtmlOmdGenerator());
                    else
                    {
                        Console.WriteLine("Invalid format parameter.");
                        WriteUsage();
                        return;
                    }
            }
            if(!generators.Any())
            {
                generators.Add(new Generators.HtmlOmdGenerator());
            }
            if (!arg.ContainsKey("source") && !arg.ContainsKey("assemblies") && !arg.ContainsKey("nuget"))
            {
                WriteUsage();
                return;
            }

            GeneratorSettings.ShowPrivateMembers = arg.ContainsKey("showPrivate");
            GeneratorSettings.ShowInternalMembers = arg.ContainsKey("showInternal");
            if(arg.ContainsKey("output"))
                GeneratorSettings.OutputLocation = arg["output"];
            List<Regex> filters = arg.ContainsKey("exclude") ? arg["exclude"].Split(';', StringSplitOptions.RemoveEmptyEntries).Select(f=>CreateFilter(f)).ToList() : new List<Regex>();
            if(arg.ContainsKey("regexfilter"))
                filters.Add(new Regex(arg["regexfilter"]));
            string[] source = arg.ContainsKey("source") ? arg["source"].Split(';', StringSplitOptions.RemoveEmptyEntries) : new string[] { };
            string[] oldSource = arg.ContainsKey("compareSource") ? arg["compareSource"].Split(';', StringSplitOptions.RemoveEmptyEntries) : null;
            string[] preprocessors = arg.ContainsKey("preprocessors") ? arg["preprocessors"].Split(';', StringSplitOptions.RemoveEmptyEntries) : null;
            string[] assemblies = arg.ContainsKey("assemblies") ? arg["assemblies"].Split(';', StringSplitOptions.RemoveEmptyEntries) : new string[] { };
            string[] compareAssemblies = arg.ContainsKey("compareAssemblies") ? arg["compareAssemblies"].Split(';', StringSplitOptions.RemoveEmptyEntries) : null;
            string[] referenceAssemblies = arg.ContainsKey("referenceAssemblies") ? arg["referenceAssemblies"].Split(';', StringSplitOptions.RemoveEmptyEntries) : null;
            string[] filterTypes = arg.ContainsKey("filter") ? arg["filter"].Split(';', StringSplitOptions.RemoveEmptyEntries) : null;
            string[] nugetPackages = arg.ContainsKey("nuget") ? arg["nuget"].Split(';', StringSplitOptions.RemoveEmptyEntries) : null;
            string[] compareNugetPackages = arg.ContainsKey("compareNuget") ? arg["compareNuget"].Split(';', StringSplitOptions.RemoveEmptyEntries) : null;
            string nugetDependencies = arg.ContainsKey("nugetDependencies") ? arg["nugetDependencies"] : null;
            string tfm = arg.ContainsKey("tfm") ? arg["tfm"] : null;

            // Fetch nuget packages
            if (nugetPackages != null && nugetPackages.Length > 0)
            {
                var nugetAssemblies = await ParseNugets(nugetPackages, tfm, nugetDependencies);
                if (nugetAssemblies is null)
                    return;
                Console.WriteLine($"Found {nugetAssemblies.Length} assemblies in nuget packages");
                assemblies = assemblies == null ? nugetAssemblies : assemblies.Concat(nugetAssemblies).ToArray();
            }
            if (compareNugetPackages != null && compareNugetPackages.Length > 0)
            {
                var nugetAssemblies = await ParseNugets(compareNugetPackages, tfm, nugetDependencies);
                if (nugetAssemblies is null)
                    return;
                Console.WriteLine($"Found {nugetAssemblies.Length} assemblies in nuget packages");
                compareAssemblies = compareAssemblies == null ? nugetAssemblies : compareAssemblies.Concat(nugetAssemblies).ToArray();
            }

            var g = new Generator(generators);

            //Set up output filename
            if (string.IsNullOrEmpty(GeneratorSettings.OutputLocation))
                GeneratorSettings.OutputLocation = "./";
            var fi = new System.IO.FileInfo(GeneratorSettings.OutputLocation);
            if (!fi.Directory.Exists)
                throw new System.IO.DirectoryNotFoundException(fi.Directory.FullName);
            if (fi.Attributes == System.IO.FileAttributes.Directory)
                GeneratorSettings.OutputLocation = System.IO.Path.Combine(GeneratorSettings.OutputLocation, "OMD");

            if (oldSource != null || compareAssemblies != null)
                await g.ProcessDiffs(oldSource, source, compareAssemblies, assemblies, preprocessors, filters.ToArray(), referenceAssemblies, filterTypes);
            else
                await g.Process(source, assemblies, preprocessors, filters.ToArray(), referenceAssemblies, filterTypes);

            if(System.Diagnostics.Debugger.IsAttached)
                Console.ReadKey();
        }
        static List<NuGetSourceResources> resources;

        internal static async Task<string[]> ParseNugets(string[] nugetPackages, string tfm, string dependencyExpression = null)
        {
            if (string.IsNullOrEmpty(tfm))
            {
                Console.WriteLine("A target framework identifier is required with nuget parameter. For example: '-tfm net8.0-windows10.0.19041.0'");
                return null;
            }

            var framework = NuGetFramework.Parse(tfm, new DefaultFrameworkNameProvider());
            var dependencyFilter = NuGetDependencyExpression.Parse(dependencyExpression);

            if (resources is null)
            {
                var settings = NuGet.Configuration.Settings.LoadDefaultSettings(null);
                var sources = NuGet.Configuration.SettingsUtility.GetEnabledSources(settings);
                resources = new List<NuGetSourceResources>();
                foreach (var source in sources)
                {
                    List<Lazy<INuGetResourceProvider>> providers = new List<Lazy<INuGetResourceProvider>>();
                    providers.AddRange(Repository.Provider.GetCoreV3());  // Add v3 API support
 
                    SourceRepository repository = new SourceRepository(source, providers);
                    FindPackageByIdResource findPackage = await repository.GetResourceAsync<FindPackageByIdResource>();
                    DependencyInfoResource dependencyInfo = await repository.GetResourceAsync<DependencyInfoResource>();
                    resources.Add(new NuGetSourceResources(repository, findPackage, dependencyInfo));
                }
            }

            List<string> nugetAssemblies = new List<string>();
            List<PackageIdentity> rootPackages = new List<PackageIdentity>();
            foreach (var package in nugetPackages)
            {
                var rootPackage = ParsePackageIdentity(package);
                if (rootPackage is null)
                    return null;
                rootPackages.Add(rootPackage);
            }

            var packagesToDownload = new Dictionary<string, PackageIdentity>(StringComparer.OrdinalIgnoreCase);
            foreach (var rootPackage in rootPackages)
                packagesToDownload[NuGetDependencyCollector.GetIdentityKey(rootPackage)] = rootPackage;

            if (dependencyFilter != null && dependencyFilter.HasRules)
            {
                var resolutionContext = new NuGetDependencyResolutionContext(rootPackages, GetAvailableVersionsAsync);
                var dependencies = await NuGetDependencyCollector.CollectIncludedDependenciesAsync(
                    rootPackages,
                    dependencyFilter,
                    p => LoadPackageNodeAsync(p, framework),
                    resolutionContext.ResolveDependencyIdentityAsync).ConfigureAwait(false);

                foreach (var dependency in dependencies)
                    packagesToDownload[NuGetDependencyCollector.GetIdentityKey(dependency)] = dependency;

                Console.WriteLine($"Including {dependencies.Count} dependency package(s) matching '{dependencyExpression}'.");
            }

            foreach (var packageIdentity in packagesToDownload.Values)
            {
                Console.WriteLine($"Getting NuGet package {packageIdentity.Id}:{packageIdentity.Version}...");
                using var resultStream = await DownloadPackageAsync(packageIdentity).ConfigureAwait(false);
                if (resultStream is null)
                {
                    Console.WriteLine($"'{packageIdentity.Id}:{packageIdentity.Version}' not found");
                    return null;
                }
                using var packageReader = new PackageArchiveReader(resultStream);
                var libs = (await packageReader.GetLibItemsAsync(CancellationToken.None).ConfigureAwait(false)).ToList();
                var nearest = NuGetFrameworkUtility.GetNearest(libs, framework);

                if (nearest is null)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"Warning: No compatible target framework libs found for '{packageIdentity.Id}:{packageIdentity.Version}' with '{tfm}'");
                    Console.ResetColor();
                    continue;
                }

                var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
                packageReader.CopyFiles(path, nearest.Items.Where(i => !i.EndsWith("/")), (string sourceFile, string targetPath, Stream fileStream) =>
                {
                    FileInfo fi = new FileInfo(targetPath);
                    if (!fi.Directory.Exists)
                        fi.Directory.Create();
                    using var fs = File.Create(targetPath);
                    fileStream.CopyTo(fs);
                    nugetAssemblies.Add(targetPath);
                    return targetPath;
                }, NullLogger.Instance, CancellationToken.None);
            }
            return nugetAssemblies.ToArray();
        }

        private async static Task<NuGetPackageNode> LoadPackageNodeAsync(PackageIdentity package, NuGetFramework framework)
        {
            foreach (var resource in resources)
            {
                var dependencyInfo = await resource.DependencyInfo.ResolvePackage(package, framework, new SourceCacheContext(), NullLogger.Instance, CancellationToken.None).ConfigureAwait(false);
                if (dependencyInfo != null)
                    return new NuGetPackageNode(new PackageIdentity(dependencyInfo.Id, dependencyInfo.Version), dependencyInfo.Dependencies);
            }
            return null;
        }

        private async static Task<IReadOnlyList<NuGetVersion>> GetAvailableVersionsAsync(string packageId)
        {
            var versions = new List<NuGetVersion>();
            foreach (var resource in resources)
            {
                var sourceVersions = await resource.FindPackageById.GetAllVersionsAsync(packageId, new SourceCacheContext(), NullLogger.Instance, CancellationToken.None).ConfigureAwait(false);
                if (sourceVersions != null)
                    versions.AddRange(sourceVersions);
            }

            return versions
                .Distinct()
                .OrderBy(v => v)
                .ToArray();
        }

        private async static Task<MemoryStream> DownloadPackageAsync(PackageIdentity packageIdentity)
        {
            foreach (var resource in resources)
            {
                var exists = await resource.FindPackageById.DoesPackageExistAsync(packageIdentity.Id, packageIdentity.Version, new SourceCacheContext(), NullLogger.Instance, CancellationToken.None).ConfigureAwait(false);
                if (!exists)
                    continue;

                MemoryStream packageStream = new MemoryStream();
                bool result = await resource.FindPackageById.CopyNupkgToStreamAsync(
                    packageIdentity.Id,
                    packageIdentity.Version,
                    packageStream,
                    new SourceCacheContext(),
                    NullLogger.Instance,
                    CancellationToken.None).ConfigureAwait(false);
                if (!result)
                {
                    packageStream.Dispose();
                    continue;
                }

                packageStream.Seek(0, SeekOrigin.Begin);
                return packageStream;
            }
            return null;
        }

        private static PackageIdentity ParsePackageIdentity(string package)
        {
            if (!package.Contains(":"))
            {
                Console.WriteLine($"Invalid nuget identifier {package}. Please use the format `nugetid:version`, for example 'Newtonsoft.Json:13.0.3'");
                return null;
            }

            string[] id = package.Split(':', 2, StringSplitOptions.None);
            if (id.Length != 2)
            {
                Console.WriteLine($"Invalid nuget identifier {package}");
                return null;
            }

            NuGetVersion version;
            if (!NuGetVersion.TryParse(id[1], out version))
            {
                Console.WriteLine($"Invalid nuget version {id[1]}");
                return null;
            }

            return new PackageIdentity(id[0], version);
        }

        private sealed class NuGetSourceResources
        {
            public NuGetSourceResources(SourceRepository repository, FindPackageByIdResource findPackageById, DependencyInfoResource dependencyInfo)
            {
                Repository = repository;
                FindPackageById = findPackageById;
                DependencyInfo = dependencyInfo;
            }

            public SourceRepository Repository { get; }
            public FindPackageByIdResource FindPackageById { get; }
            public DependencyInfoResource DependencyInfo { get; }
        }

        private static System.Text.RegularExpressions.Regex CreateFilter(string pattern, bool caseSensitive = false)
        {
            return new Regex("^" + Regex.Escape(pattern).
             Replace("\\*", ".*").
             Replace("\\?", ".") + "$", caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase);
        }

        private static void WriteUsage()
        {
            Console.WriteLine("\nUsage:");
            Console.WriteLine(" --source=[source folder] --compareSource=[oldSourceFolder] --preprocessors=[defines] --output=[out location] --format=[html,md] --filter=[regex] --showPrivate --showInternal");
            Console.WriteLine("\nRequired parameters (one or more):");
            Console.WriteLine("  source               Specifies the folder of source files to include for the object model.\n                       Separate with ; for multiple folders");
            Console.WriteLine("  assemblies           Specifies a set of assemblies to include for the object model.\n                       Separate with ; for multiple assemblies");
            Console.WriteLine("\nOptional parameters:");
            Console.WriteLine("  compareSource        Specifies a folder to compare source and generate a diff model\n                       This can be useful for finding API changes or compare branches");
            Console.WriteLine("  compareAssemblies    Specifies a set of assemblies to include to generate a adiff model.\n                       Separate with ; for multiple assemblies");
            Console.WriteLine("  output        Output location");
            Console.WriteLine("  preprocessors        Define a set of preprocessors values. Use ; to separate multiple");
            Console.WriteLine("  exclude              Defines one or more strings that can't be part of the path Ie '/Samples/;/UnitTests/'\n                       (use forward slash for folder separators)");
            Console.WriteLine("  regexfilter          Defines a regular expression for filtering on full file names in the source");
            Console.WriteLine("  referenceAssemblies  Specifies a set of assemblies to include for references for better type resolution.");
            Console.WriteLine("  showPrivate          Show private members (default is false)");
            Console.WriteLine("  showInternal         Show internal members (default is false)");
            Console.WriteLine("  filter               A set of namespaces or classes to ignore. For example: -filter=Microsoft.CSharp;Microsoft.VisualBasic"); 
            Console.Write("Using Nuget comparison:");
            Console.WriteLine("  nuget                nuget packages to generate OMD for (separate multiple with semicolon). Example: /nuget=Newtonsoft.Json:13.0.0");
            Console.WriteLine("  compareNuget         nuget packages to compare versions with (separate multiple with semicolon). Example: /nuget=Newtonsoft.Json:12.0.0");
            Console.WriteLine("  nugetDependencies    Dependency package ID patterns to include for nuget/compareNuget. Use ; to separate multiple patterns and prefix ! to exclude. Example: /nugetDependencies=Microsoft.WindowsAppSDK.*;!Microsoft.WindowsAppSDK.Tests.*");
            Console.WriteLine("  tfm                  Target Framework to use against NuGet package. Example: /tfm=net8.0-windows10.0.19041.0");
        }
    }
}
