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
            return type.GetAllMembers().OfType<IMethodSymbol>().Where(m => m.CanBeReferencedByName);
        }

        public static IEnumerable<IPropertySymbol> GetProperties(this INamedTypeSymbol type)
        {
            return type.GetAllMembers().OfType<IPropertySymbol>().Where(m => m.CanBeReferencedByName);
        }

        public static IEnumerable<INamedTypeSymbol> GetInterfaces(this INamedTypeSymbol type)
        {
            IEnumerable<INamedTypeSymbol> i = type.Interfaces;
            if (type.Interfaces.Length > 1)
            {
                if (type.Interfaces.Any(iface => iface.Name.Contains("ICoreCallback_NMEADataSource_LocationChanged")))
                {

                }

            }
            if (!GeneratorSettings.ShowPrivateMembers)
                i = i.Where(m => m.DeclaredAccessibility != Accessibility.Private);
            if (!GeneratorSettings.ShowInternalMembers)
                i = i.Where(m => m.DeclaredAccessibility != Accessibility.Internal && m.DeclaredAccessibility != Accessibility.NotApplicable);
            return i;
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
        public static IEnumerable<IMethodSymbol> GetConstructors(this INamedTypeSymbol type)
        {
            var members = type.Constructors.Where(c=>c.CanBeReferencedByName);
            if (!GeneratorSettings.ShowPrivateMembers)
                members = members.Where(m => m.DeclaredAccessibility != Accessibility.Private);
            if (!GeneratorSettings.ShowInternalMembers)
                members = members.Where(m => m.DeclaredAccessibility != Accessibility.Internal);
            return members;
        }

        public static IEnumerable<ISymbol> GetEnums(this INamedTypeSymbol type)
        {
            if (type.TypeKind != TypeKind.Enum)
                return new ISymbol[] { };
            return type.GetAllMembers().Where(m => m.Kind == SymbolKind.Field);
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
