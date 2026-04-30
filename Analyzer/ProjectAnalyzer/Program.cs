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
        static int Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.WriteLine("Usage: ProjectAnalyzer <game_folder> [results_dir]");
                return 1;
            }

            string gameFolder = Path.GetFullPath(args[0]);
            string gameName = Path.GetFileName(gameFolder.TrimEnd(Path.DirectorySeparatorChar));
            string rootDir = AppDomain.CurrentDomain.BaseDirectory;
            while (!string.IsNullOrEmpty(rootDir) && !Directory.Exists(Path.Combine(rootDir, "Analyzer")))
            {
                rootDir = Path.GetDirectoryName(rootDir) ?? "";
            }

            string resultsDir = args.Length > 1 
                ? Path.GetFullPath(args[1]) 
                : Path.Combine(Environment.CurrentDirectory, "Results", gameName);

            Console.WriteLine($"[ProjectAnalyzer] Starting analysis for {gameName}");
            Console.WriteLine($"[ProjectAnalyzer] Game folder: {gameFolder}");
            Console.WriteLine($"[ProjectAnalyzer] Results directory: {resultsDir}");

            Directory.CreateDirectory(resultsDir);
            string codeResults = Path.Combine(resultsDir, "Code");
            string dataResults = Path.Combine(resultsDir, "Data");
            Directory.CreateDirectory(codeResults);
            Directory.CreateDirectory(dataResults);

            try
            {
                // 1. Run Analyzers
                RunAnalyzers(gameFolder, gameName, codeResults, dataResults);

                // 2. Aggregate Results
                Console.WriteLine("[ProjectAnalyzer] Aggregating results...");
                var reportData = AggregateResults(codeResults, dataResults);

                // 3. Generate HTML Report
                Console.WriteLine("[ProjectAnalyzer] Generating HTML report...");
                GenerateHtmlReport(reportData, resultsDir);

                Console.WriteLine($"[ProjectAnalyzer] Success! Report generated at: {Path.Combine(resultsDir, "analysis_report.html")}");
                return 0;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[ProjectAnalyzer] Error: {ex.Message}");
                Console.ResetColor();
                return 1;
            }
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
            int warns = issues.Count(i => ((dynamic)i).severity == "Medium" || ((dynamic)i).severity == "Low");
            
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
                            message = $"{smellName}: Detected in {(smell.TryGetProperty("FullName", out var fn) ? fn.GetString() : "element")}"
                        });
                    }
                }
            }
            catch { }
        }

        static void GenerateHtmlReport(object data, string outputDir)
        {
            var assembly = Assembly.GetExecutingAssembly();
            string resourceName = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("report_template.html")) 
                ?? throw new Exception("Could not find embedded report template.");

            using var stream = assembly.GetManifestResourceStream(resourceName);
            using var reader = new StreamReader(stream ?? throw new Exception("Template stream is null"));
            string template = reader.ReadToEnd();

            string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            string reportContent = template.Replace("{{ DATA_JSON }}", json);

            File.WriteAllText(Path.Combine(outputDir, "analysis_report.html"), reportContent);
        }
    }
}
