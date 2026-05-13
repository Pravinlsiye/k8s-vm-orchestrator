using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace VMJobAPI.Tests
{
    public static class ScriptComparisonHelper
    {
        /// <summary>
        /// Normalizes a PowerShell script for comparison by removing timestamps and normalizing whitespace
        /// </summary>
        public static string NormalizeScript(string script)
        {
            if (string.IsNullOrEmpty(script))
                return string.Empty;

            // Replace timestamp placeholders
            script = Regex.Replace(script, @"# Generated: .* UTC", "# Generated: {TIMESTAMP} UTC");
            
            // Normalize line endings
            script = script.Replace("\r\n", "\n");
            
            // Trim trailing whitespace from each line
            var lines = script.Split('\n');
            var normalizedLines = lines.Select(line => line.TrimEnd());
            
            // Remove empty lines at the end
            var linesList = normalizedLines.ToList();
            while (linesList.Count > 0 && string.IsNullOrWhiteSpace(linesList.Last()))
            {
                linesList.RemoveAt(linesList.Count - 1);
            }
            
            return string.Join("\n", linesList);
        }

        /// <summary>
        /// Loads and normalizes an expected script from a file
        /// </summary>
        public static string LoadExpectedScript(string fileName)
        {
            var testDirectory = AppContext.BaseDirectory;
            var scriptPath = Path.Combine(testDirectory, "TestScripts", fileName);
            
            if (!File.Exists(scriptPath))
            {
                // Try relative to project directory
                scriptPath = Path.Combine("..", "..", "..", "TestScripts", fileName);
            }
            
            if (!File.Exists(scriptPath))
            {
                throw new FileNotFoundException($"Expected script file not found: {fileName}");
            }
            
            var content = File.ReadAllText(scriptPath);
            return NormalizeScript(content);
        }

        /// <summary>
        /// Compares two scripts and returns detailed differences if they don't match
        /// </summary>
        public static (bool IsEqual, string Differences) CompareScripts(string actual, string expected)
        {
            var normalizedActual = NormalizeScript(actual);
            var normalizedExpected = NormalizeScript(expected);
            
            if (normalizedActual == normalizedExpected)
                return (true, string.Empty);
            
            // Generate line-by-line differences
            var actualLines = normalizedActual.Split('\n');
            var expectedLines = normalizedExpected.Split('\n');
            var differences = new System.Text.StringBuilder();
            
            differences.AppendLine("Script differences found:");
            differences.AppendLine($"Actual lines: {actualLines.Length}, Expected lines: {expectedLines.Length}");
            differences.AppendLine();
            
            var maxLines = Math.Max(actualLines.Length, expectedLines.Length);
            for (int i = 0; i < maxLines; i++)
            {
                var actualLine = i < actualLines.Length ? actualLines[i] : "[MISSING]";
                var expectedLine = i < expectedLines.Length ? expectedLines[i] : "[MISSING]";
                
                if (actualLine != expectedLine)
                {
                    differences.AppendLine($"Line {i + 1}:");
                    differences.AppendLine($"  Expected: {expectedLine}");
                    differences.AppendLine($"  Actual:   {actualLine}");
                    differences.AppendLine();
                }
            }
            
            return (false, differences.ToString());
        }
    }
}
