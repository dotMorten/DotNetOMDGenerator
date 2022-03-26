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
                XmlElement elm = null;
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
                    elm = doc.GetElementsByTagName("param").OfType<XmlElement>().Where(e => e.Attributes["name"]?.Value == type.Name).FirstOrDefault();
                }
                else
                {
                    elm = doc.GetElementsByTagName("summary").OfType<XmlElement>().FirstOrDefault();
                }
                if(elm != null)
                {
                    foreach(var n in elm.ChildNodes.OfType<XmlElement>())
                    {
                        if(n.Name == "see")
                        {
                            // strip down xml <see cref="..."/> to just the type name
                            if (string.IsNullOrEmpty(n.InnerText))
                            {
                                var cref = n.GetAttribute("cref");
                                var idx = cref.LastIndexOf(".");
                                if (idx == -1)
                                    idx = cref.IndexOf(":");
                                if (idx > -1)
                                    n.InnerText = cref.Substring(idx + 1);
                            }
                        }
                    }
                }
                return elm?.InnerText.Trim();
            }
            return null;
        }

        private static IEnumerable<ISymbol> GetAllMembers(this INamedTypeSymbol type)
        {
            IEnumerable<ISymbol> members = type.GetMembers().Where(m => !m.IsOverride);
            if (!GeneratorSettings.ShowPrivateMembers)
                members = members.Where(m => m.DeclaredAccessibility != Accessibility.Private && m.DeclaredAccessibility != Accessibility.ProtectedAndFriend);
            if (!GeneratorSettings.ShowInternalMembers)
                members = members.Where(m => m.DeclaredAccessibility != Accessibility.Internal);
            return members;
        }
        internal static IEnumerable<INamedTypeSymbol> GetAllNestedTypes(this INamedTypeSymbol type)
        {
            if (type == null) return Enumerable.Empty<INamedTypeSymbol>();
            IEnumerable<INamedTypeSymbol> members = type.GetTypeMembers();
            if (!GeneratorSettings.ShowPrivateMembers)
                members = members.Where(m => m.DeclaredAccessibility != Accessibility.Private && m.DeclaredAccessibility != Accessibility.ProtectedAndFriend);
            if (!GeneratorSettings.ShowInternalMembers)
                members = members.Where(m => m.DeclaredAccessibility != Accessibility.Internal);
            return members;
        }

        public static IEnumerable<IMethodSymbol> GetMethods(this INamedTypeSymbol type)
        {
            if (type.TypeKind == TypeKind.Delegate)
                return Enumerable.Empty<IMethodSymbol>();
            return type.GetAllMembers().OfType<IMethodSymbol>()
                .Where(m => m.CanBeReferencedByName)
                .OrderBy(m => string.Join(',', m.Parameters.Select(p => p.Name))).OrderBy(m=>m.Name);
        }

        public static IEnumerable<(IMethodSymbol symbol, bool wasRemoved, bool wasObsoleted)> GetMethods(this INamedTypeSymbol type, INamedTypeSymbol oldType)
        {
            if (oldType == null || type == null)
            {
                return GetMethods(type ?? oldType).Select(p => (p, type == null, p.IsObsolete()));
                //foreach (var item in GetMethods(type ?? oldType).Select(p => (p, type == null, p.IsObsolete())))
                //    yield return item;
            }
            else
            {
                var newMembers = GetMethods(type).ToList();
                var oldMembers = GetMethods(oldType).ToList();
                var result = newMembers.Except(oldMembers, Generator.MethodComparer.Comparer).Select(p => (p, false, false))
                    .Union(oldMembers.Except(newMembers, Generator.MethodComparer.Comparer).Select(p => (p, true, false)))
                    .Union(newMembers.Where(n => n.IsObsolete() && oldMembers.Any(o => !o.IsObsolete() && Generator.MethodComparer.Comparer.Equals(o, n))).Select(p => (p, false, true))) //Obsoleted
                 .OrderBy(t => string.Join(',', t.Item1.Parameters.Select(p => p.Name))).OrderBy(t => t.Item1.Name);
                foreach (var item in result.ToArray())
                {
                    if (item.Item2 == true)//Item was removed. Check if it was just moved up to a base-class
                    {
                        if (item.p.IsOverride)
                            continue; //If override has been removed, just ignore, as it's not a removed method in that sense
                        var basetype = type.BaseType;
                        bool matchFound = false;
                        while (basetype != null && !matchFound)
                        {
                            var members = basetype.GetMembers(item.p.Name);
                            if (members.Any())
                            {
                                var identifier = item.p.ToDisplayString(Generator.Constants.AllFormatWithoutContaining);
                                if (members.OfType<IMethodSymbol>().Any(m=>identifier == m.ToDisplayString(Generator.Constants.AllFormatWithoutContaining)))
                                {
                                    matchFound = true;
                                    continue;
                                }
                            }
                            basetype = basetype.BaseType;
                        }
                        if (matchFound)
                        {
                            if (newMembers.Contains(item.p))
                                newMembers.Remove(item.p);
                            else if (oldMembers.Contains(item.p))
                                oldMembers.Remove(item.p);
                            continue;
                        }
                        //Check if optional was changed to explicit overloads
                        if (item.p.Parameters.Any(p => p.IsOptional) && newMembers.Any(n=>n.Name == item.p.Name))
                        {
                            bool found = true;
                            var newOverloads = newMembers.Where(n => n.Name == item.p.Name).ToList();
                            var start = item.p.Parameters.IndexOf(item.p.Parameters.First(item => item.IsOptional));
                            List<IMethodSymbol> foundNewMembers = new List<IMethodSymbol>();
                            for (int i = start; i <= item.p.Parameters.Length; i++)
                            {
                                var ps = item.p.Parameters.Take(i);
                                var matches = newOverloads.Where(n => n.Parameters.Length == i);
                                for (int j = 0; j < i; j++)
                                {
                                    matches = matches.Where(m =>  m.Parameters[j].Type.GetFullTypeName() == item.p.Parameters[j].Type.GetFullTypeName()).ToList();
                                }
                                if (matches.Count() == 1)
                                {
                                    foundNewMembers.Add(matches.Single());
                                }
                                else
                                    found = false;
                            }
                            if (found)
                            {
                                foreach(var member in foundNewMembers)
                                    newMembers.Remove(member);
                                if (oldMembers.Contains(item.p))
                                    oldMembers.Remove(item.p);
                            }
                        }
                    }
                }
                return result;
            }
        }

        public static IEnumerable<IPropertySymbol> GetProperties(this INamedTypeSymbol type)
        {
            return type.GetAllMembers().OfType<IPropertySymbol>().Where(m => m.CanBeReferencedByName).OrderBy(m=>m.Name);
        }

        public static IEnumerable<(IPropertySymbol symbol, bool wasRemoved, bool wasObsoleted)> GetProperties(this INamedTypeSymbol type, INamedTypeSymbol oldType)
        {
            if (oldType == null || type == null)
                return GetProperties(type ?? oldType).Select(p=>(p, type == null, p.IsObsolete()));
            var newProps = GetProperties(type);
            var oldProps = GetProperties(oldType);
            return newProps.Except(oldProps, Generator.PropertyComparer.Comparer).Select(p => (p, false, false))
                .Union(oldProps.Except(newProps, Generator.PropertyComparer.Comparer).Select(p => (p, true, false)))
                .Union(newProps.Where(n => n.IsObsolete() && oldProps.Any(o => !o.IsObsolete() && Generator.PropertyComparer.Comparer.Equals(o, n))).Select(p => (p, false, true))) //Obsoleted
                .OrderBy(t => t.Item1.Name);
        }

        public static IEnumerable<IFieldSymbol> GetFields(this INamedTypeSymbol type)
        {
            return type.GetAllMembers().OfType<IFieldSymbol>().Where(m => m.CanBeReferencedByName).OrderBy(m => m.Name);
        }

        public static IEnumerable<(IFieldSymbol symbol, bool wasRemoved, bool wasObsoleted)> GetFields(this INamedTypeSymbol type, INamedTypeSymbol oldType)
        {
            if (type.TypeKind == TypeKind.Enum)
                return Enumerable.Empty<(IFieldSymbol symbol, bool wasRemoved, bool wasObsoleted)> ();

            if (oldType == null || type == null)
                return GetFields(type ?? oldType).Select(p => (p, type == null, p.IsObsolete()));
            var newProps = GetFields(type);
            var oldProps = GetFields(oldType);
            return newProps.Except(oldProps, Generator.FieldComparer.Comparer).Select(p => (p, false, false))
                .Union(oldProps.Except(newProps, Generator.FieldComparer.Comparer).Select(p => (p, true, false)))
                .Union(newProps.Where(n => n.IsObsolete() && oldProps.Any(o => !o.IsObsolete() && Generator.FieldComparer.Comparer.Equals(o, n))).Select(p => (p, false, true))) //Obsoleted
                .OrderBy(t => t.Item1.Name);
        }

        public static IEnumerable<INamedTypeSymbol> GetInterfaces(this INamedTypeSymbol type)
        {
            IEnumerable<INamedTypeSymbol> i = type.Interfaces;
            if (!GeneratorSettings.ShowPrivateMembers)
                i = i.Where(m => m.DeclaredAccessibility != Accessibility.Private && m.DeclaredAccessibility != Accessibility.ProtectedAndFriend);
            if (!GeneratorSettings.ShowInternalMembers)
                i = i.Where(m => m.DeclaredAccessibility != Accessibility.Internal && m.DeclaredAccessibility != Accessibility.NotApplicable);
            return i;
        }

        public static IEnumerable<(INamedTypeSymbol symbol, bool wasRemoved, bool wasObsoleted)> GetInterfaces(this INamedTypeSymbol type, INamedTypeSymbol oldType)
        {
            if (oldType == null || type == null)
                return GetInterfaces(type ?? oldType).Select(p => (p, type == null, p.IsObsolete()));
            var newMembers = GetInterfaces(type);
            var oldMembers = GetInterfaces(oldType);
            return newMembers.Except(oldMembers, Generator.SymbolNameComparer.Comparer).Select(p => (p, false, false))
                .Union(oldMembers.Except(newMembers, Generator.SymbolNameComparer.Comparer).Select(p => (p, true, false)))
                .Union(newMembers.Where(n => n.IsObsolete() && oldMembers.Any(o => !o.IsObsolete() && Generator.SymbolNameComparer.Comparer.Equals(o, n))).Select(p => (p, false, true))) //Obsoleted
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
        public static bool IsObsolete(this ISymbol prop)
        {
            return prop.GetAttributes().Any(a => a.AttributeClass.GetFullTypeName() == "System.ObsoleteAttribute" || a.AttributeClass.GetFullTypeName() == "Obsolete");
        }
        public static IEnumerable<IEventSymbol> GetEvents(this INamedTypeSymbol type)
        {
            return type.GetAllMembers().OfType<IEventSymbol>().Where(m => m.CanBeReferencedByName).OrderBy(m => m.Name);
        }

        public static IEnumerable<(IEventSymbol symbol, bool wasRemoved, bool wasObsoleted)> GetEvents(this INamedTypeSymbol type, INamedTypeSymbol oldType)
        {
            if (oldType == null || type == null)
                return GetEvents(type ?? oldType).Select(p => (p, type == null, p.IsObsolete()));
            var newMembers = GetEvents(type);
            var oldMembers = GetEvents(oldType);
            return newMembers.Except(oldMembers, Generator.EventComparer.Comparer).Select(p => (p, false, false))
                .Union(oldMembers.Except(newMembers, Generator.EventComparer.Comparer).Select(p => (p, true, false)))
                .Union(newMembers.Where(n => n.IsObsolete() && oldMembers.Any(o => !o.IsObsolete() && Generator.EventComparer.Comparer.Equals(o, n))).Select(p => (p, false, true))) //Obsoleted
                .OrderBy(t => t.Item1.Name);
        }

        public static IEnumerable<IMethodSymbol> GetConstructors(this INamedTypeSymbol type)
        {
            if (type.TypeKind == TypeKind.Enum)
                return Enumerable.Empty<IMethodSymbol>();
            IEnumerable<IMethodSymbol> members = type.Constructors; //.Where(c=>c.CanBeReferencedByName);
            if (!GeneratorSettings.ShowPrivateMembers)
                members = members.Where(m => m.DeclaredAccessibility != Accessibility.Private && m.DeclaredAccessibility != Accessibility.ProtectedAndFriend);
            if (!GeneratorSettings.ShowInternalMembers)
                members = members.Where(m => m.DeclaredAccessibility != Accessibility.Internal);
            return members.OrderBy(m => string.Join(',', m.Parameters.Select(p => p.Name)));
        }

        public static IEnumerable<(IMethodSymbol symbol, bool wasRemoved, bool wasObsoleted)> GetConstructors(this INamedTypeSymbol type, INamedTypeSymbol oldType)
        {
            if (oldType == null || type == null)
                return GetConstructors(type ?? oldType).Select(p => (p, type == null, p.IsObsolete()));
            var newMembers = GetConstructors(type).ToList();
            var oldMembers = GetConstructors(oldType).ToList();
            var result = newMembers.Except(oldMembers, Generator.MethodComparer.Comparer).Select(p => (p, false, false))
                .Union(oldMembers.Except(newMembers, Generator.MethodComparer.Comparer).Select(p => (p, true, false)))
                .Union(newMembers.Where(n => n.IsObsolete() && oldMembers.Any(o => !o.IsObsolete() && Generator.MethodComparer.Comparer.Equals(o, n))).Select(p => (p, false, true))) //Obsoleted
                .OrderBy(t => t.Item1.Name);
            foreach (var item in result.ToArray())
            {
                if (item.Item2 == true)//Item was removed. Check if it was just moved up to a base-class
                {
                    //Check if optional was changed to explicit overloads
                    if (item.p.Parameters.Any(p => p.IsOptional) && newMembers.Any(n => n.Name == item.p.Name))
                    {
                        bool found = true;
                        var newOverloads = newMembers.Where(n => n.Name == item.p.Name).ToList();
                        var start = item.p.Parameters.IndexOf(item.p.Parameters.First(item => item.IsOptional));
                        List<IMethodSymbol> foundNewMembers = new List<IMethodSymbol>();
                        for (int i = start; i <= item.p.Parameters.Length; i++)
                        {
                            var ps = item.p.Parameters.Take(i);
                            var matches = newOverloads.Where(n => n.Parameters.Length == i);
                            for (int j = 0; j < i; j++)
                            {
                                matches = matches.Where(m => m.Parameters[j].Type.GetFullTypeName() == item.p.Parameters[j].Type.GetFullTypeName()).ToList();
                            }
                            if (matches.Count() == 1)
                            {
                                foundNewMembers.Add(matches.Single());
                            }
                            else
                                found = false;
                        }
                        if (found)
                        {
                            foreach (var member in foundNewMembers)
                                newMembers.Remove(member);
                            if (oldMembers.Contains(item.p))
                                oldMembers.Remove(item.p);
                        }
                    }
                }
            }
            return result;
        }

        public static IEnumerable<IFieldSymbol> GetEnums(this INamedTypeSymbol type)
        {
            if (type.TypeKind != TypeKind.Enum)
                return new IFieldSymbol[] { };
            return type.GetAllMembers().OfType<IFieldSymbol>().OrderBy(f => f.ConstantValue);
        }

        public static IEnumerable<(IFieldSymbol symbol, bool wasRemoved, bool wasObsoleted)> GetEnums(this INamedTypeSymbol type, INamedTypeSymbol oldType)
        {
            if (oldType == null || type == null)
                return GetEnums(type ?? oldType).Select(p => (p, type == null, p.IsObsolete()));
            var newMembers = GetEnums(type);
            var oldMembers = GetEnums(oldType);
            return newMembers.Except(oldMembers, Generator.FieldComparer.Comparer).Select(p => (p, false, false))
                .Union(oldMembers.Except(newMembers, Generator.FieldComparer.Comparer).Select(p => (p, true, false)))
                .Union(newMembers.Where(n => n.IsObsolete() && oldMembers.Any(o => !o.IsObsolete() && Generator.FieldComparer.Comparer.Equals(o, n))).Select(p => (p, false, true))) //Obsoleted
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
