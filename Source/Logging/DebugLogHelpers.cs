using System.Text.RegularExpressions;
using Verse;

namespace AutoArm
{
    // Helper class to make migration easier
    public static class DebugLogMigrationHelper
    {
        // This regex will match most debug log patterns in the code
        private static readonly Regex LogPattern = new Regex(
            @"if\s*\(\s*AutoArmMod\.settings\?\.debugLogging\s*==\s*true\s*\)\s*\n?\s*\{?\s*\n?\s*Log\.Message\s*\(\s*\$?""(\[AutoArm\]\s*[^""]+)""\s*\);?\s*\}?",
            RegexOptions.Multiline | RegexOptions.Singleline
        );

        // Example of how to update a string of code
        public static string MigrateDebugLogs(string sourceCode)
        {
            return LogPattern.Replace(sourceCode, match =>
            {
                string message = match.Groups[1].Value;
                return $"AutoArmDebugLogger.DebugLog($\"{message}\");";
            });
        }
    }

    // Simplified debug logging macros (fixes #7, #20)
    public static class AutoArmDebug
    {
        public static void Log(string message)
        {
            // Check debug setting inside the method
            if (AutoArmMod.settings?.debugLogging != true)
                return;

            AutoArmDebugLogger.DebugLog(message);
        }

        public static void LogFormat(string format, params object[] args)
        {
            // Check debug setting inside the method
            if (AutoArmMod.settings?.debugLogging != true)
                return;

            AutoArmDebugLogger.DebugLog(string.Format(format, args));
        }

        public static void LogPawn(Pawn pawn, string message)
        {
            // Check debug setting inside the method
            if (AutoArmMod.settings?.debugLogging != true)
                return;

            AutoArmDebugLogger.DebugLog($"[AutoArm] {pawn?.Name?.ToStringShort ?? "null"}: {message}");
        }

        public static void LogWeapon(Pawn pawn, ThingWithComps weapon, string message)
        {
            // Check debug setting inside the method
            if (AutoArmMod.settings?.debugLogging != true)
                return;

            AutoArmDebugLogger.DebugLog($"[AutoArm] {pawn?.Name?.ToStringShort ?? "null"}: {message} - {weapon?.Label ?? "null"}");
        }

        public static void LogError(string message, System.Exception ex = null)
        {
            // Always log errors regardless of debug setting
            string error = $"[AutoArm ERROR] {message}";
            if (ex != null)
                error += $"\nException: {ex.Message}\nStackTrace: {ex.StackTrace}";

            AutoArmDebugLogger.DebugLog(error, forceFlush: true);
        }
    }
}