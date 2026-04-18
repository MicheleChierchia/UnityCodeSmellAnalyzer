using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AnalyzerUtilities
{
    public static class FileExplorer
    {
        private static readonly string[] IgnoreDirectories = { "Library", "Temp", "obj", "Logs", "Builds", "UserSettings", ".git", ".vs" };

        /// <summary>
        /// Traverses directories avoiding common massive Unity folders that do not contain source files,
        /// and returns all files matching any of the specified extensions.
        /// </summary>
        /// <param name="rootPath">The directory to start searching from.</param>
        /// <param name="extensions">An array of extensions to look for (e.g. new string[] { ".cs", ".unity" }).</param>
        /// <param name="includeLibrary">If true, it will not ignore the Library folder. Useful for finding compiled DLLs.</param>
        /// <returns>A list of absolute file paths matching the extensions.</returns>
        public static List<string> GetFilesByExtensions(string rootPath, string[] extensions, bool includeLibrary = false)
        {
            var files = new List<string>();
            var stack = new Stack<string>();
            
            if (!Directory.Exists(rootPath))
            {
                return files;
            }

            stack.Push(rootPath);

            while (stack.Count > 0)
            {
                var currentDir = stack.Pop();
                
                try
                {
                    // Add directories to stack
                    foreach (var dir in Directory.EnumerateDirectories(currentDir))
                    {
                        var dirName = Path.GetFileName(dir);
                        if (IgnoreDirectories.Contains(dirName, StringComparer.OrdinalIgnoreCase))
                        {
                            if (!(includeLibrary && dirName.Equals("Library", StringComparison.OrdinalIgnoreCase)))
                            {
                                continue;
                            }
                        }
                        stack.Push(dir);
                    }

                    // Add matching files
                    foreach (var file in Directory.EnumerateFiles(currentDir))
                    {
                        if (extensions.Any(ext => file.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                        {
                            files.Add(file);
                        }
                    }
                }
                catch (UnauthorizedAccessException) { }
                catch (DirectoryNotFoundException) { }
                catch (PathTooLongException) { }
            }

            return files;
        }
    }
}
