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
            return symbols;
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
            var metaref = MetadataReference.CreateFromFile(@"c:\Windows\Microsoft.NET\Framework\v4.0.30319\mscorlib.dll");
            var project = ws.CurrentSolution.Projects.Single().AddMetadataReference(metaref);
            return project.GetCompilationAsync();
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


        //************* Difference comparisons *******************/


        internal async Task ProcessDiffs(string[] oldPaths, string[] newPaths)
        {
            var oldCompilation = await CreateCompilationAsync(oldPaths);
            var newCompilation = await CreateCompilationAsync(newPaths);
            var oldSymbols = GetSymbols(oldCompilation);
            var newSymbols = GetSymbols(newCompilation);
            var removedSymbols = oldSymbols.Except(newSymbols, new SymbolNameComparer()).ToList(); //Objects that have been removed
            var addedSymbols = newSymbols.Except(oldSymbols, new SymbolNameComparer()).ToList(); //Objects that have been added
            var sameNewSymbols = newSymbols.Intersect(oldSymbols, new SymbolNameComparer()).ToList(); // Objects present before and after
            var sameOldSymbols = oldSymbols.Intersect(newSymbols, new SymbolNameComparer()).ToList(); // Objects present before and after
            var changedSymbols = sameNewSymbols.Except(sameOldSymbols, new SymbolMemberComparer()).ToList(); //Objects that have changes
            var generator = this.generator as ICodeDiffGenerator;
            generator.Initialize(newSymbols, oldSymbols);
            List<Tuple<INamedTypeSymbol, INamedTypeSymbol>> symbols = new List<Tuple<INamedTypeSymbol, INamedTypeSymbol>>();
            foreach (var s in addedSymbols)
                symbols.Add(new Tuple<INamedTypeSymbol, INamedTypeSymbol>(s, null));
            foreach (var s in removedSymbols)
                symbols.Add(new Tuple<INamedTypeSymbol, INamedTypeSymbol>(null, s));
            foreach (var s in changedSymbols)
            {
                var name = s.GetFullTypeName();
                var oldS = oldSymbols.Where(o => o.GetFullTypeName() == name).First();
                symbols.Add(new Tuple<INamedTypeSymbol, INamedTypeSymbol>(s, oldS));
            }
            foreach(var s in symbols.OrderBy(s=> (s.Item1??s.Item2).GetFullTypeName()))
                GenerateCode(generator, s.Item1, s.Item2);

            generator.Complete();
            Console.WriteLine("Complete");

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
                //TODO: Also change base types. It's ok to move members up the hiarchy

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

        internal class PropertyComparer : IEqualityComparer<IPropertySymbol>
        {
            internal static PropertyComparer Comparer = new PropertyComparer();
            public bool Equals(IPropertySymbol x, IPropertySymbol y) => x.ToDisplayString().Equals(y.ToDisplayString());
            public int GetHashCode(IPropertySymbol obj) => obj.ToDisplayString().GetHashCode();
        }
        internal class MethodComparer : IEqualityComparer<IMethodSymbol>
        {
            public static MethodComparer Comparer = new MethodComparer();
            public bool Equals(IMethodSymbol x, IMethodSymbol y) => x.ToDisplayString().Equals(y.ToDisplayString());
            public int GetHashCode(IMethodSymbol obj) => obj.ToDisplayString().GetHashCode();
        }
        internal class EventComparer : IEqualityComparer<IEventSymbol>
        {
            public static EventComparer Comparer = new EventComparer();
            public bool Equals(IEventSymbol x, IEventSymbol y) => x.ToDisplayString().Equals(y.ToDisplayString());
            public int GetHashCode(IEventSymbol obj) => obj.ToDisplayString().GetHashCode();
        }
        internal class FieldComparer : IEqualityComparer<IFieldSymbol>
        {
            public static FieldComparer Comparer = new FieldComparer();
            public bool Equals(IFieldSymbol x, IFieldSymbol y) => (x.ToDisplayString() + "=" + x.ConstantValue?.ToString()).Equals((y.ToDisplayString() + "=" + y.ConstantValue?.ToString()));
            public int GetHashCode(IFieldSymbol obj) => obj.ToDisplayString().GetHashCode();
        }
    }
}
