using NuGet.Packaging.Core;
using NuGet.Versioning;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Generator
{
    internal sealed class NuGetDependencyExpression
    {
        private readonly Regex[] includes;
        private readonly Regex[] excludes;

        private NuGetDependencyExpression(IEnumerable<string> includes, IEnumerable<string> excludes)
        {
            this.includes = includes.Select(CreateWildcardRegex).ToArray();
            this.excludes = excludes.Select(CreateWildcardRegex).ToArray();
        }

        internal bool HasRules => includes.Length > 0 || excludes.Length > 0;

        internal static NuGetDependencyExpression Parse(string expression)
        {
            if (string.IsNullOrWhiteSpace(expression))
                return null;

            var includes = new List<string>();
            var excludes = new List<string>();
            foreach (var token in expression.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var value = token.Trim();
                if (string.IsNullOrWhiteSpace(value))
                    continue;
                if (value.StartsWith("!"))
                    excludes.Add(value.Substring(1));
                else
                    includes.Add(value);
            }
            return new NuGetDependencyExpression(includes, excludes);
        }

        internal bool IsMatch(string packageId)
        {
            if (!HasRules)
                return false;

            bool included = includes.Length == 0 || includes.Any(i => i.IsMatch(packageId));
            if (!included)
                return false;
            return !excludes.Any(e => e.IsMatch(packageId));
        }

        private static Regex CreateWildcardRegex(string pattern)
        {
            return new Regex("^" + Regex.Escape(pattern)
                .Replace("\\*", ".*")
                .Replace("\\?", ".") + "$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        }
    }

    internal sealed class NuGetPackageNode
    {
        internal NuGetPackageNode(PackageIdentity identity, IEnumerable<PackageDependency> dependencies)
        {
            Identity = identity;
            Dependencies = dependencies?.ToArray() ?? Array.Empty<PackageDependency>();
        }

        internal PackageIdentity Identity { get; }

        internal IReadOnlyList<PackageDependency> Dependencies { get; }
    }

    internal static class NuGetDependencyCollector
    {
        internal static async Task<IReadOnlyCollection<PackageIdentity>> CollectIncludedDependenciesAsync(
            IEnumerable<PackageIdentity> rootPackages,
            NuGetDependencyExpression dependencyExpression,
            Func<PackageIdentity, Task<NuGetPackageNode>> packageLoader,
            Func<PackageDependency, Task<PackageIdentity>> dependencyResolver)
        {
            if (dependencyExpression == null || !dependencyExpression.HasRules)
                return Array.Empty<PackageIdentity>();

            var selected = new ConcurrentDictionary<string, PackageIdentity>(StringComparer.OrdinalIgnoreCase);
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pending = new Queue<PackageIdentity>(rootPackages);

            while (pending.Count > 0)
            {
                var current = pending.Dequeue();
                var currentKey = GetIdentityKey(current);
                if (!visited.Add(currentKey))
                    continue;

                var package = await packageLoader(current).ConfigureAwait(false);
                if (package == null)
                    continue;

                foreach (var dependency in package.Dependencies)
                {
                    var dependencyIdentity = await dependencyResolver(dependency).ConfigureAwait(false);
                    if (dependencyIdentity == null)
                        continue;

                    pending.Enqueue(dependencyIdentity);

                    if (dependencyExpression.IsMatch(dependencyIdentity.Id))
                        selected.TryAdd(GetIdentityKey(dependencyIdentity), dependencyIdentity);
                }
            }

            return selected.Values.ToArray();
        }

        internal static string GetIdentityKey(PackageIdentity identity) => identity.Id + "/" + identity.Version.ToNormalizedString();
    }

    internal sealed class NuGetDependencyResolutionContext
    {
        private readonly Func<string, Task<IReadOnlyList<NuGetVersion>>> availableVersionsLoader;
        private readonly Dictionary<string, PackageIdentity> resolvedDependencies = new Dictionary<string, PackageIdentity>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, IReadOnlyList<NuGetVersion>> knownPackageVersions = new Dictionary<string, IReadOnlyList<NuGetVersion>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, PackageIdentity> selectedPackagesById = new Dictionary<string, PackageIdentity>(StringComparer.OrdinalIgnoreCase);

        internal NuGetDependencyResolutionContext(
            IEnumerable<PackageIdentity> rootPackages,
            Func<string, Task<IReadOnlyList<NuGetVersion>>> availableVersionsLoader)
        {
            this.availableVersionsLoader = availableVersionsLoader;
            foreach (var package in rootPackages ?? Array.Empty<PackageIdentity>())
            {
                selectedPackagesById[package.Id] = package;
            }
        }

        internal async Task<PackageIdentity> ResolveDependencyIdentityAsync(PackageDependency dependency)
        {
            if (selectedPackagesById.TryGetValue(dependency.Id, out var selected) &&
                (dependency.VersionRange == null || dependency.VersionRange.Satisfies(selected.Version)))
            {
                return selected;
            }

            var cacheKey = dependency.Id + "|" + dependency.VersionRange?.OriginalString;
            if (resolvedDependencies.TryGetValue(cacheKey, out var cachedIdentity))
                return cachedIdentity;

            var versions = await GetAvailableVersionsAsync(dependency.Id).ConfigureAwait(false);
            var version = versions
                .OrderBy(v => v)
                .FirstOrDefault(v => dependency.VersionRange == null || dependency.VersionRange.Satisfies(v));

            if (version is null)
                return null;

            var resolved = new PackageIdentity(dependency.Id, version);
            resolvedDependencies[cacheKey] = resolved;
            if (!selectedPackagesById.ContainsKey(resolved.Id))
                selectedPackagesById[resolved.Id] = resolved;
            return resolved;
        }

        private async Task<IReadOnlyList<NuGetVersion>> GetAvailableVersionsAsync(string packageId)
        {
            if (knownPackageVersions.TryGetValue(packageId, out var cachedVersions))
                return cachedVersions;

            var versions = await availableVersionsLoader(packageId).ConfigureAwait(false);
            knownPackageVersions[packageId] = versions;
            return versions;
        }
    }
}
