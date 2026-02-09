using System.IO;
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
        /// Gets the search root directory based on the current VS context.
        /// </summary>
        public async Task<string> GetSearchRootAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            // Try to get the solution path first as it's needed for fallback
            Solution solution = await VS.Solutions.GetCurrentSolutionAsync();
            var solutionPath = solution?.FullPath;
            var solutionDir = string.Empty;

            if (!string.IsNullOrEmpty(solutionPath))
            {
                // Solution path can be pointing to a folder not just a .sln file in Open Folder scenario, so we need to check if it's a directory first
                if (Directory.Exists(solutionPath))
                {
                    solutionDir = solutionPath; // Open Folder scenario where FullPath is a directory
                }
                else
                {
                    solutionDir = Path.GetDirectoryName(solutionPath);
                }
            
                // Try git repo root first (highest priority)
                var gitRoot = FindGitRoot(solutionDir);
                if (gitRoot != null)
                {
                    return gitRoot;
                }
            }

            // Fallback to solution directory
            if (!string.IsNullOrEmpty(solutionDir) && Directory.Exists(solutionDir))
            {
                return solutionDir;
            }

            // Fallback to open folder
            var openFolder = await GetOpenFolderAsync();
            if (!string.IsNullOrEmpty(openFolder))
            {
                // Also check for git root in open folder
                var gitRoot = FindGitRoot(openFolder);
                return gitRoot ?? openFolder;
            }

            return null;
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
                    if (string.IsNullOrEmpty(solutionFullName) || !solutionFullName.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
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
            catch
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
