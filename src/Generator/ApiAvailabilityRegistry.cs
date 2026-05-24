using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Generator
{
    internal static class ApiAvailabilityRegistry
    {
        private static readonly Dictionary<ISymbol, AvailabilityInfo> availabilityBySymbol = new Dictionary<ISymbol, AvailabilityInfo>(ReferenceEqualityComparer.Instance);
        private static readonly Dictionary<INamedTypeSymbol, TypeInfo> typeInfoBySymbol = new Dictionary<INamedTypeSymbol, TypeInfo>(ReferenceEqualityComparer.Instance);

        internal static void Reset()
        {
            availabilityBySymbol.Clear();
            typeInfoBySymbol.Clear();
        }

        internal static IReadOnlyList<INamedTypeSymbol> MergeTypes(IEnumerable<Generator.CompilationInput> compilationInputs, Func<Generator.CompilationInput, IEnumerable<INamedTypeSymbol>> getTypes)
        {
            var inputs = compilationInputs.ToArray();
            var universe = inputs.Select(i => i.TargetFramework).Where(t => !string.IsNullOrWhiteSpace(t)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(t => t).ToArray();

            var collectedTypes = new Dictionary<string, List<(INamedTypeSymbol symbol, string targetFramework)>>(StringComparer.Ordinal);
            foreach (var input in inputs)
            {
                foreach (var type in getTypes(input))
                {
                    var key = type.GetFullTypeName();
                    if (!collectedTypes.TryGetValue(key, out var list))
                    {
                        list = new List<(INamedTypeSymbol symbol, string targetFramework)>();
                        collectedTypes[key] = list;
                    }
                    list.Add((type, input.TargetFramework));
                }
            }

            return collectedTypes.Values
                .Select(group => RegisterMergedType(group, universe))
                .OrderBy(t => t.Name)
                .OrderBy(t => t.GetFullNamespace())
                .ToList();
        }

        internal static IEnumerable<INamedTypeSymbol> GetNestedTypes(INamedTypeSymbol type) =>
            typeInfoBySymbol.TryGetValue(type, out var info) ? info.NestedTypes : type.GetAllNestedTypesRaw();

        internal static IEnumerable<INamedTypeSymbol> GetInterfaces(INamedTypeSymbol type) =>
            typeInfoBySymbol.TryGetValue(type, out var info) ? info.Interfaces : type.GetInterfacesRaw();

        internal static IEnumerable<IMethodSymbol> GetConstructors(INamedTypeSymbol type) =>
            typeInfoBySymbol.TryGetValue(type, out var info) ? info.Constructors : type.GetConstructorsRaw();

        internal static IEnumerable<IPropertySymbol> GetProperties(INamedTypeSymbol type) =>
            typeInfoBySymbol.TryGetValue(type, out var info) ? info.Properties : type.GetPropertiesRaw();

        internal static IEnumerable<IMethodSymbol> GetMethods(INamedTypeSymbol type) =>
            typeInfoBySymbol.TryGetValue(type, out var info) ? info.Methods : type.GetMethodsRaw();

        internal static IEnumerable<IEventSymbol> GetEvents(INamedTypeSymbol type) =>
            typeInfoBySymbol.TryGetValue(type, out var info) ? info.Events : type.GetEventsRaw();

        internal static IEnumerable<IFieldSymbol> GetFields(INamedTypeSymbol type) =>
            typeInfoBySymbol.TryGetValue(type, out var info) ? info.Fields : type.GetFieldsRaw();

        internal static IEnumerable<IFieldSymbol> GetEnums(INamedTypeSymbol type) =>
            typeInfoBySymbol.TryGetValue(type, out var info) ? info.Enums : type.GetEnumsRaw();

        internal static bool AvailabilityEquals(ISymbol left, ISymbol right)
        {
            var leftInfo = availabilityBySymbol.TryGetValue(left, out var leftAvailability) ? leftAvailability : null;
            var rightInfo = availabilityBySymbol.TryGetValue(right, out var rightAvailability) ? rightAvailability : null;
            if (leftInfo == null || rightInfo == null)
                return true;

            return leftInfo.ApplicableTargetFrameworks.SetEquals(rightInfo.ApplicableTargetFrameworks);
        }

        internal static string GetAvailabilityLabel(ISymbol symbol, bool suppressIfSameAsContainingType = false)
        {
            if (!availabilityBySymbol.TryGetValue(symbol, out var info))
                return string.Empty;

            if (info.Universe.Length <= 1 || info.ApplicableTargetFrameworks.Count == info.Universe.Length)
                return string.Empty;

            if (suppressIfSameAsContainingType && symbol.ContainingType != null &&
                availabilityBySymbol.TryGetValue(symbol.ContainingType, out var containingTypeInfo) &&
                info.ApplicableTargetFrameworks.SetEquals(containingTypeInfo.ApplicableTargetFrameworks))
            {
                return string.Empty;
            }

            return $" [TFMs: {string.Join(", ", info.ApplicableTargetFrameworks.OrderBy(t => t))}]";
        }

        private static INamedTypeSymbol RegisterMergedType(IEnumerable<(INamedTypeSymbol symbol, string targetFramework)> variants, string[] universe)
        {
            var variantList = variants.ToList();
            var representative = variantList[0].symbol;
            var applicableTargetFrameworks = ResolveApplicableTargetFrameworks(variantList.Select(v => v.targetFramework), universe);

            var typeInfo = new TypeInfo(
                MergeTypeSymbols(variantList, type => type.GetAllNestedTypesRaw(), t => t.GetFullTypeName(), RegisterMergedType, universe),
                MergeSymbols(variantList, type => type.GetInterfacesRaw(), s => s.ToDisplayString(), universe, s => s.ToDisplayString(Generator.Constants.AllFormatWithoutContaining)),
                MergeSymbols(variantList, type => type.GetConstructorsRaw(), s => s.ToDisplayString(Generator.Constants.AllFormat), universe, s => s.ToDisplayString(Generator.Constants.AllFormatWithoutContaining)),
                MergeSymbols(variantList, type => type.GetPropertiesRaw(), GetPropertyKey, universe, GetPropertyDisplayKey),
                MergeSymbols(variantList, type => type.GetMethodsRaw(), s => s.ToDisplayString(Generator.Constants.AllFormat), universe, s => s.ToDisplayString(Generator.Constants.AllFormatWithoutContaining)),
                MergeSymbols(variantList, type => type.GetEventsRaw(), s => s.ToDisplayString(Generator.Constants.AllFormat), universe, s => s.ToDisplayString(Generator.Constants.AllFormatWithoutContaining)),
                MergeSymbols(variantList, type => type.GetFieldsRaw(), GetFieldKey, universe, GetFieldDisplayKey),
                MergeSymbols(variantList, type => type.GetEnumsRaw(), GetFieldKey, universe, GetFieldDisplayKey));

            typeInfoBySymbol[representative] = typeInfo;
            RegisterAvailability(representative, applicableTargetFrameworks, universe);
            return representative;
        }

        private static List<INamedTypeSymbol> MergeTypeSymbols(IEnumerable<(INamedTypeSymbol symbol, string targetFramework)> variants, Func<INamedTypeSymbol, IEnumerable<INamedTypeSymbol>> selector, Func<INamedTypeSymbol, string> keySelector, Func<IEnumerable<(INamedTypeSymbol symbol, string targetFramework)>, string[], INamedTypeSymbol> registrar, string[] universe)
        {
            var groups = variants
                .SelectMany(v => selector(v.symbol).Select(symbol => (symbol, v.targetFramework)))
                .GroupBy(v => keySelector(v.symbol), StringComparer.Ordinal);

            return groups.Select(group => registrar(group, universe)).ToList();
        }

        private static List<TSymbol> MergeSymbols<TSymbol>(IEnumerable<(INamedTypeSymbol symbol, string targetFramework)> variants, Func<INamedTypeSymbol, IEnumerable<TSymbol>> selector, Func<TSymbol, string> keySelector, string[] universe, Func<TSymbol, string> displayKeySelector = null)
            where TSymbol : class, ISymbol
        {
            var groups = variants
                .SelectMany(v => selector(v.symbol).Select(symbol => (symbol, v.targetFramework)))
                .GroupBy(v => keySelector(v.symbol), StringComparer.Ordinal);

            var merged = new List<TSymbol>();
            foreach (var group in groups)
            {
                var representative = group.First().symbol;
                merged.Add(representative);
                RegisterAvailability(representative, ResolveApplicableTargetFrameworks(group.Select(g => g.targetFramework), universe), universe);
            }
            return displayKeySelector is null ? merged : CollapseDisplayDuplicates(merged, displayKeySelector).ToList();
        }

        internal static IEnumerable<TSymbol> CollapseDisplayDuplicates<TSymbol>(IEnumerable<TSymbol> symbols, Func<TSymbol, string> displayKeySelector)
            where TSymbol : class, ISymbol
        {
            var result = new List<TSymbol>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var symbol in symbols)
            {
                var availabilityKey = availabilityBySymbol.TryGetValue(symbol, out var info)
                    ? string.Join("|", info.ApplicableTargetFrameworks.OrderBy(t => t, StringComparer.OrdinalIgnoreCase))
                    : string.Empty;
                var key = displayKeySelector(symbol) + "::" + availabilityKey;
                if (seen.Add(key))
                    result.Add(symbol);
            }
            return result;
        }

        private static void RegisterType(INamedTypeSymbol symbol, INamedTypeSymbol representative, HashSet<string> applicableTargetFrameworks, string[] universe)
        {
            typeInfoBySymbol[symbol] = new TypeInfo(
                symbol.GetAllNestedTypesRaw().ToList(),
                symbol.GetInterfacesRaw().ToList(),
                symbol.GetConstructorsRaw().ToList(),
                symbol.GetPropertiesRaw().ToList(),
                symbol.GetMethodsRaw().ToList(),
                symbol.GetEventsRaw().ToList(),
                symbol.GetFieldsRaw().ToList(),
                symbol.GetEnumsRaw().ToList());
            RegisterAvailability(representative, applicableTargetFrameworks, universe);
        }

        private static void RegisterAvailability(ISymbol symbol, HashSet<string> applicableTargetFrameworks, string[] universe)
        {
            availabilityBySymbol[symbol] = new AvailabilityInfo(applicableTargetFrameworks, universe);
        }

        private static HashSet<string> ResolveApplicableTargetFrameworks(IEnumerable<string> targetFrameworks, string[] universe)
        {
            var resolved = targetFrameworks.Where(t => !string.IsNullOrWhiteSpace(t)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (resolved.Length == 0 && universe.Length > 0)
                return new HashSet<string>(universe, StringComparer.OrdinalIgnoreCase);

            return new HashSet<string>(resolved, StringComparer.OrdinalIgnoreCase);
        }

        private static string GetFieldKey(IFieldSymbol field) => field.ToDisplayString(Generator.Constants.AllFormat) + "=" + field.ConstantValue?.ToString();

        private static string GetFieldDisplayKey(IFieldSymbol field) => field.ToDisplayString(Generator.Constants.AllFormatWithoutContaining) + "=" + field.ConstantValue?.ToString();

        internal static string GetFieldDisplayKeyForComparison(IFieldSymbol field) => GetFieldDisplayKey(field);

        private static string GetPropertyKey(IPropertySymbol property)
        {
            var getterAccessibility = property.GetMethod?.DeclaredAccessibility.ToString() ?? string.Empty;
            var setterAccessibility = property.SetMethod?.DeclaredAccessibility.ToString() ?? string.Empty;
            return property.ToDisplayString(Generator.Constants.AllFormat) + "|" + getterAccessibility + "|" + setterAccessibility;
        }

        private static string GetPropertyDisplayKey(IPropertySymbol property)
        {
            var getterAccessibility = property.GetMethod?.DeclaredAccessibility.ToString() ?? string.Empty;
            var setterAccessibility = property.SetMethod?.DeclaredAccessibility.ToString() ?? string.Empty;
            return property.ToDisplayString(Generator.Constants.AllFormatWithoutContaining) + "|" + getterAccessibility + "|" + setterAccessibility;
        }

        internal static string GetPropertyDisplayKeyForComparison(IPropertySymbol property) => GetPropertyDisplayKey(property);

        private sealed class AvailabilityInfo
        {
            internal AvailabilityInfo(HashSet<string> applicableTargetFrameworks, string[] universe)
            {
                ApplicableTargetFrameworks = applicableTargetFrameworks;
                Universe = universe;
            }

            internal HashSet<string> ApplicableTargetFrameworks { get; }

            internal string[] Universe { get; }
        }

        private sealed class TypeInfo
        {
            internal TypeInfo(
                List<INamedTypeSymbol> nestedTypes,
                List<INamedTypeSymbol> interfaces,
                List<IMethodSymbol> constructors,
                List<IPropertySymbol> properties,
                List<IMethodSymbol> methods,
                List<IEventSymbol> events,
                List<IFieldSymbol> fields,
                List<IFieldSymbol> enums)
            {
                NestedTypes = nestedTypes;
                Interfaces = interfaces;
                Constructors = constructors;
                Properties = properties;
                Methods = methods;
                Events = events;
                Fields = fields;
                Enums = enums;
            }

            internal List<INamedTypeSymbol> NestedTypes { get; }

            internal List<INamedTypeSymbol> Interfaces { get; }

            internal List<IMethodSymbol> Constructors { get; }

            internal List<IPropertySymbol> Properties { get; }

            internal List<IMethodSymbol> Methods { get; }

            internal List<IEventSymbol> Events { get; }

            internal List<IFieldSymbol> Fields { get; }

            internal List<IFieldSymbol> Enums { get; }
        }
    }
}
