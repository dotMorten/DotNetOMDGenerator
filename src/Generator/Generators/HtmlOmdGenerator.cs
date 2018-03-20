using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Generator.Generators
{
    internal class HtmlOmdGenerator : ICodeGenerator, ICodeDiffGenerator
    {
        private System.IO.StreamWriter sw;
        private List<INamedTypeSymbol> allSymbols;
        private List<INamedTypeSymbol> oldSymbols;
        private INamespaceSymbol currentNamespace;

        public void Initialize(List<INamedTypeSymbol> allSymbols) => Initialize(allSymbols, null);
        public void Initialize(List<INamedTypeSymbol> allSymbols, List<INamedTypeSymbol> oldSymbols)
        {
            this.allSymbols = allSymbols;
            this.oldSymbols = oldSymbols;
            sw = new System.IO.StreamWriter("OMDs.html");
            using (var s = typeof(HtmlOmdGenerator).Assembly.GetManifestResourceStream("Generator.Generators.HtmlOmdHeader.html"))
            {
                s.CopyTo(sw.BaseStream);
            }
        }

        public void Complete()
        {
            sw.WriteLine("</body>\n</html>");
            sw.Flush();
            sw.Close();
            sw.Dispose();
        }

        public void WriteClass(INamedTypeSymbol type) => WriteClass(type, null);

        public void WriteClass(INamedTypeSymbol type, INamedTypeSymbol oldType = null)
        {
            WriteType(type, (type ?? oldType).TypeKind.ToString().ToLower(), oldType);
        }

        public void WriteType(INamedTypeSymbol type, string kind, INamedTypeSymbol oldType)
        {
            bool isTypeRemoved = type == null && oldType != null;
            if (isTypeRemoved)
                type = oldType;

            if (type.ContainingNamespace != currentNamespace)
            {
                var nsname = type.GetFullNamespace();
                currentNamespace = type.ContainingNamespace;
                sw.WriteLine($"<div class='namespaceHeader' id='{nsname}'>{nsname}</div>");
            }
            sw.WriteLine($"<div class='objectBox {(isTypeRemoved ? " typeRemoved'" : "")}' id='{type.GetFullTypeName()}'>");
            bool isEmpty = true;
            var memberBuilder = new StringBuilder();
            {
                //List out members
                if (type.GetConstructors(oldType).Any())
                {
                    isEmpty = false;
                    memberBuilder.AppendLine($"<div class='members'><h4>Constructors</h4><ul>");
                    foreach (var method in type.GetConstructors(oldType))
                    {
                        var str = FormatMember(method.Item1);
                        if (method.Item2)
                            str = $"<span class='memberRemoved'>{str}</span>";
                        memberBuilder.AppendLine($"{GetIcon(method.Item1, str)}");
                    }
                    memberBuilder.AppendLine("</ul></div>");
                }
                if (type.GetProperties(oldType).Any())
                {
                    isEmpty = false;
                    memberBuilder.AppendLine($"<div class='members'><h4>Properties</h4><ul>");
                    foreach (var method in type.GetProperties(oldType))
                    {
                        var str = FormatMember(method.Item1);
                        if (method.Item2)
                            str = $"<span class='memberRemoved'>{str}</span>";
                        memberBuilder.AppendLine($"{GetIcon(method.Item1, str)}");
                    }
                    memberBuilder.AppendLine("</ul></div>");
                }
                if (type.GetMethods(oldType).Any())
                {
                    isEmpty = false;
                    memberBuilder.AppendLine($"<div class='members'><h4>Methods</h4><ul>");
                    foreach (var method in type.GetMethods(oldType))
                    {
                        var str = FormatMember(method.Item1);
                        if (method.Item2)
                            str = $"<span class='memberRemoved'>{str}</span>";
                        memberBuilder.AppendLine($"{GetIcon(method.Item1, str)}");
                    }
                    memberBuilder.AppendLine("</ul></div>");
                }
                if (type.GetEvents(oldType).Any())
                {
                    isEmpty = false;
                    memberBuilder.AppendLine($"<div class='members'><h4>Events</h4><ul>");
                    foreach (var method in type.GetEvents(oldType))
                    {
                        var str = FormatMember(method.Item1);
                        if (method.Item2)
                            str = $"<span class='memberRemoved'>{str}</span>";
                        memberBuilder.AppendLine($"{GetIcon(method.Item1, str)}");
                    }
                    memberBuilder.AppendLine("</ul></div>");
                }
                if (type.TypeKind == TypeKind.Enum)
                {
                    isEmpty = false;
                    memberBuilder.AppendLine("<ul class='members'>");
                    foreach (var e in type.GetEnums(oldType))
                    {
                        string str = Briefify(e);
                        memberBuilder.AppendLine($"{GetIcon(e, str)}");
                    }
                    memberBuilder.AppendLine("</ul>");
                }
            }
            sw.WriteLine($"<div class='header {kind}{(isEmpty ? " noMembers" : "")}'>");

            //Write class name + Inheritance
            var brief = type.GetDescription();
            sw.Write($"<span ");
            if (!string.IsNullOrEmpty(brief))
                sw.Write($"title=\"{System.Web.HttpUtility.HtmlEncode(brief)}\"");
            sw.Write($">{System.Web.HttpUtility.HtmlEncode(type.Name)}");
            if (type.BaseType != null && type.BaseType.Name != "Object" && type.TypeKind != TypeKind.Enum)
            {
                if (oldType == null || type.BaseType.ToDisplayString() != oldType.BaseType.ToDisplayString())
                {
                    sw.Write(" : ");
                    if (oldType != null)
                    {
                        sw.Write($"<span class='memberRemoved'>{FormatType(oldType.BaseType)}</span>");
                    }
                    sw.Write(FormatType(type.BaseType));
                }
            }
            sw.WriteLine("</span>");
            //Document interfaces
            if (type.GetInterfaces(oldType).Any())
            {
                isEmpty = false;
                sw.Write("<br/>Implements ");
                int i = 0;
                foreach(var iface in type.GetInterfaces(oldType))
                {
                    if (i > 0)
                        sw.Write(", ");
                    if (iface.Item2) sw.Write("<span class='memberRemoved'>");
                    sw.Write(FormatType(iface.Item1));
                    if (iface.Item2) sw.Write("</span>");
                    i++;
                }
                sw.WriteLine("</span>");
            }
            sw.WriteLine("</div>"); //End header box

            sw.Write(memberBuilder.ToString());
            sw.WriteLine("</div>");
            sw.Flush();
        }

        private string GetIcon(ISymbol type, string content)
        {
            string icon = "";
            if (type.DeclaredAccessibility == Accessibility.Public)
                icon = "pub";
            else if (type.DeclaredAccessibility == Accessibility.Protected)
                icon = "prot";
            else if (type.DeclaredAccessibility == Accessibility.Private)
                icon = "priv";
            if (type.Kind == SymbolKind.Method)
                icon += "method";
            else if (type.Kind == SymbolKind.Property)
                icon += "property";
            else if (type.Kind == SymbolKind.Field)
                icon += "field";
            else if (type.Kind == SymbolKind.Event)
                icon += "event";
            if(type.IsStatic && type.ContainingType?.TypeKind != TypeKind.Enum)
            {
                content = "<span class='static'/>" + content;
            }
            if (icon == "")
                return content;
            return $"<li class='{icon}'>{content}</li>";
        }

        public void WriteEnum(INamedTypeSymbol enm) => WriteEnum(enm, null);
        public void WriteEnum(INamedTypeSymbol enm, INamedTypeSymbol oldType = null)
        {
            WriteType(enm, "enum", oldType);
        }

        public void WriteInterface(INamedTypeSymbol iface) => WriteInterface(iface, null);
        public void WriteInterface(INamedTypeSymbol iface, INamedTypeSymbol oldType = null)
        {
            WriteType(iface, "interface", oldType);
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
                        t += System.Web.HttpUtility.HtmlEncode(p.ToString());
                    else if (p.Symbol is ITypeSymbol its)
                        t += LinkifyType(its, false);
                    else
                    {

                    }
                }
                return t;
            }
            else {
                return LinkifyType(type);
            }
        }

        private string LinkifyType(ITypeSymbol type, bool includeGeneric = true)
        {
            if(type is INamedTypeSymbol nts && nts.IsGenericType && !includeGeneric)
            {
                type = nts.OriginalDefinition;
            }
            var name = includeGeneric ? type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat) : type.Name;
            if (allSymbols.Contains(type))
                return $"<a href='#{type.GetFullTypeName()}'>{System.Web.HttpUtility.HtmlEncode(name)}</a>";
            else 
                return Briefify(type, name);
        }

        private string FormatMember(ISymbol member)
        {
            var brief = member.GetDescription();
            var name = /* member.DeclaredAccessibility.ToString().ToLower() + " " + */ Briefify(member);

            if (member is IPropertySymbol)
            {
                var p = (IPropertySymbol)member;
                name += " { ";
                if (p.GetMethod != null)
                {
                    if (p.GetMethod.DeclaredAccessibility == Accessibility.Internal)
                    {
                        name += "internal ";
                    }
                    name += "get; ";
                }
                if (p.SetMethod != null)
                {
                    if (p.SetMethod.DeclaredAccessibility == Accessibility.Internal)
                    {
                        name += "internal ";
                    }
                    name += "set; ";
                }
                name += "} : " + FormatType(p.Type);
            }
            else if (member is IMethodSymbol)
            {
                var m = (member as IMethodSymbol);
                name += "(";
               if(m.Parameters.Any())
                {

                }
                name += string.Join(", ", m.Parameters.Select(p => FormatType(p.Type) + " " + Briefify(p)));
                name += ")";
                if (!m.ReturnsVoid)
                {
                    name += " : " + FormatType(m.ReturnType);
                }
            }
            else if (member is IEventSymbol)
            {
                var m = (member as IEventSymbol);
                name += " : " + FormatType(m.Type);
            }
            return name;
        }

        private static string Briefify(ISymbol symbol, string content = null)
        {
            if (content == null)
                content = symbol.Name;
            var brief = symbol.GetDescription();
            if (!string.IsNullOrEmpty(brief))
                return $"<span title=\"{System.Web.HttpUtility.HtmlEncode(brief)}\">{System.Web.HttpUtility.HtmlEncode(content)}</span>";
            return System.Web.HttpUtility.HtmlEncode(content);
        }
    }
}
