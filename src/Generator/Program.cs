using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using NuGet.Protocol.Core.Types;
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
            string[] nugetPackages = arg.ContainsKey("nuget") ? arg["nuget"].Split(';', StringSplitOptions.RemoveEmptyEntries) : null;
            string[] compareNugetPackages = arg.ContainsKey("compareNuget") ? arg["compareNuget"].Split(';', StringSplitOptions.RemoveEmptyEntries) : null;
            string tfm = arg.ContainsKey("tfm") ? arg["tfm"] : null;

            // Fetch nuget packages
            if (nugetPackages != null && nugetPackages.Length > 0)
            {
                var nugetAssemblies = await ParseNugets(nugetPackages, tfm);
                if (nugetAssemblies is null)
                    return;
                Console.WriteLine($"Found {nugetAssemblies.Length} assemblies in nuget packages");
                assemblies = assemblies == null ? nugetAssemblies : assemblies.Concat(nugetAssemblies).ToArray();
            }
            if (compareNugetPackages != null && compareNugetPackages.Length > 0)
            {
                var nugetAssemblies = await ParseNugets(compareNugetPackages, tfm);
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
                await g.ProcessDiffs(oldSource, source, compareAssemblies, assemblies, preprocessors, filters.ToArray(), referenceAssemblies);
            else
                await g.Process(source, assemblies, preprocessors, filters.ToArray(), referenceAssemblies);

            if(System.Diagnostics.Debugger.IsAttached)
                Console.ReadKey();
        }
        static List<FindPackageByIdResource> resources;
        private async static Task<string[]> ParseNugets(string[] nugetPackages, string tfm)
        {
            if (string.IsNullOrEmpty(tfm))
            {
                Console.WriteLine("A target framework identifier is required with nuget parameter. For example: '-tfm net8.0-windows10.0.19041.0'");
                return null;
            }

            if (resources is null)
            {
                var settings = NuGet.Configuration.Settings.LoadDefaultSettings(null);
                var sources = NuGet.Configuration.SettingsUtility.GetEnabledSources(settings);
                resources = new List<FindPackageByIdResource>();
                foreach (var source in sources)
                {
                    List<Lazy<INuGetResourceProvider>> providers = new List<Lazy<INuGetResourceProvider>>();
                    providers.AddRange(Repository.Provider.GetCoreV3());  // Add v3 API support

                    SourceRepository repository = new SourceRepository(source, providers);
                    FindPackageByIdResource resource = await repository.GetResourceAsync<FindPackageByIdResource>();
                    resources.Add(resource);
                }
            }

                List<string> nugetAssemblies = new List<string>();
            // https://www.nuget.org/api/v2/package/Newtonsoft.Json/13.0.3
            foreach (var package in nugetPackages)
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
                NuGet.Versioning.NuGetVersion version;
                if (!NuGet.Versioning.NuGetVersion.TryParse(id[1], out version))
                {
                    Console.WriteLine($"Invalid nuget version {id[1]}");
                    return null;
                }
                //var f = NuGet.Frameworks.AssetTargetFallbackFramework.ParseFrameworkName(tfm, new NuGet.Frameworks.DefaultFrameworkNameProvider());
                var f = NuGet.Frameworks.NuGetFramework.Parse(tfm, new NuGet.Frameworks.DefaultFrameworkNameProvider());

                Console.WriteLine($"Getting NuGet package {package}...");
                MemoryStream resultStream = null;
                foreach (var resource in resources)
                {
                    var exists = await resource.DoesPackageExistAsync(id[0], version, new SourceCacheContext(), NuGet.Common.NullLogger.Instance, System.Threading.CancellationToken.None);
                    if (!exists)
                    {
                        continue;
                    }
                    MemoryStream packageStream = new MemoryStream();
                    bool result = await resource.CopyNupkgToStreamAsync(
                        id[0],
                        version,
                        packageStream,
                        new SourceCacheContext(),
                        NuGet.Common.NullLogger.Instance,
                        System.Threading.CancellationToken.None);
                    if (result)
                    {
                        resultStream = packageStream;
                        resultStream.Seek(0, SeekOrigin.Begin);
                        break;
                    }
                    else
                    {
                        packageStream.Dispose();
                    }
                }
                if (resultStream is null)
                {
                    Console.WriteLine($"'{package}' not found");
                    return null;
                }
                using var packageReader = new NuGet.Packaging.PackageArchiveReader(resultStream);
                var libs = (await packageReader.GetLibItemsAsync(CancellationToken.None)).ToList();
                var nearest = NuGet.Frameworks.NuGetFrameworkExtensions.GetNearest(libs, f);

                if (nearest is null)
                {
                    Console.WriteLine($"No compatible target framework found for '{tfm}' in '{package}'");
                    resultStream.Dispose();
                    return null;
                }
                var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
                packageReader.CopyFiles(path, nearest.Items, (string sourceFile, string targetPath, Stream fileStream) =>
                {
                    FileInfo fi = new FileInfo(targetPath);
                    if (!fi.Directory.Exists)
                        fi.Directory.Create();
                    using var fs = File.Create(targetPath);
                    fileStream.CopyTo(fs);
                    nugetAssemblies.Add(targetPath);
                    return targetPath;
                }, NuGet.Common.NullLogger.Instance, CancellationToken.None);
                resultStream.Dispose();
            }
            return nugetAssemblies.ToArray();
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
            Console.Write("Using Nuget comparison:");
            Console.WriteLine("  nuget                nuget packages to generate OMD for (separate multiple with semicolon). Example: /nuget=Newtonsoft.Json:13.0.0");
            Console.WriteLine("  compareNuget         nuget packages to compare versions with (separate multiple with semicolon). Example: /nuget=Newtonsoft.Json:12.0.0");
            Console.WriteLine("  tfm                  Target Framework to use against NuGet package. Example: /tfm=net8.0-windows10.0.19041.0");
        }
    }
}
