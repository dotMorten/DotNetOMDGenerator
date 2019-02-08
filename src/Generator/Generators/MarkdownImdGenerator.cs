using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Generator.Generators
{
    public class MarkdownImdGenerator : ICodeGenerator, ICodeDiffGenerator
    {
        private System.IO.StreamWriter sw;
        private List<INamedTypeSymbol> allSymbols;
        private List<INamedTypeSymbol> oldSymbols;
        private string currentNamespace;

        public void Initialize(List<INamedTypeSymbol> allSymbols) => Initialize(allSymbols, null);
        public void Initialize(List<INamedTypeSymbol> allSymbols, List<INamedTypeSymbol> oldSymbols)
        {
            this.allSymbols = allSymbols;
            this.oldSymbols = oldSymbols;
            var outLocation = GeneratorSettings.OutputLocation;
            var fi = new System.IO.FileInfo(outLocation);
            if (!fi.Directory.Exists)
            {
                throw new System.IO.DirectoryNotFoundException(fi.Directory.FullName);
            }
            if (fi.Attributes == System.IO.FileAttributes.Directory)
                outLocation = System.IO.Path.Combine(outLocation, "OMD.md");
            sw = new System.IO.StreamWriter(outLocation);
            //using (var s = typeof(HtmlOmdGenerator).Assembly.GetManifestResourceStream("Generator.Generators.HtmlOmdHeader.html"))
            //{
            //    s.CopyTo(sw.BaseStream);
            //}
        }

        public void Complete()
        {
            if (currentNamespace != null)
            {
                //close the last namespace section
                sw.WriteLine("}");
                sw.Flush();
            }
            sw.Flush();
            sw.Close();
            sw.Dispose();
        }


        public void WriteClass(INamedTypeSymbol type) => WriteType(type, null, false);

        public void WriteClass(INamedTypeSymbol type, INamedTypeSymbol oldType = null)
        {
            WriteType(type, oldType, true);
        }

        private void WriteLine(string line, int indent = 0)
        {
            sw.Write("> ");
            for (int i = 0; i < indent; i++)
            {
                line = "\t" + line.Replace("\n", "\n\t");
            }
            line = line.Replace("\n", "\n> ");
            line = line.Replace("  ", "&nbsp;&nbsp;");
            line = line.Replace("\t", "&nbsp;&nbsp;&nbsp;&nbsp;");
            sw.Write(line);
            sw.WriteLine();
        }

        public void WriteType(INamedTypeSymbol type, INamedTypeSymbol oldType, bool isComparison, int indent = 0)
        {
            bool isTypeRemoved = type == null && oldType != null;
            bool isTypeNew = type != null && oldType == null && isComparison;
            if (isTypeRemoved)
                type = oldType;

            string kind = "";
            switch (type.TypeKind)
            {
                case TypeKind.Struct:
                case TypeKind.Class: kind = "class"; break;
                case TypeKind.Delegate: kind = "delegate"; break;
                case TypeKind.Enum: kind = "enum"; break;
                case TypeKind.Interface: kind = "interface"; break;
                default:
                    return; //Not supported
            }

            var nsname = type.GetFullNamespace();
            if (nsname != currentNamespace)
            {
                if (currentNamespace != null)
                {
                    //close the current namespace section
                    sw.WriteLine("}");
                }
                WriteLine($"namespace {nsname}\n{{");
                currentNamespace = nsname;
            }

            string className = type.GetFullTypeName();


            var symbols = Generator.GetChangedSymbols(
                type == oldType ? Enumerable.Empty<INamedTypeSymbol>() : type.GetAllNestedTypes(),
                oldType == null ? Enumerable.Empty<INamedTypeSymbol>() : oldType.GetAllNestedTypes());
            //sw.WriteLine($"<div class='header {kind}{(isEmpty ? " noMembers" : "")}'>");

            //Write class name + Inheritance
            //var brief = type.GetDescription();
            //sw.Write($"<span ");
            //brief = $"{type.ToDisplayString(Generator.Constants.AllFormat)}\b{brief}";
            //if (!string.IsNullOrEmpty(brief))
            //    sw.Write($"title=\"{System.Web.HttpUtility.HtmlEncode(brief)}\"");
            //sw.Write($">{System.Web.HttpUtility.HtmlEncode(type.Name)}");
            if (type.BaseType != null && type.BaseType.Name != "Object" && type.TypeKind != TypeKind.Enum)
            {
                if (oldType == null || type.BaseType.ToDisplayString() != oldType.BaseType.ToDisplayString())
                {
                    className += (" : ");
                    if (oldType != null && !isTypeRemoved)
                    {
                        className += $"~~{FormatType(oldType.BaseType)}~~";
                    }
                    else
                        className += FormatType(type.BaseType);
                }
            }

            //Document interfaces
            if (type.GetInterfaces(oldType).Any())
            {
                foreach (var iface in type.GetInterfaces(oldType))
                {
                    if (className.Contains(" : "))
                        className += ", ";
                    else
                        className += " : ";
                    var typeName = FormatType(iface.symbol);
                    if (iface.wasRemoved && !isTypeRemoved) typeName = $"~~{typeName}~~";
                    className += typeName;
                }
            }
            if (isTypeRemoved)
            {
                WriteLine($"~~{className}~~", indent + 1);
                return;
            }


            WriteLine($"{(!isTypeNew ? "+" : "")}' {className}\n{{", indent + 1);
            //List out members
            if (type.GetConstructors(oldType).Any())
            {
                foreach (var method in type.GetConstructors(oldType))
                {
                    var str = FormatMember(method.symbol);
                    if (method.wasRemoved)
                        str = $"~~{str}~~";
                    WriteLine(str, indent + 2);
                }
            }
            if (type.GetProperties(oldType).Any())
            {
                foreach (var method in type.GetProperties(oldType))
                {
                    var str = FormatMember(method.symbol);
                    if (method.wasRemoved)
                    {
                        if (method.wasRemoved)
                            str = $"~~{str}~~";
                    }
                    WriteLine(str, indent + 2);
                }
            }
            if (type.GetMethods(oldType).Any())
            {
                foreach (var method in type.GetMethods(oldType))
                {
                    var str = FormatMember(method.symbol);
                    if (method.wasRemoved)
                        str = $"~~{str}~~";
                    WriteLine(str, indent + 2);
                }
            }
            if (type.GetEvents(oldType).Any())
            {
                foreach (var method in type.GetEvents(oldType))
                {
                    var str = FormatMember(method.symbol);
                    if (method.wasRemoved)
                        str = $"~~{str}~~";
                    WriteLine(str, indent + 2);
                }
            }
            if (type.GetFields(oldType).Any())
            {
                foreach (var method in type.GetFields(oldType))
                {
                    var str = FormatMember(method.symbol);
                    if (method.wasRemoved)
                        str = $"~~{str}~~";
                    WriteLine(str, indent + 2);
                }
            }

            if (type.TypeKind == TypeKind.Enum)
            {
                foreach (var e in type.GetEnums(oldType))
                {
                    string str = Briefify(e.symbol);
                    if (e.symbol.HasConstantValue)
                        str += " = " + e.symbol.ConstantValue?.ToString();
                    if (e.wasRemoved)
                        str = $"~~{str}~~";
                    WriteLine(str, indent + 2);
                }

            }

            if (symbols.Any())
            {
                foreach (var t in symbols)
                {
                    WriteType(t.newSymbol, t.oldSymbol, isComparison, indent + 2);
                }
            }

            WriteLine("}");
            sw.Flush();
        }

        public void WriteEnum(INamedTypeSymbol enm) => WriteType(enm, null, false);
        public void WriteEnum(INamedTypeSymbol enm, INamedTypeSymbol oldType = null)
        {
            WriteType(enm, oldType, true);
        }

        public void WriteInterface(INamedTypeSymbol iface) => WriteType(iface, null, false);
        public void WriteInterface(INamedTypeSymbol iface, INamedTypeSymbol oldType = null)
        {
            WriteType(iface, oldType, true);
        }
        public void WriteDelegate(INamedTypeSymbol del) => WriteType(del, null, false);

        public void WriteDelegate(INamedTypeSymbol del, INamedTypeSymbol oldDel = null)
        {
            WriteType(del, oldDel, true);
        }

        private string FormatType(ITypeSymbol type)
        {
            var f = SymbolDisplayFormat.MinimallyQualifiedFormat;
            f.AddGenericsOptions(SymbolDisplayGenericsOptions.IncludeTypeParameters);
            var parts = type.ToDisplayParts(f);
            if (parts.Length > 1)
            {
                string t = "";
                foreach (var p in parts)
                {
                    if (p.Kind == SymbolDisplayPartKind.Punctuation || p.Kind == SymbolDisplayPartKind.Space)
                        t += p; // System.Web.HttpUtility.HtmlEncode(p.ToString());
                    else if (p.Symbol is ITypeSymbol its)
                        t += LinkifyType(its, false);
                    else
                    {

                    }
                }
                return t;
            }
            else
            {
                return LinkifyType(type);
            }
        }

        private string LinkifyType(ITypeSymbol type, bool includeGeneric = true)
        {
            if (type is INamedTypeSymbol nts && nts.IsGenericType && !includeGeneric)
            {
                type = nts.OriginalDefinition;
            }
            var name = includeGeneric ? type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat) : type.Name;
            //if (allSymbols.Contains(type))
            //    return $"<a href='#{type.GetFullTypeName()}'>{System.Web.HttpUtility.HtmlEncode(name)}</a>";
            //else
                return Briefify(type, name);
        }

        private static string AccessorToString(Accessibility a)
        {
            switch (a)
            {
                case Accessibility.Public:
                    return "public";
                case Accessibility.Private:
                    return "private";
                case Accessibility.Internal:
                    return "internal";
                case Accessibility.Protected:
                    return "protected";
                case Accessibility.ProtectedOrInternal:
                    return GeneratorSettings.ShowInternalMembers ? "protected internal" : "protected";
                case Accessibility.ProtectedAndInternal:
                    return "private protected";
                default:
                    return string.Empty;
            }
        }

        private string FormatMember(ISymbol member)
        {
            var brief = member.GetDescription();
            var name = member.Name;
            if (name == ".ctor")
            {
                name = member.ContainingType.Name;
            }
            name = Briefify(member, name);

            if (member is IPropertySymbol p)
            {
                name += " { ";
                if (p.GetMethod != null && ((p.GetMethod.DeclaredAccessibility == Accessibility.Public || p.GetMethod.DeclaredAccessibility == Accessibility.Protected || p.GetMethod.DeclaredAccessibility == Accessibility.ProtectedOrInternal) ||
                    (GeneratorSettings.ShowInternalMembers && p.GetMethod.DeclaredAccessibility == Accessibility.Internal || p.GetMethod.DeclaredAccessibility == Accessibility.ProtectedAndInternal) ||
                    (GeneratorSettings.ShowPrivateMembers && p.GetMethod.DeclaredAccessibility == Accessibility.Private)))
                {
                    if (p.DeclaredAccessibility != p.GetMethod.DeclaredAccessibility)
                    {
                        name += AccessorToString(p.GetMethod.DeclaredAccessibility) + " ";
                    }
                    name += "get; ";
                }
                if (p.SetMethod != null && ((p.SetMethod.DeclaredAccessibility == Accessibility.Public || p.SetMethod.DeclaredAccessibility == Accessibility.Protected || p.SetMethod.DeclaredAccessibility == Accessibility.ProtectedOrInternal) ||
                   (GeneratorSettings.ShowInternalMembers && p.SetMethod.DeclaredAccessibility == Accessibility.Internal || p.SetMethod.DeclaredAccessibility == Accessibility.ProtectedAndInternal) ||
                   (GeneratorSettings.ShowPrivateMembers && p.SetMethod.DeclaredAccessibility == Accessibility.Private)))
                {
                    if (p.DeclaredAccessibility != p.SetMethod.DeclaredAccessibility)
                    {
                        name += AccessorToString(p.SetMethod.DeclaredAccessibility) + " ";
                    }
                    name += "set; ";
                }

                name += "} : " + FormatType(p.Type);
            }
            else if (member is IMethodSymbol m)
            {
                if (m.TypeArguments.Any())
                {
                    name += System.Web.HttpUtility.HtmlEncode("<" + string.Join(", ", m.TypeArguments.Select(t => t.ToDisplayString())) + ">");
                }

                name += "(";
                name += string.Join(", ", m.Parameters.Select(pr => FormatType(pr.Type) + " " + Briefify(pr) + (pr.HasExplicitDefaultValue ? (" = " + (pr.ExplicitDefaultValue?.ToString() ?? "null")) : "")));
                name += ")";
                if (!m.ReturnsVoid)
                {
                    name += " : " + FormatType(m.ReturnType);
                }
            }
            else if (member is IEventSymbol e)
            {
                name += " : " + FormatType(e.Type);
            }
            else if (member is IFieldSymbol f)
            {
                name += " : " + FormatType(f.Type);
            }
            return name;
        }

        private static string Briefify(ISymbol symbol, string content = null)
        {
            if (content == null)
                content = symbol.Name;
            return content;
            //var brief = symbol.GetDescription();
            //if (!string.IsNullOrEmpty(brief))
            //    return $"<span title=\"{System.Web.HttpUtility.HtmlEncode(brief)}\">{System.Web.HttpUtility.HtmlEncode(content)}</span>";
            //return System.Web.HttpUtility.HtmlEncode(content);
        }
    }
}
