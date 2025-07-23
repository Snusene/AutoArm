// Example of how to batch convert debug logs in your files
// This is a helper script that can be run to update all debug logging calls

using System;
using System.IO;
using System.Text.RegularExpressions;

namespace AutoArm.Logging
{
    public static class DebugLogConverter
    {
        // Pattern 1: Simple Log.Message with debug check
        private static readonly Regex Pattern1 = new Regex(
            @"if\s*\(\s*AutoArmMod\.settings\?\.debugLogging\s*==\s*true\s*\)\s*\n?\s*\{?\s*\n?\s*Log\.Message\(\$""(\[AutoArm\])\s*([^:""]+):\s*([^""]+)""\);\s*\}?",
            RegexOptions.Multiline
        );

        // Pattern 2: Log.Message without pawn name
        private static readonly Regex Pattern2 = new Regex(
            @"if\s*\(\s*AutoArmMod\.settings\?\.debugLogging\s*==\s*true\s*\)\s*\n?\s*\{?\s*\n?\s*Log\.Message\(\$""(\[AutoArm\])\s*([^""]+)""\);\s*\}?",
            RegexOptions.Multiline
        );

        // Pattern 3: Inline Log.Message
        private static readonly Regex Pattern3 = new Regex(
            @"if\s*\(\s*AutoArmMod\.settings\?\.debugLogging\s*==\s*true\s*\)\s*Log\.Message\(\$""(\[AutoArm\])\s*([^""]+)""\);",
            RegexOptions.Singleline
        );

        public static string ConvertFile(string filePath)
        {
            string content = File.ReadAllText(filePath);
            string original = content;

            // Convert patterns with pawn names
            content = Pattern1.Replace(content, match =>
            {
                string pawnVar = match.Groups[2].Value.Trim();
                string message = match.Groups[3].Value.Trim();
                
                // Handle {pawn.Name} or {pawn?.Name}
                if (pawnVar.Contains("{") && pawnVar.Contains("}"))
                {
                    pawnVar = Regex.Match(pawnVar, @"\{([^}]+)\}").Groups[1].Value;
                    pawnVar = pawnVar.Replace("?.Name", "").Replace(".Name", "");
                }
                
                return $"AutoArmDebug.LogPawn({pawnVar}, \"{message}\");";
            });

            // Convert simple patterns
            content = Pattern2.Replace(content, match =>
            {
                string message = match.Groups[2].Value.Trim();
                return $"AutoArmDebug.Log(\"[AutoArm] {message}\");";
            });

            // Convert inline patterns
            content = Pattern3.Replace(content, match =>
            {
                string message = match.Groups[2].Value.Trim();
                return $"AutoArmDebug.Log(\"[AutoArm] {message}\");";
            });

            // Add using statement if needed and file was modified
            if (content != original && !content.Contains("using AutoArm.Logging;"))
            {
                content = "using AutoArm.Logging;\n" + content;
            }

            return content;
        }

        public static void ConvertAllFiles(string directory)
        {
            var files = Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories);
            int converted = 0;

            foreach (var file in files)
            {
                // Skip the logging directory itself
                if (file.Contains(@"\Logging\")) continue;

                string original = File.ReadAllText(file);
                string converted_content = ConvertFile(file);

                if (original != converted_content)
                {
                    File.WriteAllText(file, converted_content);
                    converted++;
                    Console.WriteLine($"Converted: {Path.GetFileName(file)}");
                }
            }

            Console.WriteLine($"Converted {converted} files");
        }
    }
}

/* Usage:
 * DebugLogConverter.ConvertAllFiles(@"C:\Users\mohfl\source\repos\AutoArm\AutoArm\Source");
 */
