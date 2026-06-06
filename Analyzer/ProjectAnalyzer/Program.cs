using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Collections.Generic;

namespace ProjectAnalyzer
{
    class Program
    {
        public class HistoryResult
        {
            public CommitInfo Commit { get; set; } = new();
            public object Results { get; set; } = new();
        }

        private static GitHistoryManager? _activeGitManager;

        private static void HandleExit()
        {
            if (_activeGitManager != null)
            {
                _activeGitManager.RestoreOriginalBranch();
            }
        }

        static int Main(string[] args)
        {
            AppDomain.CurrentDomain.ProcessExit += (s, e) => HandleExit();
            Console.CancelKeyPress += (s, e) => {
                e.Cancel = true;
                HandleExit();
                Environment.Exit(130);
            };
            var argList = args.ToList();
            argList.Remove("--history"); // Ignore if provided for backwards compatibility
            
            int limit = 0;
            int limitIndex = argList.IndexOf("--limit");
            if (limitIndex >= 0 && limitIndex + 1 < argList.Count)
            {
                int.TryParse(argList[limitIndex + 1], out limit);
                argList.RemoveAt(limitIndex); // remove "--limit"
                argList.RemoveAt(limitIndex); // remove value
            }

            if (argList.Count < 1)
            {
                Console.WriteLine("Usage: ProjectAnalyzer <game_folder> [results_dir] [--limit N]");
                return 1;
            }

            string gameFolder = Path.GetFullPath(argList[0]);
            string gameName = Path.GetFileName(gameFolder.TrimEnd(Path.DirectorySeparatorChar));
            string resultsDir = argList.Count > 1
                ? Path.GetFullPath(argList[1])
                : Path.Combine(Environment.CurrentDirectory, "Results", gameName);

            return RunHistoryAnalysis(gameFolder, gameName, resultsDir, limit);
        }



        static int RunHistoryAnalysis(string gameFolder, string gameName, string resultsDir, int limit)
        {
            Console.WriteLine($"[ProjectAnalyzer] Starting HISTORY analysis for {gameName}");
            using var gitManager = new GitHistoryManager(gameFolder);
            _activeGitManager = gitManager;

            if (!gitManager.IsGitRepo())
            {
                Console.WriteLine("[ProjectAnalyzer] Error: game_folder is not a git repository.");
                return 1;
            }

            gitManager.InitializeClone();

            var commits = gitManager.GetCommitHistory(limit);
            Console.WriteLine($"[ProjectAnalyzer] Found {commits.Count} commits to analyze.");

            var historyResults = new List<HistoryResult>();
            var csvLines = new List<string> { "CommitHash,Date,PullRequest,LOC,Files,Errors,Warnings" };
            
            Directory.CreateDirectory(resultsDir);

            int count = 0;
            try
            {
                foreach (var commit in commits)
                {
                    count++;
                    Console.WriteLine($"\n[ProjectAnalyzer] [{count}/{commits.Count}] Analyzing commit {commit.Hash.Substring(0, 8)} ({commit.Date})");

                    gitManager.Checkout(commit.Hash);

                    // Restore dependencies if target project has package references
                    RestoreDotnetDependencies(gitManager.TempGameFolder);

                    string commitResultsDir = Path.Combine(resultsDir, "Commits", commit.Hash);
                    Directory.CreateDirectory(commitResultsDir);
                    string codeResults = Path.Combine(commitResultsDir, "Code");
                    string dataResults = Path.Combine(commitResultsDir, "Data");
                    Directory.CreateDirectory(codeResults);
                    Directory.CreateDirectory(dataResults);

                    try
                    {
                        RunAnalyzers(gitManager.TempGameFolder, gameName, codeResults, dataResults);
                        var reportData = AggregateResults(codeResults, dataResults);
                        historyResults.Add(new HistoryResult { Commit = commit, Results = reportData });
                        
                        dynamic summary = ((dynamic)reportData).summary;
                        csvLines.Add($"{commit.Hash},{commit.Date},{commit.PullRequestNumber ?? ""},{summary.loc},{summary.files},{summary.errors},{summary.warnings}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[ProjectAnalyzer] Warning: Failed to analyze commit {commit.Hash}: {ex.Message}");
                    }
                }
            }
            finally
            {
                // Ensure we checkout the original branch/commit when leaving the loop
                gitManager.RestoreOriginalBranch();
                _activeGitManager = null;
            }

            // Save aggregate history
            string historyFile = Path.Combine(resultsDir, "history_results.json");
            string json = JsonSerializer.Serialize(historyResults, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(historyFile, json);

            string csvFile = Path.Combine(resultsDir, "history_trend.csv");
            File.WriteAllLines(csvFile, csvLines);

            // Generate HTML History Report
            Console.WriteLine("[ProjectAnalyzer] Generating HTML history report...");
            try
            {
                GenerateHistoryHtmlReport(historyResults, resultsDir);
                Console.WriteLine($"[ProjectAnalyzer] Success! History report generated at: {Path.Combine(resultsDir, "history_report.html")}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ProjectAnalyzer] Warning: Failed to generate HTML history report: {ex.Message}");
            }

            Console.WriteLine($"\n[ProjectAnalyzer] History analysis complete.");
            Console.WriteLine($"[ProjectAnalyzer] JSON Results: {historyFile}");
            Console.WriteLine($"[ProjectAnalyzer] CSV Trend: {csvFile}");
            return 0;
        }

        static void RunAnalyzers(string gameFolder, string gameName, string codeResults, string dataResults)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            
            string csharpAnalyzer = FindExecutable("CSharpAnalyzer");
            string codeSmellAnalyzer = FindExecutable("CodeSmellAnalyzer");
            string unityDataAnalyzer = FindExecutable("UnityDataAnalyzer");
            string metaSmellAnalyzer = FindExecutable("MetaSmellAnalyzer");

            ExecuteProcess(csharpAnalyzer, $"-n \"{gameName}\" -p \"{gameFolder}\" -r \"{codeResults}\" -v");
            ExecuteProcess(codeSmellAnalyzer, $"-d \"{Path.Combine(codeResults, "CodeAnalysis.json")}\" -r \"{codeResults}\" -c -v");
            ExecuteProcess(unityDataAnalyzer, $"-n \"{gameName}\" -d \"{gameFolder}\" -r \"{dataResults}\" -v");
            
            string smellFile = Path.Combine(Path.GetDirectoryName(metaSmellAnalyzer) ?? "", "smell.txt");
            ExecuteProcess(metaSmellAnalyzer, $"-d \"{Path.Combine(dataResults, "mainResults")}\" \"{Path.Combine(dataResults, "metaResults")}\" -r \"{dataResults}\" -c -v -f \"{smellFile}\"");
        }

        static string FindExecutable(string name)
        {
            string rootDir = AppDomain.CurrentDomain.BaseDirectory;
            while (!string.IsNullOrEmpty(rootDir) && !Directory.Exists(Path.Combine(rootDir, "Analyzer")))
            {
                rootDir = Path.GetDirectoryName(rootDir) ?? "";
            }
            
            if (string.IsNullOrEmpty(rootDir)) throw new Exception("Could not find project root directory.");
            
            string[] relativePaths = {
                $"Analyzer/{name}/bin/Debug/net8.0/linux-x64/{name}",
                $"Analyzer/{name}/bin/Debug/net8.0/{name}",
                $"Analyzer/{name}/bin/Release/net8.0/linux-x64/{name}",
                $"Analyzer/{name}/bin/Release/net8.0/{name}"
            };

            foreach (var relPath in relativePaths)
            {
                string fullPath = Path.Combine(rootDir, relPath);
                if (File.Exists(fullPath)) return fullPath;
                if (File.Exists(fullPath + ".exe")) return fullPath + ".exe";
            }

            throw new FileNotFoundException($"Could not find executable for {name}");
        }

        static void ExecuteProcess(string fileName, string arguments)
        {
            Console.WriteLine($"[ProjectAnalyzer] Running: {Path.GetFileName(fileName)} {arguments}");
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(fileName) ?? ""
            };

            using var process = Process.Start(startInfo);
            if (process == null) throw new Exception($"Failed to start process: {fileName}");
            
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                string error = process.StandardError.ReadToEnd();
                Console.WriteLine($"[ProjectAnalyzer] Warning: {Path.GetFileName(fileName)} exited with code {process.ExitCode}. {error}");
            }
        }

        static object AggregateResults(string codeResults, string dataResults)
        {
            var summary = new { files = 0, loc = 0, errors = 0, warnings = 0 };
            var issues = new List<object>();

            string codeAnalysisPath = Path.Combine(codeResults, "CodeAnalysis.json");
            if (File.Exists(codeAnalysisPath))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(codeAnalysisPath));
                int loc = doc.RootElement.GetProperty("LOC").GetInt32();
                int fileCount = doc.RootElement.GetProperty("Project").GetArrayLength();
                summary = new { files = fileCount, loc = loc, errors = 0, warnings = 0 };
            }

            string codeSmellsDir = Path.Combine(codeResults, "CodeSmellResults");
            if (Directory.Exists(codeSmellsDir))
            {
                foreach (var file in Directory.GetFiles(codeSmellsDir, "*.json"))
                {
                    ParseSmellFile(file, issues);
                }
            }

            string metaSmellsDir = Path.Combine(dataResults, "MetaSmellResults");
            if (Directory.Exists(metaSmellsDir))
            {
                foreach (var file in Directory.GetFiles(metaSmellsDir, "*.json"))
                {
                    ParseSmellFile(file, issues);
                }
            }

            int errs = issues.Count(i => ((dynamic)i).severity == "High");
            int warns = issues.Count(i => ((dynamic)i).severity != "High");
            
            return new { 
                summary = new { summary.files, summary.loc, errors = errs, warnings = warns },
                issues = issues 
            };
        }

        static void ParseSmellFile(string filePath, List<object> issues)
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(filePath));
                var root = doc.RootElement;
                string smellName = root.GetProperty("Name").GetString() ?? "Unknown Smell";
                string severity = root.TryGetProperty("Severity", out var sev) ? sev.GetString() ?? "Medium" : "Medium";

                if (root.TryGetProperty("Smells", out var smellsArray))
                {
                    foreach (var smell in smellsArray.EnumerateArray())
                    {
                        issues.Add(new {
                            file = smell.TryGetProperty("Script", out var script) ? script.GetString() : (smell.TryGetProperty("File", out var f) ? f.GetString() : "Unknown"),
                            line = smell.TryGetProperty("Line", out var line) ? line.GetInt32() : 0,
                            severity = severity,
                            smell = smellName,
                            message = $"{smellName}: Detected in {(smell.TryGetProperty("FullName", out var fn) ? fn.GetString() : "element")}"
                        });
                    }
                }
            }
            catch { }
        }



        static void GenerateHistoryHtmlReport(object data, string outputDir)
        {
            var assembly = Assembly.GetExecutingAssembly();
            string resourceName = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("history_report_template.html")) 
                ?? throw new Exception("Could not find embedded history report template.");

            using var stream = assembly.GetManifestResourceStream(resourceName);
            using var reader = new StreamReader(stream ?? throw new Exception("Template stream is null"));
            string template = reader.ReadToEnd();

            string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            string reportContent = template.Replace("{{ DATA_JSON }}", json);

            File.WriteAllText(Path.Combine(outputDir, "history_report.html"), reportContent);
        }

        static void RestoreDotnetDependencies(string folder)
        {
            try
            {
                if (Directory.GetFiles(folder, "*.csproj", SearchOption.AllDirectories).Any() ||
                    Directory.GetFiles(folder, "*.sln", SearchOption.AllDirectories).Any())
                {
                    Console.WriteLine("[ProjectAnalyzer] Dotnet project detected. Restoring dependencies...");
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = "dotnet",
                        Arguments = "restore",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WorkingDirectory = folder
                    };
                    using var process = Process.Start(startInfo);
                    if (process != null)
                    {
                        process.WaitForExit();
                        if (process.ExitCode != 0)
                        {
                            Console.WriteLine($"[ProjectAnalyzer] Warning: dotnet restore exited with code {process.ExitCode}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ProjectAnalyzer] Warning: Failed to restore dependencies: {ex.Message}");
            }
        }
    }
}
