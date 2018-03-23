﻿using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Generator
{
    internal class Generator
    {
        private ICodeGenerator generator;

        public Generator(ICodeGenerator generator)
        {
            this.generator = generator;
        }

        internal async Task Process(IEnumerable<string> paths, IEnumerable<string> preprocessors = null, Regex filter = null, string[] filters = null)
        {
            var compilation = await CreateCompilationAsync(paths, preprocessors, filter, filters);
            Console.WriteLine("Processing types...");
            var symbols = GetSymbols(compilation);

            generator.Initialize(symbols);
            foreach (var s in symbols)
            {
                GenerateCode(s);
            }
            generator.Complete();
            Console.WriteLine("Complete");
        }

        private List<INamedTypeSymbol> GetSymbols(Compilation compilation)
        {
            Action<INamespaceSymbol, List<INamespaceSymbol>> getNamespaces = null;
            getNamespaces = (inss, list) =>
            {
                foreach (var childNs in inss.GetMembers().OfType<INamespaceSymbol>().Where(n => n.Locations.Any(l => l.Kind == LocationKind.SourceFile)))
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
                symbols.AddRange(GetTypes(ns));
            }
            symbols = symbols.OrderBy(t => t.Name).OrderBy(t => t.GetFullNamespace()).ToList();
            return symbols;
        }

        private IEnumerable<INamedTypeSymbol> GetTypes(INamespaceSymbol ns)
        {
            foreach (var type in ns.GetTypeMembers().OfType<INamedTypeSymbol>())
            {
                if (type.Locations.Any(l => l.Kind != LocationKind.SourceFile))
                    continue;
                if (type.DeclaredAccessibility == Accessibility.Private && !GeneratorSettings.ShowPrivateMembers)
                    continue;
                if (type.DeclaredAccessibility == Accessibility.Internal && !GeneratorSettings.ShowInternalMembers)
                    continue;
                yield return type;
            }
        }

        private void GenerateCode(INamedTypeSymbol type)
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

        internal async Task<Compilation> CreateCompilationAsync(IEnumerable<string> paths, IEnumerable<string> preprocessors = null, Regex filter = null, string[] filters = null)
        {
            Console.WriteLine("Creating workspace...");

            var ws = new AdhocWorkspace();
            var solutionInfo = SolutionInfo.Create(SolutionId.CreateNewId(), VersionStamp.Default);
            ws.AddSolution(solutionInfo);
            var projectInfo = ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Default, "CSharpExample", "CSharpExample", "C#");
            ws.AddProject(projectInfo);
            foreach (var path in paths)
            {
                if (path.StartsWith("http://") || path.StartsWith("https://"))
                {
                    await DownloadDocumentsAsync(path, ws, projectInfo.Id, filter, filters).ConfigureAwait(false);
                }
                else if (path.EndsWith(".zip"))
                {
                    LoadCompressedDocuments(path, ws, projectInfo.Id, filter, filters);
                }
                else
                {
                    LoadFolderDocuments(path, ws, projectInfo.Id, filter, filters);
                }
            }
            Console.WriteLine("Compiling...");
            string mscorlib = @"c:\Windows\Microsoft.NET\Framework\v4.0.30319\mscorlib.dll";
            var project = ws.CurrentSolution.Projects.Single();
            if (File.Exists(mscorlib))
            {
                project = project.WithParseOptions(new Microsoft.CodeAnalysis.CSharp.CSharpParseOptions(Microsoft.CodeAnalysis.CSharp.LanguageVersion.Latest, DocumentationMode.Parse, SourceCodeKind.Regular, preprocessors));
                var metaref = MetadataReference.CreateFromFile(mscorlib);
                project = project.AddMetadataReference(metaref);
            }

            return await project.GetCompilationAsync().ConfigureAwait(false);
        }

        private async Task DownloadDocumentsAsync(string uri, AdhocWorkspace ws, ProjectId projectId, Regex filter, string[] filters)
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
                            while (count != 0) {
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
                        LoadCompressedDocuments(filename, ws, projectId, filter, filters);
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

        private void LoadCompressedDocuments(string zipFile, AdhocWorkspace ws, ProjectId projectId, Regex filter, string[] filters)
        {
            using (var s = File.OpenRead(zipFile))
            {
                System.IO.Compression.ZipArchive a = new System.IO.Compression.ZipArchive(s, System.IO.Compression.ZipArchiveMode.Read);
                foreach (var e in a.Entries)
                {
                    if (string.IsNullOrEmpty(e.Name)) //Folder
                        continue;
                    if ((filter == null || !filter.IsMatch(e.FullName.Replace('\\', '/'))) &&
                        (filters == null || !filters.Where(f => e.FullName.Replace('\\', '/').Contains(f)).Any()))
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

        private void LoadFolderDocuments(string folderName, AdhocWorkspace ws, ProjectId projectId, Regex filter, string[] filters)
        {
            var di = new DirectoryInfo(folderName);
            FileInfo f = new FileInfo(folderName);
            IEnumerable<FileInfo> files;
            if (f.Exists)
            {
                files = new FileInfo[] { f };
            }
            else
            {
                files = di.GetFiles("*.cs");
                if (filter != null)
                    files = files.Where(n => !filter.IsMatch(n.FullName.Replace('\\', '/')));
                if (filters != null)
                    files = files.Where(n => !filters.Where(fl => n.FullName.Replace('\\', '/').Contains(fl)).Any());
            }
            foreach (var file in files)
            {
                var sourceText = SourceText.From(File.OpenRead(file.FullName));
                ws.AddDocument(projectId, file.Name, sourceText);
            }
            foreach (var dir in new DirectoryInfo(folderName).GetDirectories())
            {
                LoadFolderDocuments(dir.FullName, ws, projectId, filter, filters);
            }
        }

        //************* Difference comparisons *******************/

        internal async Task ProcessDiffs(string[] oldPaths, string[] newPaths, IEnumerable<string> preprocessors = null, Regex filter = null, string[] filters = null)
        {
            var oldCompilation = await CreateCompilationAsync(oldPaths, preprocessors, filter, filters);
            var newCompilation = await CreateCompilationAsync(newPaths, preprocessors, filter, filters);
            var oldSymbols = GetSymbols(oldCompilation);
            var newSymbols = GetSymbols(newCompilation);
            var symbols = GetChangedSymbols(newSymbols, oldSymbols);
            var generator = this.generator as ICodeDiffGenerator;
            generator.Initialize(newSymbols, oldSymbols);
            int i = 0;
            foreach (var s in symbols)
            {
                GenerateCode(generator, s.newSymbol, s.oldSymbol);
                i++;
            }
            generator.Complete();

            Console.WriteLine($"Complete. {i} symbols with changes found");
        }

        internal static IEnumerable<(INamedTypeSymbol newSymbol, INamedTypeSymbol oldSymbol)> GetChangedSymbols(IEnumerable<INamedTypeSymbol> newSymbols, IEnumerable<INamedTypeSymbol> oldSymbols)
        {
            var removedSymbols = oldSymbols.Except(newSymbols, new SymbolNameComparer()).ToList(); //Objects that have been removed
            var addedSymbols = newSymbols.Except(oldSymbols, new SymbolNameComparer()).ToList(); //Objects that have been added
            var sameNewSymbols = newSymbols.Intersect(oldSymbols, new SymbolNameComparer()).ToList(); // Objects present before and after
            var sameOldSymbols = oldSymbols.Intersect(newSymbols, new SymbolNameComparer()).ToList(); // Objects present before and after
            var changedSymbols = sameNewSymbols.Except(sameOldSymbols, new SymbolMemberComparer()).ToList(); //Objects that have changes
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

                var ifacesNew = x.GetInterfaces();
                var ifacesOld = y.GetInterfaces();
                if (ifacesNew.Count() != ifacesOld.Count()) return false;

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

                var fieldsNew = x.GetEnums();
                var fieldsOld = y.GetEnums();
                if (fieldsNew.Count() != fieldsOld.Count()) return false;

                if (ifacesNew.Except(ifacesOld, SymbolNameComparer.Comparer).Any() ||
                    ifacesOld.Except(ifacesNew, SymbolNameComparer.Comparer).Any())
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
                (SymbolDisplayParameterOptions)255, SymbolDisplayPropertyStyle.ShowReadWriteDescriptor, 
                (SymbolDisplayLocalOptions)255, (SymbolDisplayKindOptions)255, (SymbolDisplayMiscellaneousOptions)255);
        }
        internal class PropertyComparer : IEqualityComparer<IPropertySymbol>
        {
            internal static PropertyComparer Comparer = new PropertyComparer();
            public bool Equals(IPropertySymbol x, IPropertySymbol y) => x.ToDisplayString(Constants.AllFormat).Equals(y.ToDisplayString(Constants.AllFormat));
            public int GetHashCode(IPropertySymbol obj) => obj.ToDisplayString(Constants.AllFormat).GetHashCode();
        }

        internal class MethodComparer : IEqualityComparer<IMethodSymbol>
        {
            public static MethodComparer Comparer = new MethodComparer();
            public bool Equals(IMethodSymbol x, IMethodSymbol y) => x.ToDisplayString(Constants.AllFormat).Equals(y.ToDisplayString(Constants.AllFormat));
            public int GetHashCode(IMethodSymbol obj) => obj.ToDisplayString(Constants.AllFormat).GetHashCode();
        }

        internal class EventComparer : IEqualityComparer<IEventSymbol>
        {
            public static EventComparer Comparer = new EventComparer();
            public bool Equals(IEventSymbol x, IEventSymbol y) => x.ToDisplayString(Constants.AllFormat).Equals(y.ToDisplayString(Constants.AllFormat));
            public int GetHashCode(IEventSymbol obj) => obj.ToDisplayString(Constants.AllFormat).GetHashCode();
        }

        internal class FieldComparer : IEqualityComparer<IFieldSymbol>
        {
            public static FieldComparer Comparer = new FieldComparer();
            public bool Equals(IFieldSymbol x, IFieldSymbol y) => FormatField(x).Equals(FormatField(y));
            public int GetHashCode(IFieldSymbol obj) => obj.ToDisplayString(Constants.AllFormat).GetHashCode();
            private static string FormatField(IFieldSymbol x)
            {
                return x.ToDisplayString(Constants.AllFormat) + "=" + x.ConstantValue?.ToString();
            }
        }
    }
}
