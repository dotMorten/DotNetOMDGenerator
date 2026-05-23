using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;

namespace Generator
{
    internal sealed class GitResolvedSources : IDisposable
    {
        private readonly IReadOnlyList<string> tempPaths;

        internal GitResolvedSources(string[] sourcePaths, string[] compareSourcePaths, IReadOnlyList<string> tempPaths = null)
        {
            SourcePaths = sourcePaths;
            CompareSourcePaths = compareSourcePaths;
            this.tempPaths = tempPaths ?? Array.Empty<string>();
        }

        internal string[] SourcePaths { get; }

        internal string[] CompareSourcePaths { get; }

        public void Dispose()
        {
            foreach (var path in tempPaths.Reverse())
            {
                try
                {
                    if (Directory.Exists(path))
                        Directory.Delete(path, recursive: true);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
    }

    internal static class GitSourceResolver
    {
        internal static async Task<GitResolvedSources> ResolveAsync(string[] source, string[] compareSource, string gitRepo, string sourceRef, string compareRef)
        {
            if (string.IsNullOrWhiteSpace(sourceRef) && string.IsNullOrWhiteSpace(compareRef))
                return new GitResolvedSources(source, compareSource);

            if (source is null || source.Length == 0)
                throw new ArgumentException("The source parameter is required when using sourceRef or compareRef.");
            if (compareSource != null && compareSource.Length > 0 && !string.IsNullOrWhiteSpace(compareRef))
                throw new ArgumentException("compareSource can't be combined with compareRef.");
            if (!string.IsNullOrWhiteSpace(sourceRef) && string.IsNullOrWhiteSpace(compareRef))
                throw new ArgumentException("compareRef is required when sourceRef is specified.");

            var tempPaths = new List<string>();
            var repository = await ResolveRepositoryAsync(source, gitRepo, !string.IsNullOrWhiteSpace(sourceRef), tempPaths).ConfigureAwait(false);
            var pathSpecs = GetPathSpecs(source, repository.WorkingTreeRoot, allowRepoRelativePaths: !string.IsNullOrWhiteSpace(sourceRef));
            var resolvedSource = source;
            var resolvedCompareSource = compareSource;

            if (!string.IsNullOrWhiteSpace(sourceRef))
                resolvedSource = new[] { await MaterializeRefAsync(repository.GitDirectory, sourceRef, pathSpecs, tempPaths).ConfigureAwait(false) };

            if (!string.IsNullOrWhiteSpace(compareRef))
                resolvedCompareSource = new[] { await MaterializeRefAsync(repository.GitDirectory, compareRef, pathSpecs, tempPaths).ConfigureAwait(false) };

            return new GitResolvedSources(resolvedSource, resolvedCompareSource, tempPaths);
        }

        private static string[] GetPathSpecs(IEnumerable<string> source, string workingTreeRoot, bool allowRepoRelativePaths)
        {
            var pathSpecs = new List<string>();
            foreach (var item in source.Where(s => !string.IsNullOrWhiteSpace(s)))
            {
                var fullPath = Path.GetFullPath(item);
                if (File.Exists(fullPath) || Directory.Exists(fullPath))
                {
                    if (string.IsNullOrEmpty(workingTreeRoot))
                        throw new ArgumentException($"Source path '{item}' can't be mapped to a bare repository. Use repository-relative source paths instead.");

                    var relativePath = Path.GetRelativePath(workingTreeRoot, fullPath);
                    if (relativePath.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relativePath))
                        throw new ArgumentException($"Source path '{item}' must be inside the git repository '{workingTreeRoot}'.");

                    if (relativePath == "." || string.IsNullOrEmpty(relativePath))
                        return Array.Empty<string>();

                    pathSpecs.Add(NormalizePath(relativePath));
                }
                else if (allowRepoRelativePaths && !Path.IsPathRooted(item))
                {
                    var normalized = NormalizePath(item.TrimStart('.', '/', '\\'));
                    if (string.IsNullOrEmpty(normalized))
                        return Array.Empty<string>();
                    pathSpecs.Add(normalized);
                }
                else
                {
                    throw new ArgumentException($"Source path '{item}' does not exist.");
                }
            }

            if (pathSpecs.Any(p => string.IsNullOrEmpty(p)))
                return Array.Empty<string>();

            return pathSpecs.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        private static async Task<string> MaterializeRefAsync(string repositoryPath, string gitRef, string[] pathSpecs, List<string> tempPaths)
        {
            await EnsureRefExistsAsync(repositoryPath, gitRef).ConfigureAwait(false);

            var outputDirectory = CreateTempDirectory();
            tempPaths.Add(outputDirectory);

            var existingPathSpecs = pathSpecs;
            if (pathSpecs.Length > 0)
                existingPathSpecs = await GetExistingPathSpecsAsync(repositoryPath, gitRef, pathSpecs).ConfigureAwait(false);

            if (existingPathSpecs.Length == 0 && pathSpecs.Length > 0)
                return outputDirectory;

            var archivePath = Path.Combine(Path.GetTempPath(), "DotNetOMDGenerator", Guid.NewGuid().ToString("N") + ".zip");
            Directory.CreateDirectory(Path.GetDirectoryName(archivePath));

            try
            {
                var arguments = new List<string> { "archive", "--format=zip", $"--output={archivePath}", gitRef };
                if (existingPathSpecs.Length > 0)
                {
                    arguments.Add("--");
                    arguments.AddRange(existingPathSpecs);
                }

                await RunGitAsync(repositoryPath, arguments).ConfigureAwait(false);
                ZipFile.ExtractToDirectory(archivePath, outputDirectory);
            }
            finally
            {
                if (File.Exists(archivePath))
                    File.Delete(archivePath);
            }

            return outputDirectory;
        }

        private static async Task<string[]> GetExistingPathSpecsAsync(string repositoryPath, string gitRef, IEnumerable<string> pathSpecs)
        {
            var existing = new List<string>();
            foreach (var pathSpec in pathSpecs)
            {
                var result = await RunGitAsync(repositoryPath, new[] { "ls-tree", "--name-only", gitRef, "--", pathSpec }, allowFailure: true).ConfigureAwait(false);
                if (result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.StandardOutput))
                    existing.Add(pathSpec);
            }

            return existing.ToArray();
        }

        private static async Task EnsureRefExistsAsync(string repositoryPath, string gitRef)
        {
            var result = await RunGitAsync(repositoryPath, new[] { "rev-parse", "--verify", $"{gitRef}^{{commit}}" }, allowFailure: true).ConfigureAwait(false);
            if (result.ExitCode != 0)
                throw new ArgumentException($"Git reference '{gitRef}' could not be resolved.");
        }

        private static async Task<RepositoryContext> ResolveRepositoryAsync(string[] source, string gitRepo, bool allowRepoRelativePaths, List<string> tempPaths)
        {
            if (string.IsNullOrWhiteSpace(gitRepo))
            {
                if (allowRepoRelativePaths && source.Any(s => !Path.IsPathRooted(s) && !File.Exists(Path.GetFullPath(s)) && !Directory.Exists(Path.GetFullPath(s))))
                    throw new ArgumentException("gitRepo is required when sourceRef is used with repository-relative source paths.");

                var repositoryRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in source)
                {
                    var fullPath = Path.GetFullPath(item);
                    var probePath = File.Exists(fullPath) ? Path.GetDirectoryName(fullPath) : fullPath;
                    if (!Directory.Exists(probePath))
                        throw new ArgumentException($"Source path '{item}' does not exist.");

                    repositoryRoots.Add(await GetRepositoryRootAsync(probePath).ConfigureAwait(false));
                }

                if (repositoryRoots.Count != 1)
                    throw new ArgumentException("All source paths must belong to the same git repository when compareRef is used.");

                var repositoryRoot = repositoryRoots.Single();
                return new RepositoryContext(repositoryRoot, repositoryRoot);
            }

            if (Directory.Exists(gitRepo))
            {
                var workingTreeResult = await RunGitAsync(gitRepo, new[] { "rev-parse", "--show-toplevel" }, allowFailure: true).ConfigureAwait(false);
                if (workingTreeResult.ExitCode == 0)
                {
                    var workingTreeRoot = workingTreeResult.StandardOutput.Trim();
                    return new RepositoryContext(workingTreeRoot, workingTreeRoot);
                }

                var bareResult = await RunGitAsync(gitRepo, new[] { "rev-parse", "--is-bare-repository" }, allowFailure: true).ConfigureAwait(false);
                if (bareResult.ExitCode == 0 && bareResult.StandardOutput.Trim().Equals("true", StringComparison.OrdinalIgnoreCase))
                    return new RepositoryContext(gitRepo, null);

                throw new ArgumentException($"'{gitRepo}' is not a valid git repository.");
            }

            if (!IsRemoteRepository(gitRepo))
                throw new ArgumentException($"'{gitRepo}' is not a valid git repository path or URL.");

            var cloneDirectory = CreateTempDirectory();
            tempPaths.Add(cloneDirectory);
            var cloneParent = Directory.GetParent(cloneDirectory).FullName;
            var cloneName = Path.GetFileName(cloneDirectory);

            var shallowClone = await RunGitAsync(
                cloneParent,
                new[] { "clone", "--filter=blob:none", "--no-checkout", "--quiet", gitRepo, cloneName },
                allowFailure: true).ConfigureAwait(false);
            if (shallowClone.ExitCode != 0)
                await RunGitAsync(cloneParent, new[] { "clone", "--no-checkout", "--quiet", gitRepo, cloneName }).ConfigureAwait(false);

            return new RepositoryContext(cloneDirectory, cloneDirectory);
        }

        private static async Task<string> GetRepositoryRootAsync(string path)
        {
            var result = await RunGitAsync(path, new[] { "rev-parse", "--show-toplevel" }, allowFailure: true).ConfigureAwait(false);
            if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.StandardOutput))
                throw new ArgumentException($"'{path}' is not inside a git repository.");

            return result.StandardOutput.Trim();
        }

        private static bool IsRemoteRepository(string gitRepo)
        {
            if (!Uri.TryCreate(gitRepo, UriKind.Absolute, out var uri))
                return false;

            return uri.Scheme == Uri.UriSchemeHttp
                || uri.Scheme == Uri.UriSchemeHttps
                || uri.Scheme == Uri.UriSchemeFile
                || uri.Scheme == Uri.UriSchemeFtp
                || uri.Scheme.Equals("git", StringComparison.OrdinalIgnoreCase)
                || uri.Scheme.Equals("ssh", StringComparison.OrdinalIgnoreCase);
        }

        private static string CreateTempDirectory()
        {
            var path = Path.Combine(Path.GetTempPath(), "DotNetOMDGenerator", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static string NormalizePath(string path) => path.Replace('\\', '/').Trim('/');

        private static async Task<GitCommandResult> RunGitAsync(string workingDirectory, IEnumerable<string> arguments, bool allowFailure = false)
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
            var standardOutput = process.StandardOutput.ReadToEndAsync();
            var standardError = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().ConfigureAwait(false);

            var result = new GitCommandResult(
                process.ExitCode,
                await standardOutput.ConfigureAwait(false),
                await standardError.ConfigureAwait(false));

            if (!allowFailure && result.ExitCode != 0)
            {
                var message = string.IsNullOrWhiteSpace(result.StandardError) ? "Unknown git error." : result.StandardError.Trim();
                throw new InvalidOperationException(message);
            }

            return result;
        }

        private sealed record RepositoryContext(string GitDirectory, string WorkingTreeRoot);

        private sealed record GitCommandResult(int ExitCode, string StandardOutput, string StandardError);
    }
}
