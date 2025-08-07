// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: Structured logging system with categories and levels
// Provides categorized logging for different mod subsystems

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Verse;

namespace AutoArm.Logging   // keep root namespace so existing call-sites resolve
{
    /// <summary>
    /// Facade around Verse.Log that routes
    ///  • Info → in-game console / Player.log
    ///  • Warn, Debug, Error → RimWorld folder/AutoArm.log
    ///
    /// Legacy entry-points (Log, LogPawn, LogWeapon, LogError, DebugLog)
    /// are preserved so the pre-existing code base compiles unchanged.
    /// </summary>
    internal static class AutoArmLogger
    {
        private const string LogFileName = "AutoArm.log";
        private const int DEDUP_COOLDOWN_MINUTES = 5; // 5 minutes cooldown for duplicate messages
        private const int CLEANUP_INTERVAL_MINUTES = 10;

        // Message deduplication
        private static readonly Dictionary<string, DateTime> recentMessages = new Dictionary<string, DateTime>();

        private static DateTime lastCleanup = DateTime.Now;

        // Store the AutoArm.log path for verbose logging announcements
        private static string autoArmLogPath = null;

        public static string AutoArmLogPath
        {
            get
            {
                if (autoArmLogPath == null)
                {
                    // CHANGED: Write directly to RimWorld folder, not Logs subfolder
                    autoArmLogPath = Path.Combine(GenFilePaths.SaveDataFolderPath, LogFileName);
                }
                return autoArmLogPath;
            }
        }

        private static readonly object SyncRoot = new object();
        private static readonly StreamWriter Writer;      // initialised in static ctor

        #region static constructor

        static AutoArmLogger()
        {
            try
            {
                // CHANGED: Write directly to RimWorld folder
                string path = Path.Combine(GenFilePaths.SaveDataFolderPath, LogFileName);

                // Create or overwrite the log file on each game start
                Writer = new StreamWriter(path, false, Encoding.UTF8) { AutoFlush = true };

                // Write header for new log file
                Writer.WriteLine("===============================================================");
                Writer.WriteLine($"AutoArm Log - Session started: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                Writer.WriteLine("===============================================================");
                Writer.WriteLine();

                // Log the path for debugging
                Writer.WriteLine($"Log file location: {path}");
                Writer.WriteLine();
            }
            catch (Exception ex)
            {
                // If log creation fails, at least notify the player
                Verse.Log.Error("[AutoArm] Failed to create AutoArm.log file: " + ex);
            }
        }

        #endregion static constructor

        #region Verbose Logging Announcement

        /// <summary>
        /// Announces that verbose logging has been enabled and shows where to find logs
        /// </summary>
        public static void AnnounceVerboseLogging()
        {
            string message = "[AutoArm] Verbose logging enabled!\n" +
                           $"Debug logs are written to: {AutoArmLogPath}";

            // Log to both in-game console and our debug log
            Verse.Log.Message(message);
            Debug("Verbose logging enabled - player notified of log location");
        }

        #endregion Verbose Logging Announcement

        #region Public API : player-facing

        public static void Info(string message)
        { Verse.Log.Message(Prefix(message)); }

        public static void Info(string format, params object[] args)
        { Info(string.Format(format, args)); }

        #endregion Public API : player-facing

        #region Public API : deep diagnostics

        public static void Warn(string message)
        { Write(LogLevel.Warn, message); }

        public static void Warn(string format, params object[] args)
        { Warn(string.Format(format, args)); }

        public static void Debug(string message)
        {
            // Only write debug messages if debug logging is enabled
            try
            {
                // Skip debug messages if settings aren't available yet
                if (AutoArmMod.settings == null)
                    return;

                if (AutoArmMod.settings.debugLogging == true)
                {
                    Write(LogLevel.Debug, message);
                }
            }
            catch
            {
                // If there's any error accessing settings, skip debug logging
                return;
            }
        }

        public static void Debug(string format, params object[] args)
        { Debug(string.Format(format, args)); }

        public static void Error(string message, Exception ex = null)
        { Write(LogLevel.Error, message, ex); }

        public static void Error(string format, params object[] args)
        { Error(string.Format(format, args)); }

        #endregion Public API : deep diagnostics

        #region Legacy shims

        // Original "Log" → Debug level
        public static void Log(string message)
        { Debug(message); }

        public static void Log(string format, params object[] args)
        { Debug(string.Format(format, args)); }

        // "LogPawn" variants
        public static void LogPawn(Pawn pawn, string message)
        {
            Debug($"[{pawn?.LabelShort ?? "null"}] {message}");
        }

        public static void LogPawn(string message, Pawn pawn)
        {
            Debug($"[{pawn?.LabelShort ?? "null"}] {message}");
        }

        // "LogWeapon" – 2-parameter variants
        public static void LogWeapon(Thing weapon, string message)
        {
            Debug($"[Weapon:{Label(weapon)}] {message}");
        }

        public static void LogWeapon(string message, Thing weapon)
        {
            Debug($"[Weapon:{Label(weapon)}] {message}");
        }

        // "LogWeapon" – format + args
        public static void LogWeapon(Thing weapon, string format, params object[] args)
        {
            LogWeapon(weapon, string.Format(format, args));
        }

        public static void LogWeapon(string format, params object[] args)
        {
            // Try to find a Thing in args for nicer prefix; if none, weapon == null
            Thing weapon = null;
            for (int i = 0; i < args.Length; i++)
            {
                weapon = args[i] as Thing;
                if (weapon != null) break;
            }
            LogWeapon(weapon, string.Format(format, args));
        }

        // "LogWeapon" – 3-parameter variant (weapon1, weapon2, message)
        public static void LogWeapon(Thing weapon1, Thing weapon2, string message)
        {
            Debug($"[Weapon:{Label(weapon1)} | {Label(weapon2)}] {message}");
        }

        // "LogWeapon" – 4+ parameters (weapon1, weapon2, fmt, args)
        public static void LogWeapon(Thing weapon1, Thing weapon2, string format, params object[] args)
        {
            LogWeapon(weapon1, weapon2, string.Format(format, args));
        }

        // "LogError" → Error level
        public static void LogError(string message, Exception ex = null)
        { Error(message, ex); }

        // Some files used AutoArmLoggerLogger.DebugLog (typo) – keep it alive
        public static void DebugLog(string message)
        { Debug(message); }

        // Convenience method for logging pawn + weapon combos
        public static void LogPawnWeapon(Pawn pawn, Thing weapon, string message)
        {
            Debug($"[{pawn?.LabelShort ?? "null"}] [Weapon:{Label(weapon)}] {message}");
        }

        #endregion Legacy shims

        #region Internals

        private enum LogLevel
        { Debug, Warn, Error }

        private static void Write(LogLevel level, string message, Exception ex = null)
        {
            // Check for duplicate messages
            lock (SyncRoot)
            {
                // Periodic cleanup of old entries
                if (DateTime.Now.Subtract(lastCleanup).TotalMinutes > CLEANUP_INTERVAL_MINUTES)
                {
                    CleanupOldMessages();
                    lastCleanup = DateTime.Now;
                }

                // Check if this message was recently logged
                if (recentMessages.TryGetValue(message, out DateTime lastLogged))
                {
                    if (DateTime.Now.Subtract(lastLogged).TotalMinutes < DEDUP_COOLDOWN_MINUTES)
                    {
                        // Skip duplicate message
                        return;
                    }
                }

                // Record this message
                recentMessages[message] = DateTime.Now;
            }

            string ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            string lvl = level.ToString().ToUpperInvariant().PadRight(5);
            string line = $"[{ts}] [{lvl}] {message}";

            lock (SyncRoot)
            {
                Writer.WriteLine(line);

                if (ex != null)
                {
                    Writer.WriteLine($"Exception: {ex.GetType().Name}: {ex.Message}");
                    Writer.WriteLine($"Stack trace:\n{ex.StackTrace}");
                    Writer.WriteLine("------------------------------------------------");
                }
            }

            // Echo Error to game log so player notices immediately
            if (level == LogLevel.Error)
            {
                Verse.Log.Error(Prefix(message) + (ex != null ? $": {ex.Message}" : string.Empty));
            }
        }

        private static void CleanupOldMessages()
        {
            var cutoffTime = DateTime.Now.AddMinutes(-DEDUP_COOLDOWN_MINUTES * 2); // Keep messages for twice the cooldown period
            var keysToRemove = recentMessages
                .Where(kvp => kvp.Value < cutoffTime)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in keysToRemove)
            {
                recentMessages.Remove(key);
            }
        }

        [Conditional("DEBUG")]   // stripped in Release builds
        public static void DevTrace(string message)
        { Debug(message); }

        private static string Prefix(string msg)
        { return "[AutoArm] " + msg; }

        private static string Label(Thing t)
        { return t?.Label ?? "null"; }

        #endregion Internals
    }
}