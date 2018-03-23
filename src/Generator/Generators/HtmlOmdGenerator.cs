﻿using System;
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
        private string currentNamespace;

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
            if (currentNamespace != null)
            {
                //close the last namespace section
                sw.WriteLine("</div></section>");
                sw.Flush();
            }
            using (var s = typeof(HtmlOmdGenerator).Assembly.GetManifestResourceStream("Generator.Generators.HtmlOmdFooter.html"))
            {
                s.CopyTo(sw.BaseStream);
            }
            sw.Flush();
            sw.Close();
            sw.Dispose();
        }

        public void WriteClass(INamedTypeSymbol type) => WriteClass(type, null);

        public void WriteClass(INamedTypeSymbol type, INamedTypeSymbol oldType = null)
        {
            WriteType(type, oldType);
        }

        public void WriteType(INamedTypeSymbol type, INamedTypeSymbol oldType)
        {
            bool isTypeRemoved = type == null && oldType != null;
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
                if(currentNamespace != null)
                {
                    //close the current namespace section
                    sw.WriteLine("</div></section>");
                }
                sw.WriteLine($"<section id='{nsname}'>");
                currentNamespace = nsname;
                sw.WriteLine($"<h3 class='expander active'>{nsname}</h3><div>");
            }
            sw.WriteLine($"<div class='objectBox{(isTypeRemoved ? " typeRemoved" : "")}' id='{type.GetFullTypeName()}'>");
            bool isEmpty = true;
            var memberBuilder = new StringBuilder();
            {
                //List out members
                if (type.GetConstructors(oldType).Any())
                {
                    isEmpty = false;

                    memberBuilder.AppendLine($"<div class='members'>");
                    if (type.TypeKind != TypeKind.Delegate)
                        memberBuilder.AppendLine($"<h4>Constructors</h4>");
                    memberBuilder.AppendLine($"<ul>");
                    foreach (var method in type.GetConstructors(oldType))
                    {
                        var str = FormatMember(method.symbol);
                        if (method.wasRemoved)
                            str = $"<span class='memberRemoved'>{str}</span>";
                        memberBuilder.AppendLine($"{GetIcon(method.symbol, str)}");
                    }
                    memberBuilder.AppendLine("</ul></div>");
                }
                if (type.GetProperties(oldType).Any())
                {
                    isEmpty = false;
                    memberBuilder.AppendLine($"<div class='members'><h4>Properties</h4><ul>");
                    foreach (var method in type.GetProperties(oldType))
                    {
                        var str = FormatMember(method.symbol);
                        if (method.wasRemoved)
                            str = $"<span class='memberRemoved'>{str}</span>";
                        memberBuilder.AppendLine($"{GetIcon(method.symbol, str)}");
                    }
                    memberBuilder.AppendLine("</ul></div>");
                }
                if (type.GetMethods(oldType).Any())
                {
                    isEmpty = false;
                    memberBuilder.AppendLine($"<div class='members'><h4>Methods</h4><ul>");
                    foreach (var method in type.GetMethods(oldType))
                    {
                        var str = FormatMember(method.symbol);
                        if (method.wasRemoved)
                            str = $"<span class='memberRemoved'>{str}</span>";
                        memberBuilder.AppendLine($"{GetIcon(method.symbol, str)}");
                    }
                    memberBuilder.AppendLine("</ul></div>");
                }
                if (type.GetEvents(oldType).Any())
                {
                    isEmpty = false;
                    memberBuilder.AppendLine($"<div class='members'><h4>Events</h4><ul>");
                    foreach (var method in type.GetEvents(oldType))
                    {
                        var str = FormatMember(method.symbol);
                        if (method.wasRemoved)
                            str = $"<span class='memberRemoved'>{str}</span>";
                        memberBuilder.AppendLine($"{GetIcon(method.symbol, str)}");
                    }
                    memberBuilder.AppendLine("</ul></div>");
                }
                if (type.GetFields(oldType).Any())
                {
                    isEmpty = false;
                    memberBuilder.AppendLine($"<div class='members'><h4>Fields</h4><ul>");
                    foreach (var method in type.GetFields(oldType))
                    {
                        var str = FormatMember(method.symbol);
                        if (method.wasRemoved)
                            str = $"<span class='memberRemoved'>{str}</span>";
                        memberBuilder.AppendLine($"{GetIcon(method.symbol, str)}");
                    }
                    memberBuilder.AppendLine("</ul></div>");
                }
                if (type.TypeKind == TypeKind.Enum)
                {
                    isEmpty = false;
                    memberBuilder.AppendLine("<ul class='members'>");
                    foreach (var e in type.GetEnums(oldType))
                    {
                        string str = Briefify(e.symbol);
                        if(e.symbol.HasConstantValue)
                            str += " = " + e.symbol.ConstantValue?.ToString();
                        if (e.wasRemoved)
                            str = $"<span class='memberRemoved'>{str}</span>";
                        memberBuilder.AppendLine($"{GetIcon(e.symbol, str)}");
                    }
                    memberBuilder.AppendLine("</ul>");
                }
            }

            var symbols = Generator.GetChangedSymbols(
                type == oldType ? Enumerable.Empty<INamedTypeSymbol>() : type.GetAllNestedTypes(),
                oldType == null ? Enumerable.Empty<INamedTypeSymbol>() : oldType.GetAllNestedTypes());
            if (isEmpty && symbols.Any())
            {
                isEmpty = false;
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
                    if (iface.wasRemoved) sw.Write("<span class='memberRemoved'>");
                    sw.Write(FormatType(iface.symbol));
                    if (iface.wasRemoved) sw.Write("</span>");
                    i++;
                }
                sw.WriteLine("</span>");
            }
            sw.WriteLine("</div>"); //End header box

            sw.Write(memberBuilder.ToString());

            if (symbols.Any())
            {
                sw.WriteLine($"<div class='members'><h4>Nested Types</h4></div>");
                foreach (var t in symbols)
                {
                    WriteType(t.newSymbol, t.oldSymbol);
                }
            }

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
            WriteType(enm, oldType);
        }

        public void WriteInterface(INamedTypeSymbol iface) => WriteInterface(iface, null);
        public void WriteInterface(INamedTypeSymbol iface, INamedTypeSymbol oldType = null)
        {
            WriteType(iface, oldType);
        }
        public void WriteDelegate(INamedTypeSymbol del) => WriteDelegate(del, null);

        public void WriteDelegate(INamedTypeSymbol del, INamedTypeSymbol oldDel = null)
        {
            WriteType(del, oldDel);
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
            var name = member.Name;
            if (name == ".ctor")
            {
                name = member.ContainingType.Name;
            }
            name = Briefify(member, name);

            if (member is IPropertySymbol p)
            {
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
            var brief = symbol.GetDescription();
            if (!string.IsNullOrEmpty(brief))
                return $"<span title=\"{System.Web.HttpUtility.HtmlEncode(brief)}\">{System.Web.HttpUtility.HtmlEncode(content)}</span>";
            return System.Web.HttpUtility.HtmlEncode(content);
        }
    }
}
