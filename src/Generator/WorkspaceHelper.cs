using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Generator
{
    internal class Generator
    {
        private IEnumerable<ICodeGenerator> generators;

        public Generator(IEnumerable<ICodeGenerator> generators)
        {
            this.generators = generators;
        }

        internal async Task Process(IEnumerable<string> paths, IEnumerable<string> assemblies, IEnumerable<string> preprocessors = null, Regex[] filters = null, string[] referenceAssemblies = null, string[] objectFilters = null, string targetFramework = null)
        {
            ApiAvailabilityRegistry.Reset();
            var compilation = await CreateCompilationAsync(paths, assemblies, preprocessors, filters, referenceAssemblies, targetFramework);
            Console.WriteLine("Processing types...");
            var symbols = GetSymbols(compilation);

            foreach (var generator in generators)
            {
                generator.Initialize(symbols);
                foreach (var s in symbols)
                {
                    if (objectFilters is not null && objectFilters.Any(o => s.ToDisplayString().StartsWith(o)))
                        continue;
                    GenerateCode(generator, s);
                }
                generator.Complete();
            }
            Console.WriteLine("Complete");
        }

        private List<INamedTypeSymbol> GetSymbols(IReadOnlyList<CompilationInput> compilations)
        {
            return ApiAvailabilityRegistry
                .MergeTypes(compilations, (CompilationInput input) => GetSymbols(input, compilations))
                .ToList();
        }

        private IEnumerable<INamedTypeSymbol> GetSymbols(CompilationInput compilationInput, IReadOnlyList<CompilationInput> allCompilations)
        {
            var metadata = compilationInput.MetadataReferences.Any()
                ? compilationInput.MetadataReferences
                : allCompilations.SelectMany(c => c.MetadataReferences).Distinct().ToList();
            return GetSymbols(compilationInput.Compilation, metadata);
        }

        private List<INamedTypeSymbol> GetSymbols(Compilation compilation, IEnumerable<MetadataReference> assemblies)
        {
            Action<INamespaceSymbol, List<INamespaceSymbol>> getNamespaces = null;
            getNamespaces = (inss, list) =>
            {
                foreach (var childNs in inss.GetMembers().OfType<INamespaceSymbol>().Where(n => n.Locations.Any(l => l.Kind == LocationKind.SourceFile)))
                {
                    list.Add(childNs);
                    getNamespaces(childNs, list);
                }
                foreach (var childNs in inss.GetMembers().OfType<INamespaceSymbol>().Where(n => n.Locations.Any(l => l.Kind == LocationKind.MetadataFile)))
                {
                    list.Add(childNs);
                    getNamespaces(childNs, list);
                }
            };
            List<INamespaceSymbol> namespaces = new List<INamespaceSymbol>();
            getNamespaces(compilation.GlobalNamespace, namespaces);
            List<INamedTypeSymbol> symbols = new List<INamedTypeSymbol>();
            foreach (var ns in namespaces)
            {
                symbols.AddRange(GetTypes(ns, assemblies));
            }
            return symbols;
        }

        private IEnumerable<INamedTypeSymbol> GetTypes(INamespaceSymbol ns, IEnumerable<MetadataReference> assemblies)
        {
            foreach (var type in ns.GetTypeMembers().OfType<INamedTypeSymbol>())
            {
                if (type.Locations.Any(l => l.Kind == LocationKind.MetadataFile))
                {
                    var loc = type.Locations.First(l => l.Kind == LocationKind.MetadataFile);
                    var meta = loc.MetadataModule.GetMetadata();
                    if (!assemblies.Any(n => n.Display.EndsWith(meta.Name)))
                        continue;
                }
                else if (type.Locations.Any(l => l.Kind != LocationKind.SourceFile))
                    continue;
                if (type.DeclaredAccessibility == Accessibility.Private && !GeneratorSettings.ShowPrivateMembers)
                    continue;
                if (type.DeclaredAccessibility == Accessibility.Internal && !GeneratorSettings.ShowInternalMembers)
                    continue;
                yield return type;
            }
        }

        private void GenerateCode(ICodeGenerator generator, INamedTypeSymbol type)
        {
            Console.WriteLine(type.GetFullTypeName());
            if (type.TypeKind == TypeKind.Enum)
                generator.WriteEnum(type);
            else if (type.TypeKind == TypeKind.Interface)
                generator.WriteInterface(type);
            else if (type.TypeKind == TypeKind.Class || type.TypeKind == TypeKind.Struct)
                generator.WriteClass(type);
            else if (type.TypeKind == TypeKind.Delegate)
                generator.WriteDelegate(type);
            else
            {
                Console.WriteLine("****TODO**** ERROR: No generator for type " + type.GetFullTypeName() + " of kind " + type.TypeKind.ToString());
            }
        }

        internal async Task<IReadOnlyList<CompilationInput>> CreateCompilationAsync(IEnumerable<string> paths, IEnumerable<string> assemblies, IEnumerable<string> preprocessors = null, Regex[] filters = null, string[] referenceAssemblies = null, string targetFramework = null)
        {
            Console.WriteLine("Creating workspace...");
            var compilationInputs = new List<CompilationInput>();
            var sourcePaths = paths?.Where(p => !string.IsNullOrWhiteSpace(p)).ToArray() ?? Array.Empty<string>();
            var projectPaths = sourcePaths.Where(IsProjectFile).ToArray();
            var nonProjectPaths = sourcePaths.Where(p => !IsProjectFile(p)).ToArray();
            var assemblyReferences = ResolveMetadataReferences(assemblies);
            var supportReferences = ResolveMetadataReferences(referenceAssemblies);

            if (nonProjectPaths.Length > 0)
            {
                var adhocCompilation = await CreateAdhocCompilationAsync(nonProjectPaths, preprocessors, filters, assemblyReferences.Concat(supportReferences)).ConfigureAwait(false);
                compilationInputs.Add(new CompilationInput(adhocCompilation, new List<MetadataReference>(), null));
            }

            if (projectPaths.Length > 0)
            {
                var projectCompilations = await LoadProjectCompilationsAsync(projectPaths, assemblyReferences.Concat(supportReferences), targetFramework, preprocessors).ConfigureAwait(false);
                compilationInputs.AddRange(projectCompilations);
            }

            if (assemblyReferences.Count > 0)
                compilationInputs.Add(await CreateAssemblyCompilationAsync(assemblyReferences, supportReferences, preprocessors).ConfigureAwait(false));

            return compilationInputs;
        }

        private async Task<Compilation> CreateAdhocCompilationAsync(IEnumerable<string> paths, IEnumerable<string> preprocessors, Regex[] filters, IEnumerable<MetadataReference> metadataReferences, string langVersion = null)
        {
            var ws = new AdhocWorkspace();
            var solutionInfo = SolutionInfo.Create(SolutionId.CreateNewId(), VersionStamp.Default);
            ws.AddSolution(solutionInfo);
            var projectInfo = ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Default, "CSharpExample", "CSharpExample", LanguageNames.CSharp);
            ws.AddProject(projectInfo);
            foreach (var path in paths)
            {
                if (path.StartsWith("http://") || path.StartsWith("https://"))
                {
                    await DownloadDocumentsAsync(path, ws, projectInfo.Id, filters).ConfigureAwait(false);
                }
                else if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    LoadCompressedDocuments(path, ws, projectInfo.Id, filters);
                }
                else
                {
                    LoadFolderDocuments(path, ws, projectInfo.Id, filters);
                }
            }

            Console.WriteLine("Compiling...");
            var project = ws.CurrentSolution.Projects.Single()
                .WithParseOptions(new CSharpParseOptions(ParseLanguageVersion(langVersion), DocumentationMode.Parse, SourceCodeKind.Regular, preprocessors));
            foreach (var metadataReference in metadataReferences)
                project = project.AddMetadataReference(metadataReference);

            return await project.GetCompilationAsync().ConfigureAwait(false);
        }

        private async Task<IReadOnlyList<CompilationInput>> LoadProjectCompilationsAsync(IEnumerable<string> projectPaths, IEnumerable<MetadataReference> extraReferences, string targetFramework, IEnumerable<string> preprocessors)
        {
            var references = extraReferences.ToArray();
            var compilationInputs = new List<CompilationInput>();
            foreach (var projectPath in projectPaths)
            {
                var targetFrameworks = await GetValidatedTargetFrameworksAsync(projectPath, targetFramework).ConfigureAwait(false);
                await RestoreProjectAsync(projectPath).ConfigureAwait(false);
                foreach (var resolvedTargetFramework in targetFrameworks)
                {
                    Console.WriteLine("Compiling...");
                    var projectEvaluation = await LoadProjectEvaluationAsync(projectPath, resolvedTargetFramework).ConfigureAwait(false);
                    var compilation = await CreateAdhocCompilationAsync(
                        projectEvaluation.CompileFiles,
                        projectEvaluation.PreprocessorSymbols.Concat(preprocessors ?? Array.Empty<string>()),
                        null,
                        projectEvaluation.ReferencePaths.Select(path => MetadataReference.CreateFromFile(path)).Concat(references),
                        projectEvaluation.LangVersion).ConfigureAwait(false);
                    compilationInputs.Add(new CompilationInput(compilation, new List<MetadataReference>(), projectEvaluation.TargetFramework));
                }
            }

            return compilationInputs;
        }

        private async Task<CompilationInput> CreateAssemblyCompilationAsync(IEnumerable<MetadataReference> assemblies, IEnumerable<MetadataReference> supportReferences, IEnumerable<string> preprocessors)
        {
            var ws = new AdhocWorkspace();
            var solutionInfo = SolutionInfo.Create(SolutionId.CreateNewId(), VersionStamp.Default);
            ws.AddSolution(solutionInfo);
            var projectInfo = ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Default, "AssemblyMetadata", "AssemblyMetadata", LanguageNames.CSharp);
            ws.AddProject(projectInfo);

            var project = ws.CurrentSolution.Projects.Single()
                .WithParseOptions(new CSharpParseOptions(LanguageVersion.Latest, DocumentationMode.Parse, SourceCodeKind.Regular, preprocessors));
            foreach (var metadataReference in assemblies.Concat(supportReferences))
                project = project.AddMetadataReference(metadataReference);

            Console.WriteLine("Compiling...");
            var compilation = await project.GetCompilationAsync().ConfigureAwait(false);
            return new CompilationInput(compilation, assemblies.ToList(), null);
        }

        private static List<MetadataReference> ResolveMetadataReferences(IEnumerable<string> assemblies)
        {
            var metadata = new List<MetadataReference>();
            if (assemblies == null)
                return metadata;

            foreach (var assm in assemblies)
            {
                foreach (var file in ResolveAssemblyFiles(assm))
                    metadata.Add(MetadataReference.CreateFromFile(file.FullName));
            }

            return metadata;
        }

        private static IEnumerable<FileInfo> ResolveAssemblyFiles(string assm)
        {
            IEnumerable<FileInfo> files = Enumerable.Empty<FileInfo>();
            if (File.Exists(assm))
            {
                files = new FileInfo[] { new FileInfo(assm) };
            }
            else
            {
                string recursive = Path.DirectorySeparatorChar + "**" + Path.DirectorySeparatorChar;
                bool isRecursive = false;
                var d = assm;
                var fn = Path.GetFileName(assm);
                if (d.Contains(recursive))
                {
                    d = d.Substring(0, d.IndexOf(recursive, StringComparison.Ordinal));
                    isRecursive = true;
                }
                else if (Directory.Exists(d))
                {
                    fn = null;
                }
                else
                {
                    d = Path.GetDirectoryName(d);
                }
                var dir = new DirectoryInfo(d);
                if (!dir.Exists)
                    throw new DirectoryNotFoundException(d);
                files = string.IsNullOrEmpty(fn)
                    ? dir.GetFiles("*.dll", isRecursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                    : dir.GetFiles(fn, isRecursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
            }

            return files;
        }

        private static bool IsProjectFile(string path) => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase);

        private static async Task<string[]> GetValidatedTargetFrameworksAsync(string projectPath, string targetFramework)
        {
            var targetFrameworks = await GetProjectTargetFrameworksAsync(projectPath).ConfigureAwait(false);
            if (targetFrameworks.Length == 0)
                return new[] { targetFramework };

            if (targetFrameworks.Length == 1)
            {
                if (targetFrameworks.Length == 1 && !string.IsNullOrWhiteSpace(targetFramework) && !targetFrameworks[0].Equals(targetFramework, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException($"Project '{projectPath}' targets '{targetFrameworks[0]}', not '{targetFramework}'.");
                return targetFrameworks;
            }

            if (string.IsNullOrWhiteSpace(targetFramework))
                return targetFrameworks;

            if (!targetFrameworks.Contains(targetFramework, StringComparer.OrdinalIgnoreCase))
                throw new ArgumentException($"Project '{projectPath}' does not target '{targetFramework}'.");

            return new[] { targetFramework };
        }

        private static async Task<string[]> GetProjectTargetFrameworksAsync(string projectPath)
        {
            var output = await RunDotNetAsync(new[]
            {
                "msbuild",
                projectPath,
                "-nologo",
                "-verbosity:quiet",
                "-getProperty:TargetFramework",
                "-getProperty:TargetFrameworks"
            }).ConfigureAwait(false);

            using var document = JsonDocument.Parse(ExtractJson(output));
            var properties = document.RootElement.GetProperty("Properties");
            return new[]
                {
                    properties.TryGetProperty("TargetFramework", out var targetFrameworkProperty) ? targetFrameworkProperty.GetString() : null,
                    properties.TryGetProperty("TargetFrameworks", out var targetFrameworksProperty) ? targetFrameworksProperty.GetString() : null
                }
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .SelectMany(v => v.Split(';', StringSplitOptions.RemoveEmptyEntries))
                .Select(t => t.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static async Task<ProjectEvaluation> LoadProjectEvaluationAsync(string projectPath, string targetFramework)
        {
            var arguments = new List<string>
            {
                "msbuild",
                projectPath,
                "-nologo",
                "-verbosity:quiet",
                "-target:ResolveReferences",
                "-getProperty:TargetFramework",
                "-getProperty:DefineConstants",
                "-getProperty:LangVersion",
                "-getItem:Compile",
                "-getItem:ReferencePath",
                "-property:DesignTimeBuild=true",
                "-property:BuildProjectReferences=false",
                "-property:SkipCompilerExecution=true",
                "-property:ProvideCommandLineArgs=true",
                "-property:BuildingInsideVisualStudio=true"
            };
            if (!string.IsNullOrWhiteSpace(targetFramework))
                arguments.Add($"-property:TargetFramework={targetFramework}");

            var output = await RunDotNetAsync(arguments).ConfigureAwait(false);
            using var document = JsonDocument.Parse(ExtractJson(output));
            var root = document.RootElement;
            var properties = root.GetProperty("Properties");
            var items = root.GetProperty("Items");

            var compileFiles = items.TryGetProperty("Compile", out var compileItemArray)
                ? compileItemArray.EnumerateArray()
                    .Select(i => i.GetProperty("FullPath").GetString())
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()
                : Array.Empty<string>();

            var referencePaths = items.TryGetProperty("ReferencePath", out var referenceItemArray)
                ? referenceItemArray.EnumerateArray()
                    .Select(i => i.GetProperty("Identity").GetString())
                    .Where(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()
                : Array.Empty<string>();

            var preprocessorSymbols = properties.TryGetProperty("DefineConstants", out var defineConstants)
                ? defineConstants.GetString().Split(';', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => s.Length > 0).ToArray()
                : Array.Empty<string>();

            return new ProjectEvaluation(
                compileFiles,
                referencePaths,
                preprocessorSymbols,
                properties.TryGetProperty("LangVersion", out var langVersion) ? langVersion.GetString() : null,
                properties.TryGetProperty("TargetFramework", out var resolvedTargetFramework) && !string.IsNullOrWhiteSpace(resolvedTargetFramework.GetString())
                    ? resolvedTargetFramework.GetString()
                    : targetFramework);
        }

        private static async Task RestoreProjectAsync(string projectPath)
        {
            var arguments = new List<string> { "restore", projectPath, "--verbosity", "quiet" };
            await RunDotNetAsync(arguments).ConfigureAwait(false);
        }

        private static async Task<string> RunDotNetAsync(IEnumerable<string> arguments)
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            foreach (var argument in arguments)
                process.StartInfo.ArgumentList.Add(argument);

            process.Start();
            var standardOutput = process.StandardOutput.ReadToEndAsync();
            var standardError = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().ConfigureAwait(false);

            var output = await standardOutput.ConfigureAwait(false);
            var error = await standardError.ConfigureAwait(false);
            if (process.ExitCode != 0)
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? output : error.Trim());

            return output;
        }

        private static string ExtractJson(string output)
        {
            var startIndex = output.IndexOf('{');
            var endIndex = output.LastIndexOf('}');
            if (startIndex < 0 || endIndex < startIndex)
                throw new InvalidOperationException("Unable to parse MSBuild evaluation output.");

            return output.Substring(startIndex, endIndex - startIndex + 1);
        }

        private static LanguageVersion ParseLanguageVersion(string langVersion)
        {
            if (string.IsNullOrWhiteSpace(langVersion) || !LanguageVersionFacts.TryParse(langVersion, out var parsed))
                return LanguageVersion.Latest;

            return parsed;
        }

        internal sealed class CompilationInput
        {
            internal CompilationInput(Compilation compilation, List<MetadataReference> metadataReferences, string targetFramework)
            {
                Compilation = compilation ?? throw new ArgumentNullException(nameof(compilation));
                MetadataReferences = metadataReferences ?? throw new ArgumentNullException(nameof(metadataReferences));
                TargetFramework = targetFramework;
            }

            internal Compilation Compilation { get; }

            internal List<MetadataReference> MetadataReferences { get; }

            internal string TargetFramework { get; }
        }

        private sealed class ProjectEvaluation
        {
            internal ProjectEvaluation(string[] compileFiles, string[] referencePaths, string[] preprocessorSymbols, string langVersion, string targetFramework)
            {
                CompileFiles = compileFiles;
                ReferencePaths = referencePaths;
                PreprocessorSymbols = preprocessorSymbols;
                LangVersion = langVersion;
                TargetFramework = targetFramework;
            }

            internal string[] CompileFiles { get; }

            internal string[] ReferencePaths { get; }

            internal string[] PreprocessorSymbols { get; }

            internal string LangVersion { get; }

            internal string TargetFramework { get; }
        }

        private async Task DownloadDocumentsAsync(string uri, AdhocWorkspace ws, ProjectId projectId, Regex[] filters)
        {
            var handler = new HttpClientHandler() { AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate };
            var client = new HttpClient(handler);
            HttpRequestMessage msg = new HttpRequestMessage(HttpMethod.Get, uri);
            msg.Headers.UserAgent.Add(new System.Net.Http.Headers.ProductInfoHeaderValue("DotNetOMDGenerator", "1.0"));
            Console.WriteLine("Downloading " + uri + "...");
            using (var result = await client.SendAsync(msg, HttpCompletionOption.ResponseHeadersRead))
            {
                var content = result.EnsureSuccessStatusCode().Content;
                using (var s = await content.ReadAsStreamAsync())
                {
                    var headers = result.Headers.ToArray();
                    var filename = Path.GetTempFileName();
                    var name = content.Headers.ContentDisposition?.FileName;
                    if (content.Headers.ContentType?.MediaType == "application/zip")
                    {
                        var length = content.Headers.ContentLength;
                        using (var f = System.IO.File.OpenWrite(filename))
                        {
                            var buffer = new byte[65536];
                            long read = 0;
                            int count = -1;
                            while (count != 0)
                            {
                                count = await s.ReadAsync(buffer, 0, buffer.Length);
                                if (count > 0)
                                    await f.WriteAsync(buffer, 0, count);
                                read += count;
                                if (length.HasValue)
                                    Console.Write($"         \r{(read * 100.0 / length.Value).ToString("0.0")}%  ({(length.Value / 1024d / 1024d).ToString("0.0")}mb)");
                                else
                                    Console.Write($"         \r{read} bytes...");
                            }
                            Console.WriteLine();
                        }
                        LoadCompressedDocuments(filename, ws, projectId, filters);
                        File.Delete(filename);
                    }
                    else if (content.Headers.ContentType?.MediaType == "text/plain")
                    {
                        var sourceText = SourceText.From(s);
                        ws.AddDocument(projectId, name ?? "Unknown.cs", sourceText);
                    }
                    else
                        throw new Exception("Invalid or missing content type: " + content.Headers.ContentType?.MediaType);
                }
            }
        }

        private void LoadCompressedDocuments(string zipFile, AdhocWorkspace ws, ProjectId projectId, Regex[] filters)
        {
            using (var s = File.OpenRead(zipFile))
            {
                System.IO.Compression.ZipArchive a = new System.IO.Compression.ZipArchive(s, System.IO.Compression.ZipArchiveMode.Read);
                foreach (var e in a.Entries)
                {
                    if (string.IsNullOrEmpty(e.Name)) //Folder
                        continue;
                    var fullname = e.FullName.Replace('\\', '/');
                    if (filters == null || !filters.Where(f => f.IsMatch(fullname)).Any())
                    {
                        if (e.Name.EndsWith(".cs"))
                        {
                            using (var sr = new StreamReader(e.Open()))
                            {
                                var sourceText = SourceText.From(sr.ReadToEnd());
                                ws.AddDocument(projectId, e.Name, sourceText);
                            }
                        }
                    }
                }
            }
        }

        private void LoadFolderDocuments(string pathName, AdhocWorkspace ws, ProjectId projectId, Regex[] filters)
        {
            FileInfo f = new FileInfo(pathName);
            DirectoryInfo di = null;
            IEnumerable<FileInfo> files;
            if (f.Exists)
            {
                files = new FileInfo[] { f };
            }
            else
            {
                di = new DirectoryInfo(pathName);
                files = di.GetFiles("*.cs");
                if (filters != null)
                    files = files.Where(n => !filters.Where(fl => fl.IsMatch(n.FullName.Replace('\\', '/'))).Any());
            }
            foreach (var file in files)
            {
                var sourceText = SourceText.From(File.OpenRead(file.FullName));
                ws.AddDocument(projectId, file.Name, sourceText);
            }
            if (di != null)
            {
                foreach (var dir in di.GetDirectories())
                {
                    LoadFolderDocuments(dir.FullName, ws, projectId, filters);
                }
            }
        }

        //************* Difference comparisons *******************/

        internal async Task ProcessDiffs(string[] oldPaths, string[] newPaths, IEnumerable<string> oldAssemblies, IEnumerable<string> newAssemblies, IEnumerable<string> preprocessors = null, Regex[] filters = null, string[] referenceAssemblies = null, string[] objectFilters = null, string targetFramework = null)
        {
            ApiAvailabilityRegistry.Reset();
            var oldCompilation = await CreateCompilationAsync(oldPaths, oldAssemblies, preprocessors, filters, referenceAssemblies, targetFramework);
            var newCompilation = await CreateCompilationAsync(newPaths, newAssemblies, preprocessors, filters, referenceAssemblies, targetFramework);
            var oldSymbols = GetSymbols(oldCompilation);
            var newSymbols = GetSymbols(newCompilation);
            var symbols = GetChangedSymbols(newSymbols, oldSymbols);
            int i = 0;
            foreach (var generator in generators.OfType<ICodeDiffGenerator>())
            {
                generator.Initialize(newSymbols, oldSymbols);
                i = 0;
                foreach (var s in symbols)
                {
                    if (objectFilters is not null && objectFilters.Any(o => s.oldSymbol != null && s.oldSymbol.ToDisplayString().StartsWith(o) || s.newSymbol != null && s.newSymbol.ToDisplayString().StartsWith(o)))
                        continue;
                    GenerateCode(generator, s.newSymbol, s.oldSymbol);
                    i++;
                }
                generator.Complete();
            }
            Console.WriteLine($"Complete. {i} symbols with changes found");
        }

        internal static IEnumerable<(INamedTypeSymbol newSymbol, INamedTypeSymbol oldSymbol)> GetChangedSymbols(IEnumerable<INamedTypeSymbol> newSymbols, IEnumerable<INamedTypeSymbol> oldSymbols)
        {
            var symbolNameComparer = new SymbolNameComparer();
            var removedSymbols = oldSymbols.Except(newSymbols, symbolNameComparer).ToList(); //Objects that have been removed
            var addedSymbols = newSymbols.Except(oldSymbols, symbolNameComparer).ToList(); //Objects that have been added
            var sameNewSymbols = newSymbols.Intersect(oldSymbols, symbolNameComparer).ToList(); // Objects present before and after
            var sameOldSymbols = oldSymbols.Intersect(newSymbols, symbolNameComparer).ToList(); // Objects present before and after
            var changedSymbols = sameNewSymbols.Except(sameOldSymbols, new SymbolMemberComparer())
                .Union(sameNewSymbols.Where(n=>n.IsObsolete() && !sameOldSymbols.Single(o=>symbolNameComparer.Equals(n, o)).IsObsolete()))
                .ToList(); //Objects that have changes
            List<(INamedTypeSymbol newSymbol, INamedTypeSymbol oldSymbol)> symbols = new List<(INamedTypeSymbol newSymbol, INamedTypeSymbol oldSymbol)>();
            foreach (var s in addedSymbols)
                symbols.Add((s, null));
            foreach (var s in removedSymbols)
                symbols.Add((null, s));
            foreach (var s in changedSymbols)
            {
                var name = s.GetFullTypeName();
                var oldS = oldSymbols.Where(o => o.GetFullTypeName() == name).First();
                symbols.Add((s, oldS));
            }
            return symbols.OrderBy(s => (s.Item1 ?? s.Item2).Name).OrderBy(s => (s.Item1 ?? s.Item2).GetFullNamespace()).ToList();
        }
        private void GenerateCode(ICodeDiffGenerator generator, INamedTypeSymbol type, INamedTypeSymbol oldType)
        {
            if (type == null && oldType == null)
                throw new ArgumentNullException("Both old and new type can't be null");
            var t = (type ?? oldType);
            Console.WriteLine(t.GetFullTypeName());
            if (t.TypeKind == TypeKind.Enum)
                generator.WriteEnum(type, oldType);
            else if (t.TypeKind == TypeKind.Interface)
                generator.WriteInterface(type, oldType);
            else if (t.TypeKind == TypeKind.Class || t.TypeKind == TypeKind.Struct)
                generator.WriteClass(type, oldType);
            else if (t.TypeKind == TypeKind.Delegate)
                generator.WriteDelegate(type, oldType);
            else
            {
                Console.WriteLine("****TODO**** ERROR: No generator for type " + t.GetFullTypeName() + " of kind " + t.TypeKind.ToString());
            }
        }
        internal class SymbolNameComparer : IEqualityComparer<INamedTypeSymbol>
        {
            internal static SymbolNameComparer Comparer = new SymbolNameComparer();
            public bool Equals(INamedTypeSymbol x, INamedTypeSymbol y) => x.ToDisplayString().Equals(y.ToDisplayString());
            public int GetHashCode(INamedTypeSymbol obj) => obj.ToDisplayString().GetHashCode();
        }

        private class SymbolMemberComparer : IEqualityComparer<INamedTypeSymbol>
        {
            public bool Equals(INamedTypeSymbol x, INamedTypeSymbol y)
            {
                //TODO: Also check base types. It's ok to move members up the hiarchy
                if (x.BaseType?.ToDisplayString() != y.BaseType?.ToDisplayString())
                    return false; // Inheritance changed

                if (x.TypeKind != y.TypeKind) return false;

                if (x.GetDeclarationKind() != y.GetDeclarationKind())
                    return false;

                if (!ApiAvailabilityRegistry.AvailabilityEquals(x, y))
                    return false;

                var ifacesNew = x.GetInterfaces();
                var ifacesOld = y.GetInterfaces();
                if (ifacesNew.Count() != ifacesOld.Count()) return false;

                if (x.TypeKind == TypeKind.Enum && x.EnumUnderlyingType?.ToDisplayString() != y.EnumUnderlyingType?.ToDisplayString())
                    return false; //Enum type changed

                // Compare member count
                var constructorsNew = x.GetConstructors();
                var constructorsOld = y.GetConstructors();
                if (constructorsNew.Count() != constructorsOld.Count()) return false;

                var propsNew = x.GetProperties();
                var propsOld = y.GetProperties();
                if (propsNew.Count() != propsOld.Count()) return false;

                var methodsNew = x.GetMethods();
                var methodsOld = y.GetMethods();
                if (methodsNew.Count() != methodsOld.Count()) return false;

                var eventsNew = x.GetEvents();
                var eventsOld = y.GetEvents();
                if (eventsNew.Count() != eventsOld.Count()) return false;

                var fieldsNew = x.TypeKind == TypeKind.Enum ? x.GetEnums() : x.GetFields();
                var fieldsOld = y.TypeKind == TypeKind.Enum ? y.GetEnums() : y.GetFields();
                if (fieldsNew.Count() != fieldsOld.Count()) return false;

                if (ifacesNew.Except(ifacesOld, AvailabilityAwareTypeComparer.Comparer).Any() ||
                    ifacesOld.Except(ifacesNew, AvailabilityAwareTypeComparer.Comparer).Any())
                    return false;

                if (propsNew.Except(propsOld, PropertyComparer.Comparer).Any() ||
                   propsOld.Except(propsNew, PropertyComparer.Comparer).Any())
                    return false;

                if (constructorsNew.Except(constructorsOld, MethodComparer.Comparer).Any() ||
                   constructorsOld.Except(constructorsNew, MethodComparer.Comparer).Any())
                    return false;

                if (propsNew.Except(propsOld, PropertyComparer.Comparer).Any() ||
                   propsOld.Except(propsNew, PropertyComparer.Comparer).Any())
                    return false;

                if (methodsNew.Except(methodsOld, MethodComparer.Comparer).Any() ||
                   methodsOld.Except(methodsNew, MethodComparer.Comparer).Any())
                    return false;

                if (eventsNew.Except(eventsOld, EventComparer.Comparer).Any() ||
                   eventsOld.Except(eventsNew, EventComparer.Comparer).Any())
                    return false;

                if (fieldsNew.Except(fieldsOld, FieldComparer.Comparer).Any() ||
                   fieldsOld.Except(fieldsNew, FieldComparer.Comparer).Any())
                    return false;
                return true;
            }
            public int GetHashCode(INamedTypeSymbol obj) => obj.GetFullTypeName().GetHashCode();
        }

        internal static class Constants
        {
            public static readonly SymbolDisplayFormat AllFormat = new SymbolDisplayFormat(
                SymbolDisplayGlobalNamespaceStyle.Included,
                SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
                (SymbolDisplayGenericsOptions)255,
                (SymbolDisplayMemberOptions)255,
                (SymbolDisplayDelegateStyle)255, (SymbolDisplayExtensionMethodStyle)255,
                (SymbolDisplayParameterOptions)255, SymbolDisplayPropertyStyle.NameOnly,
                (SymbolDisplayLocalOptions)255, (SymbolDisplayKindOptions)255, (SymbolDisplayMiscellaneousOptions)255);
            public static readonly SymbolDisplayFormat AllFormatWithoutContaining = new SymbolDisplayFormat(
                SymbolDisplayGlobalNamespaceStyle.Omitted,
                SymbolDisplayTypeQualificationStyle.NameOnly,
                (SymbolDisplayGenericsOptions)255,
                (SymbolDisplayMemberOptions)223,
                (SymbolDisplayDelegateStyle)255, (SymbolDisplayExtensionMethodStyle)255,
                (SymbolDisplayParameterOptions)255, SymbolDisplayPropertyStyle.NameOnly,
                (SymbolDisplayLocalOptions)255, (SymbolDisplayKindOptions)255, (SymbolDisplayMiscellaneousOptions)255);
        }
        internal class AvailabilityAwareTypeComparer : IEqualityComparer<INamedTypeSymbol>
        {
            internal static AvailabilityAwareTypeComparer Comparer = new AvailabilityAwareTypeComparer();
            public bool Equals(INamedTypeSymbol x, INamedTypeSymbol y) =>
                x.ToDisplayString().Equals(y.ToDisplayString()) && ApiAvailabilityRegistry.AvailabilityEquals(x, y);
            public int GetHashCode(INamedTypeSymbol obj) => obj.ToDisplayString().GetHashCode();
        }
        internal class PropertyComparer : IEqualityComparer<IPropertySymbol>
        {
            internal static PropertyComparer Comparer = new PropertyComparer();
            public bool Equals(IPropertySymbol x, IPropertySymbol y)
            {
                if (!ApiAvailabilityRegistry.AvailabilityEquals(x, y))
                    return false;
                if (!x.ToDisplayString(Constants.AllFormat).Equals(y.ToDisplayString(Constants.AllFormat)))
                    return false;
                IMethodSymbol gx = (x.GetMethod?.DeclaredAccessibility == Accessibility.Public ||
                    (x.GetMethod?.DeclaredAccessibility == Accessibility.Internal && GeneratorSettings.ShowInternalMembers) ||
                    (x.GetMethod?.DeclaredAccessibility == Accessibility.Private && GeneratorSettings.ShowPrivateMembers)) ? x.GetMethod : null;
                IMethodSymbol gy = (y.GetMethod?.DeclaredAccessibility == Accessibility.Public ||
                    (y.GetMethod?.DeclaredAccessibility == Accessibility.Internal && GeneratorSettings.ShowInternalMembers) ||
                    (y.GetMethod?.DeclaredAccessibility == Accessibility.Private && GeneratorSettings.ShowPrivateMembers)) ? y.GetMethod : null;
                if (gx?.DeclaredAccessibility != gy?.DeclaredAccessibility)
                    return false;
                IMethodSymbol sx = (x.SetMethod?.DeclaredAccessibility == Accessibility.Public ||
                    (x.SetMethod?.DeclaredAccessibility == Accessibility.Internal && GeneratorSettings.ShowInternalMembers) ||
                    (x.SetMethod?.DeclaredAccessibility == Accessibility.Private && GeneratorSettings.ShowPrivateMembers)) ? x.SetMethod : null;
                IMethodSymbol sy = (y.SetMethod?.DeclaredAccessibility == Accessibility.Public ||
                    (y.SetMethod?.DeclaredAccessibility == Accessibility.Internal && GeneratorSettings.ShowInternalMembers) ||
                    (y.SetMethod?.DeclaredAccessibility == Accessibility.Private && GeneratorSettings.ShowPrivateMembers)) ? y.SetMethod : null;
                if (sx?.DeclaredAccessibility != sy?.DeclaredAccessibility)
                    return false;
                return true;
            }
            public int GetHashCode(IPropertySymbol obj) => obj.ToDisplayString(Constants.AllFormat).GetHashCode();
        }

        internal class MethodComparer : IEqualityComparer<IMethodSymbol>
        {
            public static MethodComparer Comparer = new MethodComparer();
            public bool Equals(IMethodSymbol x, IMethodSymbol y) =>
                x.ToDisplayString(Constants.AllFormat).Equals(y.ToDisplayString(Constants.AllFormat)) &&
                ApiAvailabilityRegistry.AvailabilityEquals(x, y);
            public int GetHashCode(IMethodSymbol obj) => obj.ToDisplayString(Constants.AllFormat).GetHashCode();
        }

        internal class EventComparer : IEqualityComparer<IEventSymbol>
        {
            public static EventComparer Comparer = new EventComparer();
            public bool Equals(IEventSymbol x, IEventSymbol y) =>
                x.ToDisplayString(Constants.AllFormat).Equals(y.ToDisplayString(Constants.AllFormat)) &&
                ApiAvailabilityRegistry.AvailabilityEquals(x, y);
            public int GetHashCode(IEventSymbol obj) => obj.ToDisplayString(Constants.AllFormat).GetHashCode();
        }

        internal class FieldComparer : IEqualityComparer<IFieldSymbol>
        {
            public static FieldComparer Comparer = new FieldComparer();
            public bool Equals(IFieldSymbol x, IFieldSymbol y) =>
                FormatField(x).Equals(FormatField(y)) &&
                ApiAvailabilityRegistry.AvailabilityEquals(x, y);
            public int GetHashCode(IFieldSymbol obj) => obj.ToDisplayString(Constants.AllFormat).GetHashCode();
            private static string FormatField(IFieldSymbol x)
            {
                return x.ToDisplayString(Constants.AllFormat) + "=" + x.ConstantValue?.ToString();
            }
        }
    }
}