using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Generator.Generators
{
    internal class HtmlOmdGenerator : ICodeGenerator
    {
        private System.IO.StreamWriter sw;
        private List<INamedTypeSymbol> allSymbols;

        public void Initialize(List<INamedTypeSymbol> allSymbols)
        {
            this.allSymbols = allSymbols;
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

        public void WriteClass(INamedTypeSymbol type)
        {
            WriteType(type, type.TypeKind.ToString().ToLower());
        }

        public void WriteType(INamedTypeSymbol type, string kind)
        {
            sw.WriteLine($"<div class='objectBox' id='{type.GetFullTypeName()}'>");
            sw.WriteLine($"<div class='{kind}HeaderBox'>");
            var ns = type.GetFullNamespace();
            if(!string.IsNullOrEmpty(ns))
                sw.WriteLine($"<span>{type.GetFullNamespace()}</span><br/>"); //namespace
            //Write class name + Inheritance
            var brief = type.GetDescription();
            sw.Write($"<span class='objectHeader' ");
            if (!string.IsNullOrEmpty(brief))
                sw.Write($"title=\"{System.Web.HttpUtility.HtmlEncode(brief)}\"");
            sw.Write($">{System.Web.HttpUtility.HtmlEncode(type.Name)}");
            if (type.BaseType != null && type.BaseType.Name != "Object" && type.TypeKind != TypeKind.Enum)
            {
                sw.Write(" : " + FormatType(type.BaseType));
            }
            sw.WriteLine("</span>");
            
            //Document interfaces
            if (type.GetInterfaces().Any())
            {                
                sw.Write("<br/>Implements " + string.Join(", ", type.GetInterfaces().Select(i => FormatType(i))) + "</span>");
            }
            sw.WriteLine("</div>"); //End header box
            //List out members
            if (type.GetConstructors().Any())
            {
                sw.WriteLine($"<span class='memberGroup'>Constructors</span><ul>");
                foreach (var method in type.GetConstructors())
                {
                    var str = FormatMember(method);
                    sw.WriteLine($"<li>{str}</li>");
                }
                sw.WriteLine("</ul>");
            }
            if (type.GetProperties().Any())
            {
                sw.WriteLine($"<span class='memberGroup'>Properties</span><ul>");
                foreach (var method in type.GetProperties())
                {
                    var str = FormatMember(method);
                    sw.WriteLine($"<li>{str}</li>");
                }
                sw.WriteLine("</ul>");
            }
            if (type.GetMethods().Any())
            {
                sw.WriteLine($"<span class='memberGroup'>Methods</span><ul>");
                foreach (var method in type.GetMethods())
                {
                    var str = FormatMember(method);
                    sw.WriteLine($"<li>{str}</li>");
                }
                sw.WriteLine("</ul>");
            }
            if (type.GetEvents().Any())
            {
                sw.WriteLine($"<span class='memberGroup'>Events</span><ul>");
                foreach (var method in type.GetEvents())
                {
                    var str = FormatMember(method);
                    sw.WriteLine($"<li>{str}</li>");
                }
                sw.WriteLine("</ul>");
            }
            if (type.TypeKind == TypeKind.Enum)
            {
                sw.WriteLine("<ul>");
                foreach (var e in type.GetEnums())
                {
                    string str = Briefify(e);
                    sw.WriteLine($"<li>{str}</li>");
                }
                sw.WriteLine("</ul>");
            }
            sw.WriteLine("</div>");
            sw.Flush();
        }

        public void WriteEnum(INamedTypeSymbol enm)
        {
            WriteType(enm, "enum");
        }

        public void WriteInterface(INamedTypeSymbol iface)
        {
            WriteType(iface, "interface");
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
            var name = member.DeclaredAccessibility.ToString().ToLower() + " " + Briefify(member);

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
