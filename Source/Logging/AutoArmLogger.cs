// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: Structured logging system with categories and levels
// Provides categorized logging for different mod subsystems

using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using Verse;      // RimWorld’s logging & utility namespace

namespace AutoArm   // keep root namespace so existing call-sites resolve
{
    /// <summary>
    /// Facade around Verse.Log that routes
    ///  • Info  → in-game console / Player.log
    ///  • Warn, Debug, Error → &lt;SaveData&gt;/Logs/AutoArm.log
    ///
    /// Legacy entry-points (Log, LogPawn, LogWeapon, LogError, DebugLog)
    /// are preserved so the pre-existing code base compiles unchanged.
    /// </summary>
    internal static class AutoArmLogger
    {
        private const string LogFileName = "AutoArm.log";

        private static readonly object SyncRoot = new object();
        private static readonly StreamWriter Writer;   // initialised in static ctor

        #region static constructor
        static AutoArmLogger()
        {
            try
            {
                string folder = Path.Combine(GenFilePaths.SaveDataFolderPath, "Logs");
                if (!Directory.Exists(folder))
                    Directory.CreateDirectory(folder);

                string path = Path.Combine(folder, LogFileName);

                Writer = new StreamWriter(path, true, Encoding.UTF8) { AutoFlush = true };

                // Cosmetic session delimiter
                Writer.WriteLine("───────────────────────────────────────────────────────────────");
                Writer.WriteLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  ─ AutoArm session started");
            }
            catch (Exception ex)
            {
                // If log creation fails, at least notify the player
                Verse.Log.Error("[AutoArm] Failed to create AutoArm.log file: " + ex);
            }
        }
        #endregion

        #region Public API : player-facing
        public static void Info(string message)
        {
            Verse.Log.Message(Prefix(message));
        }

        public static void Info(string format, params object[] args)
        {
            Info(string.Format(format, args));
        }
        #endregion

        #region Public API : deep diagnostics
        public static void Warn(string message)
        {
            Write(LogLevel.Warn, message);
        }
        public static void Warn(string format, params object[] args)
        {
            Warn(string.Format(format, args));
        }

        public static void Debug(string message)
        {
            Write(LogLevel.Debug, message);
        }
        public static void Debug(string format, params object[] args)
        {
            Debug(string.Format(format, args));
        }

        public static void Error(string message, Exception ex = null)
        {
            Write(LogLevel.Error, message, ex);
        }
        public static void Error(string format, params object[] args)
        {
            Error(string.Format(format, args));
        }
        #endregion

        #region Legacy shims
        // Original ‘Log’ entry-point → Debug level
        public static void Log(string message) { Debug(message); }
        public static void Log(string format, params object[] args)
        {
            Debug(string.Format(format, args));
        }

        // ‘LogPawn’ variants → Debug level with pawn prefix
        public static void LogPawn(Pawn pawn, string message)
        {
            Debug("[" + (pawn == null ? "null" : pawn.LabelShort) + "] " + message);
        }
        public static void LogPawn(string message, Pawn pawn)
        {
            Debug("[" + (pawn == null ? "null" : pawn.LabelShort) + "] " + message);
        }

        // ‘LogWeapon’ variants → Debug level with weapon prefix
        public static void LogWeapon(Thing weapon, string message)
        {
            Debug("[Weapon:" + (weapon == null ? "null" : weapon.Label) + "] " + message);
        }
        public static void LogWeapon(string message, Thing weapon)
        {
            Debug("[Weapon:" + (weapon == null ? "null" : weapon.Label) + "] " + message);
        }

        // ‘LogError’ → Error level
        public static void LogError(string message, Exception ex = null)
        {
            Error(message, ex);
        }

        // Some files used AutoArmLoggerLogger.DebugLog (typo) – keep it alive
        public static void DebugLog(string message) { Debug(message); }
        #endregion

        #region Internals
        private enum LogLevel { Debug, Warn, Error }

        private static void Write(LogLevel level, string message, Exception ex = null)
        {
            string ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            string lvl = level.ToString().ToUpperInvariant().PadRight(5);
            string line = "[" + ts + "] [" + lvl + "] " + message;

            lock (SyncRoot)
            {
                Writer.WriteLine(line);

                if (ex != null)
                {
                    Writer.WriteLine(ex);
                    Writer.WriteLine("------------------------------------------------");
                }
            }

            // Echo Error to game log so player notices immediately
            if (level == LogLevel.Error)
            {
                Verse.Log.Error(Prefix(message) + (ex != null ? ": " + ex.Message : string.Empty));
            }
        }

        [Conditional("DEBUG")]   // stripped in Release builds
        public static void DevTrace(string message) { Debug(message); }

        private static string Prefix(string msg) { return "[AutoArm] " + msg; }
        #endregion
    }
}