using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace ProjectAnalyzer
{
    public class CommitInfo
    {
        public string Hash { get; set; } = "";
        public string Date { get; set; } = "";
        public string Message { get; set; } = "";
        public string? PullRequestNumber { get; set; }
    }

    public class GitHistoryManager : IDisposable
    {
        private readonly string _originalRepoPath;
        private readonly string _originalGameFolder;
        private string? _originalBranchOrCommit;
        private bool _isRestored;

        public string RepoPath => _originalRepoPath;
        public string TempGameFolder => _originalGameFolder;

        public GitHistoryManager(string gameFolder)
        {
            _originalGameFolder = Path.GetFullPath(gameFolder);
            _originalRepoPath = GetGitRoot(_originalGameFolder);
        }

        public void InitializeClone()
        {
            _originalBranchOrCommit = GetCurrentBranchOrCommit();
            Console.WriteLine($"[GitManager] Target repository: {_originalRepoPath}");
            Console.WriteLine($"[GitManager] Saved original branch/commit: {_originalBranchOrCommit}");
        }

        private string GetCurrentBranchOrCommit()
        {
            try
            {
                // Try to get symbolic name (branch name)
                string branch = RunGitCommand("symbolic-ref --short -q HEAD").Trim();
                if (!string.IsNullOrEmpty(branch))
                    return branch;
            }
            catch (Exception ex)
            {
                // Fallback to commit hash if detached HEAD (e.g., in detached HEAD state)
                SharedUtilities.Logger.Log(SharedUtilities.LogLevel.Debug, $"Could not get branch name (detached HEAD?): {ex.Message}");
            }

            try
            {
                return RunGitCommand("rev-parse HEAD").Trim();
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to retrieve current Git branch or commit hash.", ex);
            }
        }

        private string GetGitRoot(string path)
        {
            string current = path;
            while (!string.IsNullOrEmpty(current))
            {
                if (Directory.Exists(Path.Combine(current, ".git")))
                    return current;
                current = Path.GetDirectoryName(current) ?? "";
            }
            throw new Exception($"Path {path} is not part of a Git repository.");
        }

        public bool IsGitRepo()
        {
            try
            {
                GetGitRoot(_originalGameFolder);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public List<CommitInfo> GetCommitHistory(int limit = 0)
        {
            string limitArg = limit > 0 ? $"-n {limit} " : "";
            var output = RunGitCommand($"log {limitArg}--format=\"%H|%ai|%s\" --reverse HEAD");
            var commits = new List<CommitInfo>();

            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split('|');
                if (parts.Length >= 3)
                {
                    string message = string.Join('|', parts.Skip(2));
                    string? prNumber = null;
                    
                    var prMatch = Regex.Match(message, @"#(\d+)");
                    if (prMatch.Success)
                    {
                        prNumber = prMatch.Groups[1].Value;
                    }
                    else
                    {
                        var mrMatch = Regex.Match(message, @"!(\d+)");
                        if (mrMatch.Success)
                        {
                            prNumber = mrMatch.Groups[1].Value;
                        }
                    }

                    commits.Add(new CommitInfo
                    {
                        Hash = parts[0],
                        Date = parts[1],
                        Message = message,
                        PullRequestNumber = prNumber
                    });
                }
            }

            return commits;
        }

        public List<string>? GetChangedFiles(string? previousCommitHash, string currentCommitHash)
        {
            try
            {
                string cmd = string.IsNullOrEmpty(previousCommitHash) 
                    ? $"diff-tree --no-commit-id --name-only -r --root {currentCommitHash}"
                    : $"diff-tree --no-commit-id --name-only -r {previousCommitHash} {currentCommitHash}";
                string output = RunGitCommand(cmd);
                return output.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            }
            catch (Exception ex)
            {
                SharedUtilities.Logger.Log(SharedUtilities.LogLevel.Warning, $"Could not get changed files: {ex.Message}");
                return null;
            }
        }

        public void Checkout(string target)
        {
            RunGitCommand($"checkout {target} --quiet --force");
        }

        public void RestoreOriginalBranch()
        {
            if (_isRestored) return;
            if (!string.IsNullOrEmpty(_originalBranchOrCommit))
            {
                Console.WriteLine($"[GitManager] Restoring original branch/commit: {_originalBranchOrCommit}");
                try
                {
                    RunGitCommand($"checkout \"{_originalBranchOrCommit}\" --quiet --force");
                    _isRestored = true;
                }
                catch (Exception ex)
                {
                    SharedUtilities.Logger.Log(SharedUtilities.LogLevel.Error, $"Error restoring original branch/commit: {ex.Message}");
                    SharedUtilities.Logger.LogException(ex, "Git Restore");
                }
            }
        }

        private string RunGitCommand(string args, string? workingDirectory = null)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = workingDirectory ?? _originalRepoPath
            };

            using var process = Process.Start(startInfo);
            if (process == null) throw new Exception("Failed to start git process.");

            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0 && !args.Contains("checkout") && !args.Contains("symbolic-ref"))
            {
                string errorMsg = $"Git command failed: git {args}\nError: {error}";
                SharedUtilities.Logger.Log(SharedUtilities.LogLevel.Error, errorMsg);
                throw new Exception(errorMsg);
            }

            return output;
        }

        public void Dispose()
        {
            RestoreOriginalBranch();
        }
    }
}
