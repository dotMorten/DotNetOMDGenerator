using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        internal async Task Process(params string[] paths)
        {
            var compilation = await CreateCompilationAsync(paths);
            Console.WriteLine("Processing types...");

            Action<INamespaceSymbol, List<INamespaceSymbol>> getNamespaces = null;
            getNamespaces = (inss, list) =>
            {
                foreach (var childNs in inss.GetMembers().OfType<INamespaceSymbol>())
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
            symbols = symbols.OrderBy(t => t.GetFullTypeName()).ToList();
            generator.Initialize(symbols);
            foreach (var s in symbols)
            {
                GenerateCode(s);
            }
            generator.Complete();
            Console.WriteLine("Complete");
        }

        private IEnumerable<INamedTypeSymbol> GetTypes(INamespaceSymbol ns)
        {
            foreach (var type in ns.GetTypeMembers().OfType<INamedTypeSymbol>())
            {
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
            else
            {
                Console.WriteLine("****TODO**** ERROR: No generator for type " + type.GetFullTypeName() + " of kind " + type.TypeKind.ToString());
            }
        }

        internal Task<Compilation> CreateCompilationAsync(params string[] paths)
        {
            Console.WriteLine("Creating workspace...");

            var ws = new AdhocWorkspace();
            var solutionInfo = SolutionInfo.Create(SolutionId.CreateNewId(), VersionStamp.Default);
            ws.AddSolution(solutionInfo);
            var projectInfo = ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Default, "CSharpExample", "CSharpExample", "C#");
            ws.AddProject(projectInfo);
            foreach (var path in paths)
                LoadFolderDocuments(path, ws, projectInfo.Id);
            Console.WriteLine("Compiling...");
            return ws.CurrentSolution.Projects.Single().GetCompilationAsync();
        }

        private void LoadFolderDocuments(string folderName, AdhocWorkspace ws, ProjectId projectId)
        {
            foreach (var file in new DirectoryInfo(folderName).GetFiles("*.cs"))
            {
                var sourceText = SourceText.From(File.OpenRead(file.FullName));
                ws.AddDocument(projectId, file.Name, sourceText);
            }
            foreach (var dir in new DirectoryInfo(folderName).GetDirectories())
            {
                LoadFolderDocuments(dir.FullName, ws, projectId);
            }
        }
    }
}
