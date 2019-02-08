using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Generator.Generators
{
    public class MarkdownGenerator : ICodeGenerator, ICodeDiffGenerator
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
            WriteLine("<pre>", 0);
        }

        public void Complete()
        {
            if (currentNamespace != null)
            {
                //close the last namespace section
                WriteLine("}</pre>", 0);
                WriteLine("Generated with (.NET Object Model Diagram Generator)[https://github.com/dotMorten/DotNetOMDGenerator]", 0);
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

        private void WriteLine(string line, int indent)
        {
            sw.Write("> ");
            if (!string.IsNullOrEmpty(line))
            {
                for (int i = 0; i < indent; i++)
                {
                    line = "\t" + line.Replace("\n", "\n\t");
                }
                line = line.Replace("\n", "\n> ");
                line = line.Replace("\t", "    ");
                sw.Write(line);
            }
            sw.WriteLine();
        }
        private const string RemoveStart = "<strike>";
        private const string RemoveEnd = "</strike>";
        private const string AddedStart = "<b>";
        private const string AddedEnd = "</b>";
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
                case TypeKind.Class:
                    kind = "class";
                    if (type.IsSealed && (oldType == null || oldType.IsSealed)) kind = "sealed " + kind;
                    else if (type.IsSealed && !oldType.IsSealed) kind = $"{AddedStart}sealed{AddedEnd} {kind}";
                    else if (!type.IsSealed && oldType != null && oldType.IsSealed) kind = $"{RemoveStart}sealed{RemoveEnd} {kind}";

                    if (type.IsStatic && (oldType == null || oldType.IsStatic)) kind = "static " + kind;
                    else if (type.IsStatic && !oldType.IsStatic) kind = $"{AddedStart}static{AddedEnd} {kind}";
                    else if (!type.IsStatic && oldType != null && oldType.IsStatic) kind = $"{RemoveStart}static{RemoveEnd} {kind}";
                    break;
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
                    WriteLine("}", indent);
                }
                WriteLine($"namespace {nsname}\n{{", indent);
                currentNamespace = nsname;
            }
            
            string className = AccessorToString(type.DeclaredAccessibility) + " " + kind + " " + type.GetFullTypeName();

            var symbols = Generator.GetChangedSymbols(
                type == oldType ? Enumerable.Empty<INamedTypeSymbol>() : type.GetAllNestedTypes(),
                oldType == null ? Enumerable.Empty<INamedTypeSymbol>() : oldType.GetAllNestedTypes());
            if (type.BaseType != null && type.BaseType.Name != "Object" && type.TypeKind != TypeKind.Enum)
            {
                if (oldType == null || type.BaseType.ToDisplayString() != oldType.BaseType.ToDisplayString())
                {
                    className += (" : ");
                    if (oldType != null && !isTypeRemoved)
                    {
                        className += $"{RemoveStart}{FormatType(oldType.BaseType)}{RemoveEnd}"; //removed baseclass
                    }
                    else if(oldType == null && isComparison && !isTypeNew)
                        className += $"{AddedStart}{FormatType(type.BaseType)}{AddedEnd}"; //new baseclass
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
                    if (iface.wasRemoved && !isTypeRemoved)
                        typeName = $"{RemoveStart}{typeName}{RemoveEnd}";
                    else if (isComparison && !isTypeNew)
                        className += $"{AddedStart}{typeName}{AddedEnd}";
                    else
                        className += typeName;
                }
            }

            if (isTypeRemoved)
            {
                WriteLine($"{RemoveStart}{className} {{ ... }}{RemoveEnd}", indent + 1);
                return;
            }


            WriteLine($"{(isTypeNew && isComparison ? AddedStart : "")}{className}", indent + 1);
            WriteLine("{", indent + 1); //Begin class
            //List out members
            if (type.GetConstructors(oldType).Any())
            {
                foreach (var method in type.GetConstructors(oldType))
                {
                    var str = FormatMember(method.symbol);
                    if (method.wasRemoved)
                        str = $"{RemoveStart}{str}{RemoveEnd}";
                    else if (isComparison && !isTypeNew)
                        str = $"{AddedStart}{str}{AddedEnd}";
                    WriteLine(str, indent + 2);
                }
            }
            if (type.GetProperties(oldType).Any())
            {
                foreach (var method in type.GetProperties(oldType))
                {
                    var str = FormatMember(method.symbol);
                    if (method.wasRemoved)
                        str = $"{RemoveStart}{str}{RemoveEnd}";
                    else if (isComparison && !isTypeNew)
                        str = $"{AddedStart}{str}{AddedEnd}";
                    WriteLine(str, indent + 2);
                }
            }
            if (type.GetMethods(oldType).Any())
            {
                foreach (var method in type.GetMethods(oldType))
                {
                    var str = FormatMember(method.symbol);
                    if (method.wasRemoved)
                        str = $"{RemoveStart}{str}{RemoveEnd}";
                    else if (isComparison && !isTypeNew)
                        str = $"{AddedStart}{str}{AddedEnd}";
                    WriteLine(str, indent + 2);
                }
            }
            if (type.GetEvents(oldType).Any())
            {
                foreach (var method in type.GetEvents(oldType))
                {
                    var str = FormatMember(method.symbol);
                    if (method.wasRemoved)
                        str = $"{RemoveStart}{str}{RemoveEnd}";
                    else if (isComparison && !isTypeNew)
                        str = $"{AddedStart}{str}{AddedEnd}";
                    WriteLine(str, indent + 2);
                }
            }
            if (type.GetFields(oldType).Any())
            {
                foreach (var method in type.GetFields(oldType))
                {
                    var str = FormatMember(method.symbol);
                    if (method.wasRemoved)
                        str = $"{RemoveStart}{str}{RemoveEnd}";
                    else if (isComparison && !isTypeNew)
                        str = $"{AddedStart}{str}{AddedEnd}";
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
                        str = $"{RemoveStart}{str}{RemoveEnd}";
                    else if (isComparison && !isTypeNew)
                        str = $"{AddedStart}{str}{AddedEnd}";
                    WriteLine(str, indent + 2);
                }

            }

            if (symbols.Any())
            {
                WriteLine(null, 0);
                foreach (var t in symbols)
                {
                    WriteType(t.newSymbol, t.oldSymbol, isComparison && !isTypeNew, indent + 1);
                }
            }

            WriteLine("}" + (isTypeNew && isComparison ? AddedEnd:""), indent + 1); //EndClass
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
            string accessor = AccessorToString(member.DeclaredAccessibility);
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

                name = FormatType(p.Type) + " " + name + " }";
            }
            else if (member is IMethodSymbol m)
            {
                if (m.TypeArguments.Any())
                {
                    name += "&lt;" + string.Join(", ", m.TypeArguments.Select(t => t.ToDisplayString())) + "&gt;";
                }

                name += "(";
                name += string.Join(", ", m.Parameters.Select(pr => FormatType(pr.Type) + " " + Briefify(pr) + (pr.HasExplicitDefaultValue ? (" = " + (pr.ExplicitDefaultValue?.ToString() ?? "null")) : "")));
                name += ");";
                if (!m.ReturnsVoid)
                {
                    name = FormatType(m.ReturnType) + " " + name;
                }
                
            }
            else if (member is IEventSymbol e)
            {
                name = FormatType(e.Type) + " " + name;
            }
            else if (member is IFieldSymbol f)
            {
                name = FormatType(f.Type) + " " + name;
            }
            if (member.ContainingType.TypeKind == TypeKind.Interface)
                return name; //Don't add accessor to interface members
            return accessor + " " + name;
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
