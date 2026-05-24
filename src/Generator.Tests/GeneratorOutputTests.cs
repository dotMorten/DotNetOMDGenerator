using System.Diagnostics;
using System.Text.RegularExpressions;

using GeneratorApp = global::Generator.Generator;
using GeneratorSettings = global::Generator.GeneratorSettings;
using ProgramApp = global::Generator.Program;
using HtmlOmdGenerator = global::Generator.Generators.HtmlOmdGenerator;
using MarkdownGenerator = global::Generator.Generators.MarkdownGenerator;
using ICodeGenerator = global::Generator.ICodeGenerator;

namespace Generator.Tests;

[TestClass]
public sealed class GeneratorOutputTests
{
    private const string SupportedTypesSource = """
        namespace SampleNamespace;

        public class SampleClass { }
        public struct SampleStruct { public int Value; }
        public interface ISampleInterface { void Run(); }
        public enum SampleEnum { One = 1, Two = 2 }
        public delegate void SampleDelegate(int value);
        public record SampleRecord(string Name);
        public record struct SampleRecordStruct(int X);
        """;

    private const string OldDiffSource = """
        namespace SampleNamespace;

        public class ChangedClass { public void OldMethod() { } }
        public struct ChangedStruct { public int OldField; }
        public interface IChangedInterface { void OldMethod(); }
        public enum ChangedEnum { OldValue = 1 }
        public delegate void ChangedDelegate(int value);
        public class ChangedToRecord { public string Name { get; } public ChangedToRecord(string name) => Name = name; }
        public struct ChangedToRecordStruct { public int X { get; set; } }
        """;

    private const string NewDiffSource = """
        namespace SampleNamespace;

        public class ChangedClass { public void NewMethod() { } }
        public struct ChangedStruct { public int NewField; }
        public interface IChangedInterface { void NewMethod(); }
        public enum ChangedEnum { NewValue = 2 }
        public delegate void ChangedDelegate(string value);
        public record ChangedToRecord(string Name);
        public record struct ChangedToRecordStruct(int X);
        """;

    private const string OldObsoleteDiffSource = """
        namespace SampleNamespace;

        public class ObsoleteSample
        {
            public void ActiveMethod() { }
            public void RemovedMethod() { }
            public string RemovedProperty { get; set; }
            public event System.EventHandler RemovedEvent;
        }

        public class ObsoleteType { }
        """;

    private const string NewObsoleteDiffSource = """
        namespace SampleNamespace;

        [System.Obsolete]
        public class ObsoleteSample
        {
            [System.Obsolete]
            public void ActiveMethod() { }

            [System.Obsolete]
            public string AddedObsoleteProperty { get; set; }
        }

        [System.Obsolete]
        public class ObsoleteType { }
        """;

    private const string OldBreakingDiffSource = """
        namespace SampleNamespace;

        public class RemovedType { public void OldMethod() { } }
        public class ChangedType { public void OldMethod() { } }
        """;

    private const string NewBreakingDiffSource = """
        namespace SampleNamespace;

        public class ChangedType { public void NewMethod() { } }
        """;

    private const string ProjectIncludedSource = """
        namespace SampleNamespace;

        public class IncludedType { }
        """;

    private const string ProjectExcludedSource = """
        namespace SampleNamespace;

        public class ExcludedType { }
        """;

    private const string Net8Source = """
        namespace SampleNamespace;

        public class Net8OnlyType { }
        """;

    private const string Net9Source = """
        namespace SampleNamespace;

        public class Net9OnlyType { }
        """;

    [TestMethod]
    public async Task MarkdownOutput_IdentifiesAllSupportedDeclarationKinds()
    {
        var markdown = await GenerateMarkdownAsync(SupportedTypesSource);

        StringAssert.Contains(markdown, "public interface ISampleInterface");
        StringAssert.Contains(markdown, "public class SampleClass");
        StringAssert.Contains(markdown, "public struct SampleStruct");
        StringAssert.Contains(markdown, "public enum SampleEnum");
        StringAssert.Contains(markdown, "public delegate SampleDelegate : MulticastDelegate");
        StringAssert.Contains(markdown, "Invoke(int value)");
        StringAssert.Contains(markdown, "public record SampleRecord");
        StringAssert.Contains(markdown, "public record struct SampleRecordStruct");
    }

    [TestMethod]
    public async Task HtmlOutput_IdentifiesAllSupportedDeclarationKinds()
    {
        var html = await GenerateHtmlAsync(SupportedTypesSource);

        AssertHtmlType(html, "SampleNamespace.ISampleInterface", "interface", "interface");
        AssertHtmlType(html, "SampleNamespace.SampleClass", "class", "class");
        AssertHtmlType(html, "SampleNamespace.SampleStruct", "struct", "struct");
        AssertHtmlType(html, "SampleNamespace.SampleEnum", "enum", "enum");
        AssertHtmlType(html, "SampleNamespace.SampleDelegate", "delegate", "delegate");
        AssertHtmlType(html, "SampleNamespace.SampleRecord", "class", "record");
        AssertHtmlType(html, "SampleNamespace.SampleRecordStruct", "struct", "record struct");
        StringAssert.Contains(html, "Invoke(int value)");
    }

    [TestMethod]
    public async Task MarkdownDiffOutput_ReportsSupportedTypeChanges()
    {
        var markdown = await GenerateMarkdownDiffAsync(OldDiffSource, NewDiffSource);

        StringAssert.Contains(markdown, "<b>public NewMethod();</b>");
        StringAssert.Contains(markdown, "<strike>public OldMethod();</strike>");
        StringAssert.Contains(markdown, "<b>public int NewField</b>");
        StringAssert.Contains(markdown, "<strike>public int OldField</strike>");
        StringAssert.Contains(markdown, "<b>NewMethod();</b>");
        StringAssert.Contains(markdown, "<strike>OldMethod();</strike>");
        StringAssert.Contains(markdown, "<b>NewValue</b>");
        StringAssert.Contains(markdown, "<strike>OldValue</strike>");
        StringAssert.Contains(markdown, "<b>public Invoke(string value);</b>");
        StringAssert.Contains(markdown, "<strike>public Invoke(int value);</strike>");
        StringAssert.Contains(markdown, "public <strike>class</strike> <b>record</b> ChangedToRecord");
        StringAssert.Contains(markdown, "public <strike>struct</strike> <b>record struct</b> ChangedToRecordStruct");
    }

    [TestMethod]
    public async Task HtmlDiffOutput_ShowsDeclarationKindAndMemberChanges()
    {
        var html = await GenerateHtmlDiffAsync(OldDiffSource, NewDiffSource);

        StringAssert.Contains(html, "<div class='typeKind'><span class='memberRemoved'>class</span> record</div>");
        StringAssert.Contains(html, "<div class='typeKind'><span class='memberRemoved'>struct</span> record struct</div>");
        StringAssert.Contains(html, "NewField");
        StringAssert.Contains(html, "OldField");
        StringAssert.Contains(html, "Invoke(string value)");
        StringAssert.Contains(html, "Invoke(int value)");
    }

    [TestMethod]
    public async Task HtmlDiffOutput_ShowsObsoleteTypesAndMembers()
    {
        var html = await GenerateHtmlDiffAsync(OldObsoleteDiffSource, NewObsoleteDiffSource);

        StringAssert.Contains(html, "<div class='header class obsolete'>");
        StringAssert.Contains(html, "<div class='header class noMembers obsolete'>");
        StringAssert.Contains(html, "<span class='obsolete'>ActiveMethod()</span>");
        StringAssert.Contains(html, "AddedObsoleteProperty { get; set; } : string");
        StringAssert.Contains(html, "<span class='memberRemoved'>RemovedProperty { get; set; } : string</span>");
        StringAssert.Contains(html, "<span class='memberRemoved'>RemovedMethod()</span>");
        StringAssert.Contains(html, "<span class='memberRemoved'>RemovedEvent : EventHandler</span>");
    }

    [TestMethod]
    public async Task MarkdownDiffOutput_ShowsRemovedTypesAsBreakingChanges()
    {
        var markdown = await GenerateMarkdownDiffAsync(OldBreakingDiffSource, NewBreakingDiffSource);

        StringAssert.Contains(markdown, "<b>public NewMethod();</b>");
        StringAssert.Contains(markdown, "<strike>public OldMethod();</strike>");
        StringAssert.Contains(markdown, "<strike>public class RemovedType { ... }</strike>");
    }

    [TestMethod]
    public async Task HtmlDiffOutput_ShowsRemovedTypesAsBreakingChanges()
    {
        var html = await GenerateHtmlDiffAsync(OldBreakingDiffSource, NewBreakingDiffSource);

        StringAssert.Contains(html, "<div class='objectBox typeRemoved typeExisting' id='SampleNamespace.RemovedType'>");
        StringAssert.Contains(html, "<span class='memberRemoved'>OldMethod()</span>");
        StringAssert.Contains(html, "NewMethod()");
    }

    [TestMethod]
    public async Task MarkdownDiffOutput_ComparesCurrentSourceAgainstGitTag()
    {
        using var workspace = new TestWorkspace();
        var repositoryDirectory = workspace.CreateDirectory("repo");
        var sourceDirectory = Path.Combine(repositoryDirectory, "src");
        Directory.CreateDirectory(sourceDirectory);
        InitializeGitRepository(repositoryDirectory);

        workspace.WriteSource(sourceDirectory, "Types.cs", OldDiffSource);
        CommitAll(repositoryDirectory, "baseline");
        RunGit(repositoryDirectory, "tag", "baseline");

        workspace.WriteSource(sourceDirectory, "Types.cs", NewDiffSource);

        var markdown = await GenerateCliMarkdownAsync(
            workspace,
            "git-tag-diff",
            $"/source={sourceDirectory}",
            "/compareRef=baseline");

        StringAssert.Contains(markdown, "<b>public NewMethod();</b>");
        StringAssert.Contains(markdown, "<strike>public OldMethod();</strike>");
        StringAssert.Contains(markdown, "public <strike>class</strike> <b>record</b> ChangedToRecord");
    }

    [TestMethod]
    public async Task MarkdownDiffOutput_ComparesTwoRefsFromRemoteGitRepository()
    {
        using var workspace = new TestWorkspace();
        var repositoryDirectory = workspace.CreateDirectory("repo");
        var sourceDirectory = Path.Combine(repositoryDirectory, "src");
        Directory.CreateDirectory(sourceDirectory);
        InitializeGitRepository(repositoryDirectory);

        workspace.WriteSource(sourceDirectory, "Types.cs", OldDiffSource);
        var oldCommit = CommitAll(repositoryDirectory, "old");

        workspace.WriteSource(sourceDirectory, "Types.cs", NewDiffSource);
        var newCommit = CommitAll(repositoryDirectory, "new");

        var remoteRepositoryDirectory = workspace.CreateDirectory("remote.git");
        RunGit(workspace.RootPath, "init", "--bare", remoteRepositoryDirectory);

        var remoteRepositoryUri = new Uri(remoteRepositoryDirectory).AbsoluteUri;
        RunGit(repositoryDirectory, "remote", "add", "origin", remoteRepositoryUri);
        RunGit(repositoryDirectory, "push", "origin", "HEAD");

        var markdown = await GenerateCliMarkdownAsync(
            workspace,
            "git-remote-diff",
            "/source=src",
            $"/gitRepo={remoteRepositoryUri}",
            $"/sourceRef={newCommit}",
            $"/compareRef={oldCommit}");

        StringAssert.Contains(markdown, "<b>public NewMethod();</b>");
        StringAssert.Contains(markdown, "<strike>public OldMethod();</strike>");
        StringAssert.Contains(markdown, "<b>public Invoke(string value);</b>");
    }

    [TestMethod]
    public async Task MarkdownOutput_LoadsSingleSourceRefFromRemoteGitRepository()
    {
        using var workspace = new TestWorkspace();
        var repositoryDirectory = workspace.CreateDirectory("repo");
        var sourceDirectory = Path.Combine(repositoryDirectory, "src");
        Directory.CreateDirectory(sourceDirectory);
        InitializeGitRepository(repositoryDirectory);

        workspace.WriteSource(sourceDirectory, "Types.cs", SupportedTypesSource);
        var commit = CommitAll(repositoryDirectory, "snapshot");

        var remoteRepositoryDirectory = workspace.CreateDirectory("remote.git");
        RunGit(workspace.RootPath, "init", "--bare", remoteRepositoryDirectory);

        var remoteRepositoryUri = new Uri(remoteRepositoryDirectory).AbsoluteUri;
        RunGit(repositoryDirectory, "remote", "add", "origin", remoteRepositoryUri);
        RunGit(repositoryDirectory, "push", "origin", "HEAD");

        var markdown = await GenerateCliMarkdownAsync(
            workspace,
            "git-remote-source",
            "/source=src",
            $"/gitRepo={remoteRepositoryUri}",
            $"/sourceRef={commit}");

        StringAssert.Contains(markdown, "public class SampleClass");
        StringAssert.Contains(markdown, "public record SampleRecord");
        Assert.IsFalse(markdown.Contains("<strike>", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task MarkdownOutput_UsesProjectCompileItemsInsteadOfAllFolderFiles()
    {
        using var workspace = new TestWorkspace();
        var projectDirectory = workspace.CreateDirectory("project");
        workspace.WriteSource(projectDirectory, "Included.cs", ProjectIncludedSource);
        workspace.WriteSource(projectDirectory, "Excluded.cs", ProjectExcludedSource);

        var projectPath = Path.Combine(projectDirectory, "Sample.csproj");
        workspace.WriteSource(projectDirectory, "Sample.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
                <ImplicitUsings>disable</ImplicitUsings>
                <Nullable>disable</Nullable>
              </PropertyGroup>
              <ItemGroup>
                <Compile Remove="Excluded.cs" />
              </ItemGroup>
            </Project>
            """);

        var markdown = await GenerateCliMarkdownAsync(workspace, "project-output", $"/source={projectPath}");

        StringAssert.Contains(markdown, "public class IncludedType");
        Assert.IsFalse(markdown.Contains("ExcludedType", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task MarkdownOutput_UsesRequestedTargetFrameworkForMultiTargetProject()
    {
        using var workspace = new TestWorkspace();
        var projectDirectory = workspace.CreateDirectory("multitarget");
        workspace.WriteSource(projectDirectory, "Net8Only.cs", Net8Source);
        workspace.WriteSource(projectDirectory, "Net9Only.cs", Net9Source);

        var projectPath = Path.Combine(projectDirectory, "Sample.csproj");
        workspace.WriteSource(projectDirectory, "Sample.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFrameworks>net8.0;net9.0</TargetFrameworks>
                <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
                <ImplicitUsings>disable</ImplicitUsings>
                <Nullable>disable</Nullable>
              </PropertyGroup>
              <ItemGroup Condition="'$(TargetFramework)' == 'net8.0'">
                <Compile Include="Net8Only.cs" />
              </ItemGroup>
              <ItemGroup Condition="'$(TargetFramework)' == 'net9.0'">
                <Compile Include="Net9Only.cs" />
              </ItemGroup>
            </Project>
            """);

        var markdown = await GenerateCliMarkdownAsync(
            workspace,
            "multitarget-output",
            $"/source={projectPath}",
            "/tfm=net8.0");

        StringAssert.Contains(markdown, "public class Net8OnlyType");
        Assert.IsFalse(markdown.Contains("Net9OnlyType", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task MarkdownDiffOutput_ComparesCurrentProjectAgainstGitTag()
    {
        using var workspace = new TestWorkspace();
        var repositoryDirectory = workspace.CreateDirectory("repo-project");
        var projectDirectory = Path.Combine(repositoryDirectory, "src");
        Directory.CreateDirectory(projectDirectory);
        InitializeGitRepository(repositoryDirectory);

        var projectPath = Path.Combine(projectDirectory, "Sample.csproj");
        workspace.WriteSource(projectDirectory, "Sample.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
                <ImplicitUsings>disable</ImplicitUsings>
                <Nullable>disable</Nullable>
              </PropertyGroup>
            </Project>
            """);

        workspace.WriteSource(projectDirectory, "Types.cs", OldDiffSource);
        CommitAll(repositoryDirectory, "baseline");
        RunGit(repositoryDirectory, "tag", "baseline");

        workspace.WriteSource(projectDirectory, "Types.cs", NewDiffSource);

        var markdown = await GenerateCliMarkdownAsync(
            workspace,
            "git-project-diff",
            $"/source={projectPath}",
            "/compareRef=baseline");

        StringAssert.Contains(markdown, "<b>public NewMethod();</b>");
        StringAssert.Contains(markdown, "<strike>public OldMethod();</strike>");
    }

    private static async Task<string> GenerateMarkdownAsync(string source)
    {
        using var workspace = new TestWorkspace();
        var sourceDirectory = workspace.CreateDirectory("source");
        workspace.WriteSource(sourceDirectory, "Types.cs", source);
        return await GenerateOutputAsync(workspace, "markdown", sourceDirectory, null, new MarkdownGenerator(), ".md");
    }

    private static async Task<string> GenerateHtmlAsync(string source)
    {
        using var workspace = new TestWorkspace();
        var sourceDirectory = workspace.CreateDirectory("source");
        workspace.WriteSource(sourceDirectory, "Types.cs", source);
        return await GenerateOutputAsync(workspace, "html", sourceDirectory, null, new HtmlOmdGenerator(), ".html");
    }

    private static async Task<string> GenerateMarkdownDiffAsync(string oldSource, string newSource)
    {
        using var workspace = new TestWorkspace();
        var oldDirectory = workspace.CreateDirectory("old");
        var newDirectory = workspace.CreateDirectory("new");
        workspace.WriteSource(oldDirectory, "Types.cs", oldSource);
        workspace.WriteSource(newDirectory, "Types.cs", newSource);
        return await GenerateOutputAsync(workspace, "diff", newDirectory, oldDirectory, new MarkdownGenerator(), ".md");
    }

    private static async Task<string> GenerateHtmlDiffAsync(string oldSource, string newSource)
    {
        using var workspace = new TestWorkspace();
        var oldDirectory = workspace.CreateDirectory("old");
        var newDirectory = workspace.CreateDirectory("new");
        workspace.WriteSource(oldDirectory, "Types.cs", oldSource);
        workspace.WriteSource(newDirectory, "Types.cs", newSource);
        return await GenerateOutputAsync(workspace, "diff", newDirectory, oldDirectory, new HtmlOmdGenerator(), ".html");
    }

    private static async Task<string> GenerateCliMarkdownAsync(TestWorkspace workspace, string outputName, params string[] args)
    {
        var outputBasePath = Path.Combine(workspace.RootPath, outputName);
        var previousOutputLocation = GeneratorSettings.OutputLocation;
        var previousShowPrivateMembers = GeneratorSettings.ShowPrivateMembers;
        var previousShowInternalMembers = GeneratorSettings.ShowInternalMembers;

        try
        {
            await ProgramApp.RunAsync(args.Concat(new[]
            {
                $"/output={outputBasePath}",
                "/format=md"
            }).ToArray());
        }
        finally
        {
            GeneratorSettings.OutputLocation = previousOutputLocation;
            GeneratorSettings.ShowPrivateMembers = previousShowPrivateMembers;
            GeneratorSettings.ShowInternalMembers = previousShowInternalMembers;
        }

        return await File.ReadAllTextAsync(outputBasePath + ".md");
    }

    private static async Task<string> GenerateOutputAsync(TestWorkspace workspace, string outputName, string sourceDirectory, string? oldSourceDirectory, ICodeGenerator generator, string extension)
    {
        var outputBasePath = Path.Combine(workspace.RootPath, outputName);
        var app = new GeneratorApp(new[] { generator });
        var previousOutputLocation = GeneratorSettings.OutputLocation;
        var previousShowPrivateMembers = GeneratorSettings.ShowPrivateMembers;
        var previousShowInternalMembers = GeneratorSettings.ShowInternalMembers;

        try
        {
            GeneratorSettings.OutputLocation = outputBasePath;
            GeneratorSettings.ShowPrivateMembers = false;
            GeneratorSettings.ShowInternalMembers = false;

            if (oldSourceDirectory is null)
            {
                await app.Process(new[] { sourceDirectory }, Array.Empty<string>());
            }
            else
            {
                await app.ProcessDiffs(
                    new[] { oldSourceDirectory },
                    new[] { sourceDirectory },
                    Array.Empty<string>(),
                    Array.Empty<string>());
            }
        }
        finally
        {
            GeneratorSettings.OutputLocation = previousOutputLocation;
            GeneratorSettings.ShowPrivateMembers = previousShowPrivateMembers;
            GeneratorSettings.ShowInternalMembers = previousShowInternalMembers;
        }

        return await File.ReadAllTextAsync(outputBasePath + extension);
    }

    private static void AssertHtmlType(string html, string fullTypeName, string headerKind, string declarationKind)
    {
        var pattern = $@"id='{Regex.Escape(fullTypeName)}'.*?<div class='header {Regex.Escape(headerKind)}[^']*'>.*?<div class='typeKind'>{Regex.Escape(declarationKind)}</div>";
        StringAssert.Matches(html, new Regex(pattern, RegexOptions.Singleline));
    }

    private static void InitializeGitRepository(string repositoryDirectory)
    {
        RunGit(repositoryDirectory, "init");
        RunGit(repositoryDirectory, "config", "user.name", "Generator Tests");
        RunGit(repositoryDirectory, "config", "user.email", "generator.tests@example.com");
    }

    private static string CommitAll(string repositoryDirectory, string message)
    {
        RunGit(repositoryDirectory, "add", ".");
        RunGit(repositoryDirectory, "commit", "-m", message);
        return RunGit(repositoryDirectory, "rev-parse", "HEAD");
    }

    private static string RunGit(string workingDirectory, params string[] arguments)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

        process.Start();
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"git {string.Join(" ", arguments)} failed: {standardError}");

        return standardOutput.Trim();
    }

    private sealed class TestWorkspace : IDisposable
    {
        public TestWorkspace()
        {
            RootPath = Path.Combine(Path.GetTempPath(), "GeneratorTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(RootPath);
        }

        public string RootPath { get; }

        public string CreateDirectory(string name)
        {
            var directory = Path.Combine(RootPath, name);
            Directory.CreateDirectory(directory);
            return directory;
        }

        public void WriteSource(string directory, string fileName, string content)
        {
            File.WriteAllText(Path.Combine(directory, fileName), content);
        }

        public void Dispose()
        {
            if (!Directory.Exists(RootPath))
                return;

            for (var attempt = 0; attempt < 10; attempt++)
            {
                try
                {
                    ResetAttributes(RootPath);
                    Directory.Delete(RootPath, recursive: true);
                    return;
                }
                catch (IOException) when (attempt < 9)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    Thread.Sleep(50);
                }
                catch (UnauthorizedAccessException) when (attempt < 9)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    Thread.Sleep(50);
                }
            }
        }

        private static void ResetAttributes(string directory)
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
        }
    }
}
