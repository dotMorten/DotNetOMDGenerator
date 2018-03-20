using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Generator
{
    interface ICodeGenerator
    {
        void Initialize(List<INamedTypeSymbol> allSymbols);
        void WriteClass(INamedTypeSymbol type);

        void WriteInterface(INamedTypeSymbol iface);

        void WriteEnum(INamedTypeSymbol enm);
        void Complete();
    }
}
