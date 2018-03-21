using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;

namespace Generator
{
    internal static class TypeExtensions
    {
        public static string GetDescription(this ISymbol type)
        {
            string xml = null;
            if (type.Kind == SymbolKind.Parameter)
            {
                xml = type.ContainingSymbol.GetDocumentationCommentXml();
            }
            else
            {
                xml = type.GetDocumentationCommentXml();
            }
            if (!string.IsNullOrEmpty(xml))
            {
                XmlDocument doc = new XmlDocument();
                try
                {
                    doc.LoadXml(xml);
                }
                catch
                {
                    return null;
                }
                if (type.Kind == SymbolKind.Parameter)
                {
                    var elm2 = doc.GetElementsByTagName("param").OfType<XmlElement>().Where(e => e.Attributes["name"]?.Value == type.Name).FirstOrDefault();
                    return elm2?.InnerText.Trim();
                }
                var elm = doc.GetElementsByTagName("summary").OfType<XmlElement>().FirstOrDefault();
                return elm?.InnerText.Trim();
            }
            return null;
        }

        private static IEnumerable<ISymbol> GetAllMembers(this INamedTypeSymbol type)
        {
            IEnumerable<ISymbol> members = type.GetMembers();
            if (!GeneratorSettings.ShowPrivateMembers)
                members = members.Where(m => m.DeclaredAccessibility != Accessibility.Private);
            if (!GeneratorSettings.ShowInternalMembers)
                members = members.Where(m => m.DeclaredAccessibility != Accessibility.Internal);
            return members;
        }

        public static IEnumerable<IMethodSymbol> GetMethods(this INamedTypeSymbol type)
        {
            if (type.TypeKind == TypeKind.Delegate)
                return Enumerable.Empty<IMethodSymbol>();
            return type.GetAllMembers().OfType<IMethodSymbol>().Where(m => m.CanBeReferencedByName);
        }

        public static IEnumerable<Tuple<IMethodSymbol, bool>> GetMethods(this INamedTypeSymbol type, INamedTypeSymbol oldType)
        {
            if (oldType == null || type == null)
                return GetMethods(type ?? oldType).Select(p => new Tuple<IMethodSymbol, bool>(p, type == null));
            var newMembers = GetMethods(type);
            var oldMembers = GetMethods(oldType);
            return newMembers.Except(oldMembers, Generator.MethodComparer.Comparer).Select(p => new Tuple<IMethodSymbol, bool>(p, false))
                .Union(oldMembers.Except(newMembers, Generator.MethodComparer.Comparer).Select(p => new Tuple<IMethodSymbol, bool>(p, true)))
                .OrderBy(t => t.Item1.Name);
        }

        public static IEnumerable<IPropertySymbol> GetProperties(this INamedTypeSymbol type)
        {
            return type.GetAllMembers().OfType<IPropertySymbol>().Where(m => m.CanBeReferencedByName);
        }

        public static IEnumerable<Tuple<IPropertySymbol, bool>> GetProperties(this INamedTypeSymbol type, INamedTypeSymbol oldType)
        {
            if (oldType == null || type == null)
                return GetProperties(type ?? oldType).Select(p=>new Tuple<IPropertySymbol, bool>(p, type == null));
            var newProps = GetProperties(type);
            var oldProps = GetProperties(oldType);
            return newProps.Except(oldProps, Generator.PropertyComparer.Comparer).Select(p => new Tuple<IPropertySymbol, bool>(p, false))
                .Union(oldProps.Except(newProps, Generator.PropertyComparer.Comparer).Select(p => new Tuple<IPropertySymbol, bool>(p, true)))
                .OrderBy(t => t.Item1.Name);
        }


        public static IEnumerable<INamedTypeSymbol> GetInterfaces(this INamedTypeSymbol type)
        {
            IEnumerable<INamedTypeSymbol> i = type.Interfaces;
            if (!GeneratorSettings.ShowPrivateMembers)
                i = i.Where(m => m.DeclaredAccessibility != Accessibility.Private);
            if (!GeneratorSettings.ShowInternalMembers)
                i = i.Where(m => m.DeclaredAccessibility != Accessibility.Internal && m.DeclaredAccessibility != Accessibility.NotApplicable);
            return i;
        }

        public static IEnumerable<Tuple<INamedTypeSymbol, bool>> GetInterfaces(this INamedTypeSymbol type, INamedTypeSymbol oldType)
        {
            if (oldType == null || type == null)
                return GetInterfaces(type ?? oldType).Select(p => new Tuple<INamedTypeSymbol, bool>(p, type == null));
            var newMembers = GetInterfaces(type);
            var oldMembers = GetInterfaces(oldType);
            return newMembers.Except(oldMembers, Generator.SymbolNameComparer.Comparer).Select(p => new Tuple<INamedTypeSymbol, bool>(p, false))
                .Union(oldMembers.Except(newMembers, Generator.SymbolNameComparer.Comparer).Select(p => new Tuple<INamedTypeSymbol, bool>(p, true)))
                .OrderBy(t => t.Item1.Name);
        }

        public static bool IsSettable(this IPropertySymbol prop)
        {
            if (prop.SetMethod == null)
                return false;
            if (prop.SetMethod.DeclaredAccessibility == Accessibility.Private && !GeneratorSettings.ShowPrivateMembers)
                return false;
            if (prop.SetMethod.DeclaredAccessibility == Accessibility.Internal && !GeneratorSettings.ShowInternalMembers)
                return false;
            // if (prop.SetMethod.DeclaredAccessibility == Accessibility.Public || prop.SetMethod.DeclaredAccessibility == Accessibility.Protected)
            //     return true;
            return true;
        }
        public static bool IsReadable(this IPropertySymbol prop)
        {
            if (prop.GetMethod == null)
                return false;
            if (prop.GetMethod.DeclaredAccessibility == Accessibility.Private && !GeneratorSettings.ShowPrivateMembers)
                return false;
            if (prop.GetMethod.DeclaredAccessibility == Accessibility.Internal && !GeneratorSettings.ShowInternalMembers)
                return false;
            // if (prop.SetMethod.DeclaredAccessibility == Accessibility.Public || prop.SetMethod.DeclaredAccessibility == Accessibility.Protected)
            //     return true;
            return true;
        }
        public static IEnumerable<IEventSymbol> GetEvents(this INamedTypeSymbol type)
        {
            return type.GetAllMembers().OfType<IEventSymbol>().Where(m => m.CanBeReferencedByName);
        }

        public static IEnumerable<Tuple<IEventSymbol, bool>> GetEvents(this INamedTypeSymbol type, INamedTypeSymbol oldType)
        {
            if (oldType == null || type == null)
                return GetEvents(type ?? oldType).Select(p => new Tuple<IEventSymbol, bool>(p, type == null));
            var newMembers = GetEvents(type);
            var oldMembers = GetEvents(oldType);
            return newMembers.Except(oldMembers, Generator.EventComparer.Comparer).Select(p => new Tuple<IEventSymbol, bool>(p, false))
                .Union(oldMembers.Except(newMembers, Generator.EventComparer.Comparer).Select(p => new Tuple<IEventSymbol, bool>(p, true)))
                .OrderBy(t => t.Item1.Name);
        }
        public static IEnumerable<IMethodSymbol> GetConstructors(this INamedTypeSymbol type)
        {
            if (type.TypeKind == TypeKind.Enum)
                return Enumerable.Empty<IMethodSymbol>();
            IEnumerable<IMethodSymbol> members = type.Constructors; //.Where(c=>c.CanBeReferencedByName);
            if (!GeneratorSettings.ShowPrivateMembers)
                members = members.Where(m => m.DeclaredAccessibility != Accessibility.Private);
            if (!GeneratorSettings.ShowInternalMembers)
                members = members.Where(m => m.DeclaredAccessibility != Accessibility.Internal);
            return members;
        }

        public static IEnumerable<Tuple<IMethodSymbol, bool>> GetConstructors(this INamedTypeSymbol type, INamedTypeSymbol oldType)
        {
            if (oldType == null || type == null)
                return GetConstructors(type ?? oldType).Select(p => new Tuple<IMethodSymbol, bool>(p, type == null));
            var newMembers = GetConstructors(type);
            var oldMembers = GetConstructors(oldType);
            return newMembers.Except(oldMembers, Generator.MethodComparer.Comparer).Select(p => new Tuple<IMethodSymbol, bool>(p, false))
                .Union(oldMembers.Except(newMembers, Generator.MethodComparer.Comparer).Select(p => new Tuple<IMethodSymbol, bool>(p, true)))
                .OrderBy(t => t.Item1.Name);
        }

        public static IEnumerable<IFieldSymbol> GetEnums(this INamedTypeSymbol type)
        {
            if (type.TypeKind != TypeKind.Enum)
                return new IFieldSymbol[] { };
            return type.GetAllMembers().OfType<IFieldSymbol>();
        }

        public static IEnumerable<Tuple<IFieldSymbol, bool>> GetEnums(this INamedTypeSymbol type, INamedTypeSymbol oldType)
        {
            if (oldType == null || type == null)
                return GetEnums(type ?? oldType).Select(p => new Tuple<IFieldSymbol, bool>(p, type == null));
            var newMembers = GetEnums(type);
            var oldMembers = GetEnums(oldType);
            return newMembers.Except(oldMembers, Generator.FieldComparer.Comparer).Select(p => new Tuple<IFieldSymbol, bool>(p, false))
                .Union(oldMembers.Except(newMembers, Generator.FieldComparer.Comparer).Select(p => new Tuple<IFieldSymbol, bool>(p, true)))
                .OrderBy(t => t.Item1.Name);
        }

        public static string GetFullTypeName(this ITypeSymbol type)
        {
            string name = type.Name;
            var ns = type.ContainingNamespace;
            while (ns != null && !ns.IsGlobalNamespace)
            {
                name = ns + "." + name;
                ns = ns.ContainingNamespace;
            }
            return name;
        }
        public static string GetFullNamespace(this ITypeSymbol type)
        {
            var name = "";
            var ns = type.ContainingNamespace;
            while (ns != null && !ns.IsGlobalNamespace)
            {
                if (string.IsNullOrEmpty(name))
                    name += ns.Name;
                else
                    name = ns.Name + "." + name;
                ns = ns.ContainingNamespace;
            }
            return name;
        }

        public static string GetFullTypeString(this INamedTypeSymbol type)
        {
            string result = type.Name;

            if (type.TypeArguments.Length > 0)
            {
                result += "<";

                bool isFirstIteration = true;
                foreach (INamedTypeSymbol typeArg in type.TypeArguments.OfType< INamedTypeSymbol>())
                {
                    if (isFirstIteration)
                    {
                        isFirstIteration = false;
                    }
                    else
                    {
                        result += ", ";
                    }

                    result += GetFullTypeString(typeArg);
                }

                result += ">";
            }

            return result;
        }

        public static string GetFullNamespace(this INamespaceSymbol ns)
        {
            if (ns.IsGlobalNamespace) return string.Empty;
            string name = ns.Name;
            ns = ns.ContainingNamespace;
            while (ns != null && !ns.IsGlobalNamespace)
            {
                name = ns + "." + name;
                ns = ns.ContainingNamespace;
            }
            return name;
        }
    }
}
