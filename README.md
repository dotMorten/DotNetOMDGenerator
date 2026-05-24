# .NET Object Model Diagram Generator

A cross-platform Roslyn-based tool that generates an object model diagram of a set of C# source files and/or .NET assemblies

## Sponsoring

If you like this library and use it a lot, consider sponsoring me. Anything helps and encourages me to keep going.

See here for details: https://github.com/sponsors/dotMorten


### Install

Run the following command from commandline (requires .NET Core 2.1 installed):
```
dotnet tool install --global dotMorten.OmdGenerator
```


### Usage:
```
generateomd --source=[source folder|file|project] --compareSource=[oldSource folder|file|project] --gitRepo=[repo path or url] --sourceRef=[commit|branch|tag] --compareRef=[commit|branch|tag] --preprocessors=[defines] --format=[html|md] --showPrivate -showInternal

Required parameters:
  source            Specifies folders, source files, or .csproj files to include for the object model.
                    Separate with ; for multiple items
or
  assemblies        Specifies a set of assemblies to include for the object model.
                    Separate with ; for multiple assemblies, or use wildcards
				
Optional parameters:
  compareSource     Specifies a folder of old source to compare and generate a diff model
                    This can be useful for finding API changes or compare branches
  gitRepo           Specifies a local git repository path or remote git URL to resolve source paths from when using `--sourceRef` or `--compareRef`
  sourceRef         Specifies the git commit, branch, or tag to use for the source side of a diff
  compareRef        Specifies the git commit, branch, or tag to compare the current source or /sourceRef against
  compareAssemblies Specifies a set of old assemblies to compare and generate a adiff model.
                    Separate with; for multiple assemblies, or use wildcards
  format            Format to generate: 
                       `html` a single html output (html is default)
                       `md` for markdown you can copy-paste to for instance GitHub.
		       Specify multiple with a semicolon seperator, and use an output filename without extension
  preprocessors     Define a set of preprocessors values. Use ; to separate multiple
  exclude           Defines one or more strings that can't be part of the path Ie '*/Samples/*;*/UnitTests/*'
                    (use forward slash for folder separators)
  regexfilter       Defines a regular expression for filtering on full file names in the source
  showPrivate       Show private members (default is false)
  showInternal      Show internal members (default is false)
  output            Filename to write the output to (extension is optional, but exclude the extension if you specify multiple formats)
  tfm               Target Framework to use against NuGet packages or multi-targeted source projects.
                    When omitted for project inputs, the generator evaluates all supported target frameworks,
                    merges the resulting APIs, and annotates APIs that only exist on some TFMs.
  nugetDependencies Dependency package ID patterns to include for /nuget and /compareNuget.
                    Separate with ; for multiple patterns and prefix with ! to exclude a package or subtree.
```


### NuGet
As an alternative you can also reference a [NuGet package](https://www.nuget.org/packages/dotMorten.OmdGenerator/) to your class library, and set up a post-build script to generate an Object Model Diagram HTML file:

```
Install-Package dotMorten.OmdGenerator 
```

Add the following to your project:
```xml
  <Target Name="GenerateObjectModel" AfterTargets="Compile">
    <Exec Command="dotnet &quot;$(DotNetOMDGeneratorToolPath)&quot; /source=&quot;@(Compile)&quot; /preprocessors=&quot;$(DefineConstants)&quot; /output=&quot;$(OutputPath)$(TargetName)&quot;" WorkingDirectory="$(ProjectDir)" />
  </Target>
 
```


An example of a generated output for all of .NET Core can be found [here](http://www.sharpgis.net/Tests/corefx.html).

It can also be used to compare two folders (for instance two separate branches) and only show changes to the API. [Here's an example of .NET CoreFX v2.0 vs Master](http://www.sharpgis.net/Tests/corefx_new.html).

[![Screenshot](Screenshot.png)](http://www.sharpgis.net/Tests/corefx.html)


### Examples

Generate OMD for .NET Core FX source code, and ignore ref and test folders:
```
generateomd --source=c:\github\dotnet\corefx\src --exclude="*/ref/*;*/tests/*;*/perftests/*"
```

Compare .NET CoreFX Master with v2.0.0 repo branches directly from their Github zipped downloads:

```
generateomd --source=https://github.com/dotnet/corefx/archive/master.zip --compareSource=https://github.com/dotnet/corefx/archive/release/2.0.0.zip --exclude="*/ref/*;*/tests/*;*/perftests/*"
```

Generate OMD from the files actually included by a project instead of every `.cs` file in the folder:
```
generateomd --source=c:\github\MyLibrary\src\MyLibrary\MyLibrary.csproj
```

Select the target framework when the project is multi-targeted:
```
generateomd --source=c:\github\MyLibrary\src\MyLibrary\MyLibrary.csproj --tfm=net8.0
```

Merge APIs across multiple project files or across all TFMs of a multi-targeted project when `--tfm` is omitted:
```
generateomd --source=c:\github\MyLibrary\src\MyLibrary\MyLibrary.csproj;c:\github\MyLibrary\src\MyLibrary.Wpf\MyLibrary.Wpf.csproj
```

APIs that are only available on some target frameworks are annotated in the output:
```
public class Net8OnlyType [TFMs: net8.0]
```

Compare the current checkout against a tagged release from the same git repository:
```
generateomd --source=c:\github\dotnet\runtime\src\libraries\System.Text.Json\src --compareRef=v8.0.0
```

Compare a project file against a tag from the same git repository:
```
generateomd --source=c:\github\dotnet\runtime\src\libraries\System.Text.Json\src\System.Text.Json.csproj --compareRef=v8.0.0 --tfm=net8.0
```

Compare two commits from a remote git repository by resolving the selected repo-relative source path from both refs:
```
generateomd --source=src/libraries/System.Text.Json/src --gitRepo=https://github.com/dotnet/runtime.git --sourceRef=9f4f4cf --compareRef=v8.0.0
```

What's new in Xamarin.Forms? Compare assemblies from the nuget cache:
```
generateomd --assemblies=%USERPROFILE%\.nuget\packages\xamarin.forms\3.3.0.912540\lib\netstandard2.0\*.dll --compareAsssemblies=%USERPROFILE%\.nuget\packages\xamarin.forms\3.2.0.871581\lib\netstandard2.0\*.dll
```

Compare a meta package and include only matching dependency packages in the analysis:
```
generateomd --nuget=Microsoft.WindowsAppSDK:1.0.0 --compareNuget=Microsoft.WindowsAppSDK:0.8.0 --tfm=net8.0-windows10.0.19041.0 --nugetDependencies="Microsoft.WindowsAppSDK.*;!Microsoft.WindowsAppSDK.Tests.*"
```

### GitHub Actions: comment PR API changes

You can use the git ref comparison support in a pull request workflow to generate the markdown diff between the PR head commit and the PR base commit, then post the result as a PR comment.

In the workflow below:
- `--source` is the repo-relative path to the C# source you want to analyze.
- `sourceRef` is the PR head SHA.
- `compareRef` is the PR base SHA.
- When the diff becomes empty again, the workflow updates its existing comment to say so.
- If the workflow has never commented on the PR before and there are no API changes, it does nothing.

```yaml
name: PR API diff

on:
  pull_request:
    types: [opened, synchronize, reopened]

permissions:
  contents: read
  pull-requests: write

jobs:
  api-diff:
    runs-on: ubuntu-latest

    steps:
      - name: Set up .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 8.0.x

      - name: Install generator
        run: dotnet tool install --global dotMorten.OmdGenerator

      - name: Generate API diff
        shell: bash
        run: |
          generateomd \
            --source=src/MyLibrary/MyLibrary.csproj \
            --gitRepo=${{ github.server_url }}/${{ github.repository }} \
            --sourceRef=${{ github.event.pull_request.head.sha }} \
            --compareRef=${{ github.event.pull_request.base.sha }} \
            --format=md \
            --output=api-diff

      - name: Check whether API changes were found
        id: api_diff
        shell: bash
        run: |
          if grep -q '^namespace ' api-diff.md; then
            echo "has_changes=true" >> "$GITHUB_OUTPUT"
          else
            echo "has_changes=false" >> "$GITHUB_OUTPUT"
          fi

      - name: Create or update PR comment
        uses: actions/github-script@v7
        env:
          COMMENT_MARKER: <!-- dotnet-omd-api-diff -->
          HAS_CHANGES: ${{ steps.api_diff.outputs.has_changes }}
        with:
          github-token: ${{ secrets.GITHUB_TOKEN }}
          script: |
            const fs = require('fs');
            const marker = process.env.COMMENT_MARKER;
            const hasChanges = process.env.HAS_CHANGES === 'true';
            const diff = fs.readFileSync('api-diff.md', 'utf8').trim();

            const { owner, repo } = context.repo;
            const issue_number = context.payload.pull_request.number;

            const comments = await github.paginate(github.rest.issues.listComments, {
              owner,
              repo,
              issue_number,
              per_page: 100
            });

            const existing = comments.find(comment =>
              comment.user.type === 'Bot' && comment.body.includes(marker));

            if (!hasChanges && !existing) {
              return;
            }

            const body = hasChanges
              ? [
                  marker,
                  '## API changes',
                  '',
                  diff
                ].join('\n')
              : [
                  marker,
                  '## API changes',
                  '',
                  'No API changes are currently detected in this pull request.'
                ].join('\n');

            if (existing) {
              await github.rest.issues.updateComment({
                owner,
                repo,
                comment_id: existing.id,
                body
              });
            } else {
              await github.rest.issues.createComment({
                owner,
                repo,
                issue_number,
                body
              });
            }
```

If you prefer to remove the old bot comment instead of replacing it with a "no API changes" message, change the `!hasChanges` branch to call `github.rest.issues.deleteComment(...)` when `existing` is found.

If you want the comment to cover multiple source roots, separate them with semicolons in `--source`, for example `--source=src/MyLibrary;src/MyOtherLibrary`.

If your PRs come from forks and you want to comment on those PRs too, you may need a `pull_request_target` workflow instead of `pull_request`. Use that carefully, since it runs with broader repository permissions.

### GitHub Actions: add API diff to release notes

You can also use the same git ref support to append API changes to a GitHub Release. The workflow below runs when a release is published, finds the previous published release, compares the two tags, and inserts or updates a marked API diff section in the release body.

In the workflow below:
- `source` is the repo-relative path to the C# source you want to analyze.
- `sourceRef` is the current release tag.
- `compareRef` is the previous published release tag.
- If there is no previous release, the workflow exits without changing the release body.
- If the diff is empty, the workflow writes a short "no API changes" note into the marked release section.

```yaml
name: Release API diff

on:
  release:
    types: [published]

permissions:
  contents: write

jobs:
  api-diff:
    runs-on: ubuntu-latest

    steps:
      - name: Set up .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 8.0.x

      - name: Install generator
        run: dotnet tool install --global dotMorten.OmdGenerator

      - name: Find previous published release
        id: previous_release
        uses: actions/github-script@v7
        with:
          github-token: ${{ secrets.GITHUB_TOKEN }}
          script: |
            const releases = await github.paginate(github.rest.repos.listReleases, {
              owner: context.repo.owner,
              repo: context.repo.repo,
              per_page: 100
            });

            const currentReleaseId = context.payload.release.id;
            const publishedReleases = releases.filter(release => !release.draft);
            const currentIndex = publishedReleases.findIndex(release => release.id === currentReleaseId);

            if (currentIndex === -1 || currentIndex === publishedReleases.length - 1) {
              core.setOutput('tag', '');
              return;
            }

            core.setOutput('tag', publishedReleases[currentIndex + 1].tag_name);

      - name: Generate API diff
        if: steps.previous_release.outputs.tag != ''
        shell: bash
        run: |
          generateomd \
            --source=src/MyLibrary.csproj \
            --gitRepo=${{ github.server_url }}/${{ github.repository }} \
            --sourceRef=${{ github.event.release.tag_name }} \
            --compareRef=${{ steps.previous_release.outputs.tag }} \
            --format=md \
            --output=api-diff

      - name: Check whether API changes were found
        if: steps.previous_release.outputs.tag != ''
        id: api_diff
        shell: bash
        run: |
          if grep -q '^namespace ' api-diff.md; then
            echo "has_changes=true" >> "$GITHUB_OUTPUT"
          else
            echo "has_changes=false" >> "$GITHUB_OUTPUT"
          fi

      - name: Update release notes
        if: steps.previous_release.outputs.tag != ''
        uses: actions/github-script@v7
        env:
          SECTION_MARKER: <!-- dotnet-omd-api-diff -->
          HAS_CHANGES: ${{ steps.api_diff.outputs.has_changes }}
          PREVIOUS_TAG: ${{ steps.previous_release.outputs.tag }}
        with:
          github-token: ${{ secrets.GITHUB_TOKEN }}
          script: |
            const fs = require('fs');
            const marker = process.env.SECTION_MARKER;
            const hasChanges = process.env.HAS_CHANGES === 'true';
            const previousTag = process.env.PREVIOUS_TAG;
            const diff = fs.readFileSync('api-diff.md', 'utf8').trim();

            const release = context.payload.release;
            const existingBody = release.body || '';

            const section = hasChanges
              ? [
                  marker,
                  `## API changes since ${previousTag}`,
                  '',
                  diff
                ].join('\n')
              : [
                  marker,
                  `## API changes since ${previousTag}`,
                  '',
                  'No API changes are detected since the previous release.'
                ].join('\n');

            const markerIndex = existingBody.indexOf(marker);
            const body = markerIndex >= 0
              ? existingBody.slice(0, markerIndex).trimEnd() + '\n\n' + section
              : (existingBody.trim()
                  ? existingBody.trimEnd() + '\n\n' + section
                  : section);

            await github.rest.repos.updateRelease({
              owner: context.repo.owner,
              repo: context.repo.repo,
              release_id: release.id,
              tag_name: release.tag_name,
              name: release.name,
              body,
              draft: release.draft,
              prerelease: release.prerelease
            });
```

If you would rather omit the API section entirely when no changes are found, change the `section` assignment so the `!hasChanges` branch returns an empty string and skip the `updateRelease` call when there is no existing marker.

If you want to compare against the previous stable release only, filter out prereleases in the `publishedReleases` list before picking the previous tag.
