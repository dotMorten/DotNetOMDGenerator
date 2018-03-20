using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Generator.Generators
{
    internal class OMDGenerator : ICodeGenerator
    {
        private static SolidBrush BackgroundClassBrush = new SolidBrush(Color.FromArgb(255, 211, 220, 239));
        private static SolidBrush BackgroundInterfaceBrush = new SolidBrush(Color.FromArgb(255, 231, 240, 220));
        private static SolidBrush BackgroundEnumBrush = new SolidBrush(Color.FromArgb(255, 221, 214, 239));
        private static SolidBrush BackgroundGroupBrush = new SolidBrush(Color.FromArgb(255, 240, 242, 239));
        private static Font MemberFont = new System.Drawing.Font("Arial", 16);
        private static Font HeaderFont = new System.Drawing.Font("Arial", 20, FontStyle.Bold);
        private static Pen BlackPen = new Pen(Color.Black, 1);
        private const int groupHeight = 47;
        private const int headerHeight = 100;
        private const int lineSpacing = 12;
        private const int MemberIndent = 75;

        public void WriteClass(INamedTypeSymbol type) =>RenderOMD(type);

        public void WriteEnum(INamedTypeSymbol enm) => RenderOMD(enm);

        public void WriteInterface(INamedTypeSymbol iface) => RenderOMD(iface);

        private void RenderOMD(INamedTypeSymbol type)
        { 
            using (Bitmap bitmap = new System.Drawing.Bitmap(2000, 2000))
            {
                int y = 0;
                int startHeight = 0;
                double width = 200; //MinWidth
                using (var bmp = System.Drawing.Graphics.FromImage(bitmap))
                {
                    bmp.Clear(Color.Transparent);
                    if (type.Interfaces.Any())
                    {
                        bmp.DrawEllipse(BlackPen, 40, 0, 24, 24);
                        y += 5;
                        foreach (var iface in type.Interfaces)
                        {
                            width = Math.Max(width, bmp.MeasureString(iface.Name, HeaderFont).Width + 75);
                            bmp.DrawString(iface.Name, MemberFont, Brushes.Black, 75, y);
                            y += (int)(MemberFont.Size + lineSpacing);
                        }
                        y += lineSpacing;
                        bmp.DrawLine(BlackPen, 52, 24, 52, y);
                    }
                    startHeight = y;
                    var bbrush = BackgroundClassBrush;
                    if (type.TypeKind == TypeKind.Enum)
                        bbrush = BackgroundEnumBrush;
                    else if (type.TypeKind == TypeKind.Interface)
                        bbrush = BackgroundInterfaceBrush;
                    bmp.FillRectangle(bbrush, 0, y, bitmap.Width, headerHeight);
                    width = Math.Max(width, bmp.MeasureString(type.Name, HeaderFont).Width + 10);
                    bmp.DrawString(type.Name, HeaderFont, Brushes.Black, 10, 18 + y);
                    string typeDesc = "";
                    if (type.TypeKind == TypeKind.Class)
                        typeDesc = type.IsAbstract ? "Abstract " : "" + "Class";
                    else if (type.TypeKind == TypeKind.Interface)
                        typeDesc = "Interface";
                    else if (type.TypeKind == TypeKind.Enum)
                        typeDesc = "Enum";
                    bmp.DrawString(typeDesc, MemberFont, Brushes.Black, 10, 18 + y + HeaderFont.Size + lineSpacing);
                    if (type.BaseType != null && type.BaseType.Name != "Object" && type.TypeKind != TypeKind.Enum)
                        bmp.DrawString("→ " + type.BaseType.Name, MemberFont, Brushes.Black, 10, 18 + y + HeaderFont.Size + lineSpacing * 2 + MemberFont.Size);
                    y += headerHeight;
                    bmp.DrawLine(BlackPen, 0, y, bitmap.Width, y);
                    Action<string, IEnumerable<ISymbol>> RenderMembers = (n, m) =>
                       {
                           if (m.Any())
                           {
                               bmp.FillRectangle(BackgroundGroupBrush, 0, y, bitmap.Width, groupHeight);
                               var size = bmp.MeasureString(n, new Font("Arial", 18));
                               width = Math.Max(width, size.Width + 25);
                               bmp.DrawString(n, new Font("Arial", 18), Brushes.Black, 25, y + (groupHeight - size.Height)/2);
                               y += groupHeight + lineSpacing;

                               foreach (var method in m)
                               {
                                   var str = FormatMember(method);
                                   width = Math.Max(width, bmp.MeasureString(str, MemberFont).Width + MemberIndent);
                                   bmp.DrawString(str, MemberFont, Brushes.Black, MemberIndent, y);
                                   y += (int)(MemberFont.Size + lineSpacing);
                               }
                               y += lineSpacing;
                           }
                       };
                    RenderMembers("Constructors", type.GetConstructors().OfType<ISymbol>());
                    RenderMembers("Properties", type.GetProperties().OfType<ISymbol>());
                    RenderMembers("Methods", type.GetMethods().OfType<ISymbol>());
                    RenderMembers("Events", type.GetEvents().OfType<ISymbol>());
                    if(type.TypeKind == TypeKind.Enum)
                    {
                        y += lineSpacing;
                        foreach(var e in type.GetEnums())
                        {
                            string str = e.Name;
                            width = Math.Max(width, bmp.MeasureString(str, MemberFont).Width + 25);
                            bmp.DrawString(str, MemberFont, Brushes.Black, 25, y);
                            y += (int)(MemberFont.Size + lineSpacing);
                        }
                    }
                    bmp.Flush();
                }
                int padding = 10;
                using (Bitmap bitmap2 = new System.Drawing.Bitmap((int)width + 10 + 2* padding, (int)y + 10 + 2* padding))
                {
                    using (var bmp2 = System.Drawing.Graphics.FromImage(bitmap2))
                    {
                        bmp2.Clear(Color.White);
                        bmp2.DrawImageUnscaledAndClipped(bitmap, new Rectangle(padding, padding, bitmap2.Width - 2 * padding, bitmap2.Height - 2 * padding));
                        bmp2.DrawRectangle(BlackPen, padding, startHeight+padding, (float)bitmap2.Width - 21, bitmap2.Height - startHeight - 21);
                    }
                    bitmap2.Save($"{type.ContainingNamespace.GetFullNamespace()}.{type.Name}.png");
                }
            }
        }

        private string FormatMember(ISymbol member)
        {
            string name = member.Name;
            if (member.DeclaredAccessibility != Accessibility.Public)
            {
                name = member.DeclaredAccessibility.ToString().ToLower() + " " + name;
            }
            if (member is IPropertySymbol)
            {
                var p = (IPropertySymbol)member;
                name += " { ";
                if(p.IsReadable())
                {
                    if (p.GetMethod.DeclaredAccessibility < member.DeclaredAccessibility)
                    {
                        name += p.SetMethod.DeclaredAccessibility.ToString() + " ";
                    }
                    name += "get; ";
                }
                if (p.IsSettable())
                {
                    if (p.SetMethod.DeclaredAccessibility < member.DeclaredAccessibility)
                    {
                        name += p.SetMethod.DeclaredAccessibility.ToString() + " ";
                    }
                    name += "set; ";
                }
                name += "} : " + p.Type.Name;
            }
            else if(member is IMethodSymbol)
            {
                var m = (member as IMethodSymbol);
                name += "(";
                name += string.Join(", ", m.Parameters.Select(p => p.Type.Name + " " + p.Name));
                name += ")";
                if (!m.ReturnsVoid) {
                    name += " : " + m.ReturnType.Name;
                }
            }
            else if (member is IEventSymbol)
            {
                var m = (member as IEventSymbol);
                name += " : " + m.Type.Name;
            }
            return name;
        }

        public void Initialize(List<INamedTypeSymbol> allSymbols)
        {
        }

        public void Complete()
        {
        }
    }
}
