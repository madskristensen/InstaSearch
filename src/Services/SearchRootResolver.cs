using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using EnvDTE80;

namespace InstaSearch.Services
{
    /// <summary>
    /// Resolves the search root directory in priority order:
    /// 1. Git repository root
    /// 2. Solution directory
    /// 3. Open folder
    /// </summary>
    public class SearchRootResolver
    {
        /// <summary>
        /// Gets all search root directories based on current VS context.
        /// Includes solution/open-folder roots and all loaded project roots.
        /// </summary>
        public async Task<IReadOnlyList<string>> GetSearchRootsAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var solutionPath = await GetSolutionPathAsync();
            var solutionDir = NormalizeToDirectory(solutionPath);
            if (!string.IsNullOrEmpty(solutionDir))
            {
                candidates.Add(solutionDir);
            }

            foreach (var projectDirectory in ParseSolutionProjectDirectories(solutionPath))
            {
                candidates.Add(projectDirectory);
            }

            var openFolder = await GetOpenFolderAsync();
            var openFolderDir = NormalizeToDirectory(openFolder);
            if (!string.IsNullOrEmpty(openFolderDir))
            {
                candidates.Add(openFolderDir);
            }

            if (candidates.Count == 0)
            {
                return [];
            }

            var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var candidate in candidates)
            {
                var gitRoot = FindGitRoot(candidate);
                var root = gitRoot ?? candidate;
                roots.Add(Path.GetFullPath(root));
            }

            return [.. roots.OrderBy(path => path, StringComparer.OrdinalIgnoreCase)];
        }

        /// <summary>
        /// Gets the currently opened workspace path for MRU tracking.
        /// Returns a solution/project file path when available; otherwise an open folder path.
        /// </summary>
        public async Task<string> GetCurrentWorkspacePathForMruAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var solutionPath = await GetSolutionPathAsync();

            if (!string.IsNullOrEmpty(solutionPath))
            {
                if (File.Exists(solutionPath) || Directory.Exists(solutionPath))
                {
                    return solutionPath;
                }
            }

            return await GetOpenFolderAsync();
        }

        /// <summary>
        /// Gets the search root directory based on the current VS context.
        /// </summary>
        public async Task<string> GetSearchRootAsync()
        {
            IReadOnlyList<string> roots = await GetSearchRootsAsync();
            return roots.Count > 0 ? roots[0] : null;
        }

        private static IEnumerable<string> ParseSolutionProjectDirectories(string solutionPath)
        {
            if (string.IsNullOrWhiteSpace(solutionPath)
                || !solutionPath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)
                || !File.Exists(solutionPath))
            {
                yield break;
            }

            var solutionDirectory = Path.GetDirectoryName(solutionPath);
            if (string.IsNullOrEmpty(solutionDirectory))
            {
                yield break;
            }

            var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            IEnumerable<string> lines;
            try
            {
                lines = File.ReadLines(solutionPath);
            }
            catch (IOException)
            {
                yield break;
            }
            catch (UnauthorizedAccessException)
            {
                yield break;
            }

            foreach (var line in lines)
            {
                var projectPath = ExtractProjectPathFromSolutionLine(line);
                if (string.IsNullOrWhiteSpace(projectPath)
                    || projectPath.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var candidatePath = Path.IsPathRooted(projectPath)
                    ? projectPath
                    : Path.Combine(solutionDirectory, projectPath);

                var projectDirectory = NormalizeToDirectory(candidatePath);
                if (!string.IsNullOrEmpty(projectDirectory))
                {
                    directories.Add(projectDirectory);
                }
            }

            foreach (var directory in directories)
            {
                yield return directory;
            }
        }

        private static string ExtractProjectPathFromSolutionLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("Project(", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var firstComma = line.IndexOf(',');
            if (firstComma < 0)
            {
                return null;
            }

            var secondComma = line.IndexOf(',', firstComma + 1);
            if (secondComma < 0)
            {
                return null;
            }

            var pathSegment = line.Substring(firstComma + 1, secondComma - firstComma - 1).Trim();
            if (pathSegment.Length >= 2 && pathSegment[0] == '"' && pathSegment[pathSegment.Length - 1] == '"')
            {
                pathSegment = pathSegment.Substring(1, pathSegment.Length - 2);
            }

            return pathSegment;
        }

        private static async Task<string> GetSolutionPathAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            DTE2 dte = await VS.GetServiceAsync<EnvDTE.DTE, DTE2>();
            if (dte?.Solution == null)
            {
                return null;
            }

            var solutionFullName = dte.Solution.FullName;
            if (!string.IsNullOrWhiteSpace(solutionFullName))
            {
                return solutionFullName;
            }

            try
            {
                return dte.Solution.Properties?.Item("Path")?.Value as string;
            }
            catch (COMException)
            {
                return null;
            }
        }

        private static string NormalizeToDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            if (Directory.Exists(path))
            {
                return Path.GetFullPath(path);
            }

            if (!File.Exists(path))
            {
                return null;
            }

            var directory = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                return null;
            }

            return Path.GetFullPath(directory);
        }

        private async Task<string> GetOpenFolderAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                DTE2 dte = await VS.GetServiceAsync<EnvDTE.DTE, DTE2>();
                if (dte?.Solution != null)
                {
                    // Check if this is an "Open Folder" scenario (no .sln file)
                    var solutionFullName = dte.Solution.FullName;
                    if (string.IsNullOrEmpty(solutionFullName)
                        || (!solutionFullName.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)
                            && !solutionFullName.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase)))
                    {
                        // Try to get the folder from the solution's properties
                        var solutionDir = dte.Solution.Properties?.Item("Path")?.Value as string;
                        if (!string.IsNullOrEmpty(solutionDir))
                        {
                            return Path.GetDirectoryName(solutionDir);
                        }
                    }
                }
            }
            catch (COMException)
            {
                // Ignore errors accessing DTE
            }

            return null;
        }

        /// <summary>
        /// Walks up the directory tree to find a .git folder.
        /// </summary>
        private static string FindGitRoot(string startPath)
        {
            var current = startPath;
            while (!string.IsNullOrEmpty(current))
            {
                var gitPath = Path.Combine(current, ".git");
                if (Directory.Exists(gitPath) || File.Exists(gitPath)) // .git can be a file for submodules
                {
                    return current;
                }

                DirectoryInfo parent = Directory.GetParent(current);
                if (parent == null)
                {
                    break;
                }
                current = parent.FullName;
            }
            return null;
        }
    }
}
