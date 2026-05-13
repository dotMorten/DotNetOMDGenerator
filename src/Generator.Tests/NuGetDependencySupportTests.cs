using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace Generator.Tests;

[TestClass]
public sealed class NuGetDependencySupportTests
{
    [TestMethod]
    public void DependencyExpression_SupportsIncludeAndExcludePatterns()
    {
        var expression = NuGetDependencyExpression.Parse("Microsoft.WindowsAppSDK.*;!Microsoft.WindowsAppSDK.Tests.*");

        Assert.IsTrue(expression.IsMatch("Microsoft.WindowsAppSDK.Foundation"));
        Assert.IsTrue(expression.IsMatch("Microsoft.WindowsAppSDK.Graphics"));
        Assert.IsFalse(expression.IsMatch("Microsoft.WindowsAppSDK.Tests.Core"));
        Assert.IsFalse(expression.IsMatch("CommunityToolkit.WinUI"));
    }

    [TestMethod]
    public async Task DependencyCollector_TraversesHierarchyAndIncludesMatchingPackages()
    {
        var expression = NuGetDependencyExpression.Parse("Microsoft.WindowsAppSDK.*;!Microsoft.WindowsAppSDK.Graphics");
        var root = new PackageIdentity("MetaPackage", new NuGetVersion("1.0.0"));
        var meta = new PackageIdentity("Intermediary.Meta", new NuGetVersion("2.0.0"));
        var nested = new PackageIdentity("Nested.Package", new NuGetVersion("1.0.0"));
        var core = new PackageIdentity("Microsoft.WindowsAppSDK.Core", new NuGetVersion("1.1.0"));
        var interop = new PackageIdentity("Microsoft.WindowsAppSDK.Interop", new NuGetVersion("1.2.0"));
        var graphics = new PackageIdentity("Microsoft.WindowsAppSDK.Graphics", new NuGetVersion("1.3.0"));
        var unrelated = new PackageIdentity("Contoso.Utility", new NuGetVersion("9.9.9"));

        var graph = new Dictionary<string, NuGetPackageNode>(StringComparer.OrdinalIgnoreCase)
        {
            [NuGetDependencyCollector.GetIdentityKey(root)] = new NuGetPackageNode(root, new[]
            {
                CreateDependency(meta),
                CreateDependency(unrelated)
            }),
            [NuGetDependencyCollector.GetIdentityKey(meta)] = new NuGetPackageNode(meta, new[]
            {
                CreateDependency(core),
                CreateDependency(nested)
            }),
            [NuGetDependencyCollector.GetIdentityKey(nested)] = new NuGetPackageNode(nested, new[]
            {
                CreateDependency(interop),
                CreateDependency(graphics)
            }),
            [NuGetDependencyCollector.GetIdentityKey(core)] = new NuGetPackageNode(core, Array.Empty<PackageDependency>()),
            [NuGetDependencyCollector.GetIdentityKey(interop)] = new NuGetPackageNode(interop, Array.Empty<PackageDependency>()),
            [NuGetDependencyCollector.GetIdentityKey(graphics)] = new NuGetPackageNode(graphics, Array.Empty<PackageDependency>()),
            [NuGetDependencyCollector.GetIdentityKey(unrelated)] = new NuGetPackageNode(unrelated, Array.Empty<PackageDependency>())
        };

        var selected = await NuGetDependencyCollector.CollectIncludedDependenciesAsync(
            new[] { root },
            expression,
            package => Task.FromResult(graph[NuGetDependencyCollector.GetIdentityKey(package)]),
            dependency => Task.FromResult(graph.Values.Select(n => n.Identity).Single(i => i.Id == dependency.Id)));

        CollectionAssert.AreEquivalent(
            new[] { core.Id, interop.Id },
            selected.Select(s => s.Id).ToArray());
    }

    [TestMethod]
    public async Task ResolutionContext_DoesNotShareSelectionsAcrossRuns()
    {
        var dependency = new PackageDependency("Microsoft.WindowsAppSDK.Foundation", VersionRange.Parse("[1.0.0, 3.0.0)"));
        var oldVersion = new NuGetVersion("1.8.0");
        var newVersion = new NuGetVersion("2.0.0");

        var firstRun = new NuGetDependencyResolutionContext(
            new[] { new PackageIdentity("Root", newVersion) },
            _ => Task.FromResult<IReadOnlyList<NuGetVersion>>(new[] { newVersion }));
        var secondRun = new NuGetDependencyResolutionContext(
            new[] { new PackageIdentity("Root", oldVersion) },
            _ => Task.FromResult<IReadOnlyList<NuGetVersion>>(new[] { oldVersion }));

        var firstResolved = await firstRun.ResolveDependencyIdentityAsync(dependency);
        var secondResolved = await secondRun.ResolveDependencyIdentityAsync(dependency);

        Assert.AreEqual(newVersion, firstResolved.Version);
        Assert.AreEqual(oldVersion, secondResolved.Version);
    }

    [TestMethod]
    public async Task ResolutionContext_ReusesCompatiblePackageSelection()
    {
        var selected = new PackageIdentity("Microsoft.WindowsAppSDK.Foundation", new NuGetVersion("2.0.0"));
        var context = new NuGetDependencyResolutionContext(
            new[] { selected },
            _ => Task.FromResult<IReadOnlyList<NuGetVersion>>(new[] { new NuGetVersion("1.0.0"), selected.Version }));

        var resolved = await context.ResolveDependencyIdentityAsync(
            new PackageDependency(selected.Id, VersionRange.Parse("[1.0.0, 3.0.0)")));

        Assert.AreEqual(selected.Version, resolved.Version);
    }

    private static PackageDependency CreateDependency(PackageIdentity identity)
    {
        return new PackageDependency(identity.Id, new VersionRange(identity.Version, true, identity.Version, true));
    }
}
