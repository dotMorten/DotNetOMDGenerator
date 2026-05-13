# Copilot Instructions for DotNetOMDGenerator



## Build and test commands


- Build the solution from the repo root: `dotnet build src/Generator.sln`
- Run the full test suite: `dotnet test src/Generator.sln`
- Run a single test: `dotnet test src/Generator.Tests/Generator.Tests.csproj --filter "FullyQualifiedName\~Generator.Tests.GeneratorOutputTests.HtmlOutput\_IdentifiesAllSupportedDeclarationKinds"`
- Pack the tool/NuGet package: `dotnet pack src/Generator/Generator.csproj -c Release`

`src/Generator/Generator.csproj` has `GeneratePackageOnBuild=true`, so normal builds also emit a package into 
`src/nupkg`. There is no separate lint command configured in this repository.

## High-level architecture

- `src/Generator/Program.cs` is the CLI entrypoint. It parses `-`, `--`, and `/` arguments, selects one or more output generators from `format`, expands `nuget=id:version` inputs into assemblies, and dispatches either normal generation (`Generator.Process`) or diff generation (`Generator.ProcessDiffs`).
- `src/Generator/WorkspaceHelper.cs` owns the Roslyn pipeline. It builds an `AdhocWorkspace`, loads C# sources from  folders, individual files, zip files, downloaded zip/plain-text URLs, and metadata assemblies, then creates a compilation and extracts the symbols that feed every output format.
- `src/Generator/TypeExtensions.cs` is the shared symbol/model layer. It centralizes member visibility filtering, declaration-kind detection (`class`, `record`, `record struct`, `delegate`, enum-like metadata types), XML-doc summary extraction, and the member-by-member diff helpers used by both output backends.
- Output backends are pluggable through `ICodeGenerator` and `ICodeDiffGenerator`:
 - `src/Generator/Generators/HtmlOmdGenerator.cs` writes the interactive HTML report, grouped by namespace, with embedded header/footer resources and cross-links for symbols in the analyzed set.
 - `src/Generator/Generators/MarkdownGenerator.cs` writes a `<pre>`-based Markdown snapshot/diff.
  - `src/Generator/Generators/OMDGenerator.cs` is a legacy image renderer and is explicitly excluded from the current build by `Generator.csproj`.
- Diff mode compares two compilations rather than using a separate pipeline. `Generator.GetChangedSymbols(...)` computes added/removed/changed top-level types, and both generators recurse into nested types with the same comparison helpers.
- `src/Generator.Tests/GeneratorOutputTests.cs` is the regression suite. Tests create temporary source trees, instantiate the internal `Generator` directly, and assert on generated `.html`/`.md` text instead of shelling out to the CLI.

## Key repository conventions

- `GeneratorSettings` is global mutable state for `ShowPrivateMembers`, `ShowInternalMembers`, and `OutputLocation`. Tests already save and restore it; new code should do the same.
- An output path without an extension is treated as a basename. Each generator appends its own extension (`.html` or `.md`). If the output path points to a directory, `Program` rewrites it to `<directory>/OMD`.
- Visibility rules are shared through `TypeExtensions`, not duplicated in generators. If you change what is considered visible/public/internal/private, update the extension helpers first.
- Declaration-kind handling is also shared through `TypeExtensions.GetDeclarationKind()` and `GetStyleKind()`. Keep HTML and Markdown behavior aligned with those helpers, especially for records, record structs, delegates, and enum-like symbols loaded from assemblies.
- Member diffing intentionally suppresses some noisy changes:
  - removed overrides are ignored
  - members moved up to a base type are not reported as removals
 - methods/constructors with optional parameters can be treated as equivalent to a matching set of explicit overloads
- Namespace sections in both generators rely on symbols being pre-sorted by namespace then name in `Generator.GetSymbols(...)`.
- `exclude` filtering is matched against full paths with `/` separators, even on Windows. Keep that behavior in sync with the README examples and `Program.CreateFilter(...)`.
- `dotMorten.OmdGenerator.targets` is part of the shipped package experience. If CLI arguments, output naming, or build integration change, update that target file and the README usage examples together.
- HTML output depends on embedded resources `Generators/HtmlOmdHeader.html` and `Generators/HtmlOmdFooter.html`; keep the resource names and `GetManifestResourceStream(...)` calls aligned with the project file.
- Tests are marked with `\[assembly: DoNotParallelize]` because they mutate static generator settings and write files to temp directories.

