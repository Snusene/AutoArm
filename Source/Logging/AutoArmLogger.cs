using System;
using System.Collections.Generic;
using Verse;

namespace AutoArm
{
    public enum LogCategory
    {
        General,
        WeaponScoring,
        JobCreation,
        ModCompatibility,
        Performance,
        ThinkTree,
        Cache,
        Sidearms
    }

    public enum LogLevel
    {
        Trace = 0,
        Debug = 1,
        Info = 2,
        Warning = 3,
        Error = 4
    }

    public static class AutoArmLogger
    {
        private static Dictionary<LogCategory, LogLevel> categoryLevels = new Dictionary<LogCategory, LogLevel>();

        static AutoArmLogger()
        {
            foreach (LogCategory category in Enum.GetValues(typeof(LogCategory)))
            {
                categoryLevels[category] = LogLevel.Info;
            }
        }

        public static void Log(LogCategory category, LogLevel level, string message, Pawn pawn = null)
        {
            if (!AutoArmMod.settings?.debugLogging ?? true)
                return;

            if (!categoryLevels.TryGetValue(category, out var minLevel) || level < minLevel)
                return;

            string prefix = $"[AutoArm:{category}]";
            if (pawn != null)
                prefix += $"[{pawn.Name}]";

            string fullMessage = $"{prefix} {message}";

            if (level >= LogLevel.Error)
                Verse.Log.Error(fullMessage);
            else if (level >= LogLevel.Warning)
                Verse.Log.Warning(fullMessage);
            else
                Verse.Log.Message(fullMessage);
        }

        public static void Trace(LogCategory category, string message, Pawn pawn = null)
            => Log(category, LogLevel.Trace, message, pawn);

        public static void Debug(LogCategory category, string message, Pawn pawn = null)
            => Log(category, LogLevel.Debug, message, pawn);

        public static void Info(LogCategory category, string message, Pawn pawn = null)
            => Log(category, LogLevel.Info, message, pawn);

        public static void Warning(LogCategory category, string message, Pawn pawn = null)
            => Log(category, LogLevel.Warning, message, pawn);

        public static void Error(LogCategory category, string message, Pawn pawn = null)
            => Log(category, LogLevel.Error, message, pawn);

        public static LogLevel GetCategoryLevel(LogCategory category)
        {
            return categoryLevels.TryGetValue(category, out var level) ? level : LogLevel.Info;
        }

        public static void SetCategoryLevel(LogCategory category, LogLevel level)
        {
            categoryLevels[category] = level;
        }

        public static string ExportRecentLogs()
        {
            return "Log export not implemented in this version";
        }
    }
}