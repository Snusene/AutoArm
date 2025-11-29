
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using Verse;

namespace AutoArm.Logging
{

    internal static class AutoArmLogger
    {
        private const string LogFileName = "AutoArm.log";
        private const int MaxDedupEntries = 4096;
        private const int DedupTickWindow = 300;

        private static readonly Dictionary<string, int> recentMessages = new Dictionary<string, int>(MaxDedupEntries);

        private static readonly Queue<string> dedupOrder = new Queue<string>(MaxDedupEntries);

        private struct SuppressionInfo
        {
            public int Count;
            public int FirstBucket;
            public int LastBucket;
        }

        private static readonly Dictionary<string, SuppressionInfo> suppressed = new Dictionary<string, SuppressionInfo>(MaxDedupEntries);
        private static readonly Queue<string> suppressedOrder = new Queue<string>(MaxDedupEntries);

        private static readonly Regex CoordPattern2 = new Regex(@"\(\d+,\s*\d+\)", RegexOptions.Compiled);

        private static readonly Regex CoordPattern3 = new Regex(@"\(\d+,\s*\d+,\s*\d+\)", RegexOptions.Compiled);

        private static readonly Regex IdPattern = new Regex(@"thingIDNumber=\d+", RegexOptions.Compiled);
        private static readonly Regex PawnIdPattern = new Regex(@"\b\d{5,}\b", RegexOptions.Compiled);

        private static string autoArmLogPath = null;

        public static string AutoArmLogPath
        {
            get
            {
                if (autoArmLogPath == null)
                {
                    autoArmLogPath = Path.Combine(GenFilePaths.SaveDataFolderPath, LogFileName);
                }
                return autoArmLogPath;
            }
        }

        private static StreamWriter Writer = null;

        private static bool quietTestMode = false;

        public static void SetQuietTestMode(bool quiet)
        {
            quietTestMode = quiet;
        }

        #region static constructor

        static AutoArmLogger()
        {
            InitializeLogFile();
        }

        private static void InitializeLogFile()
        {
            try
            {
                string path = Path.Combine(GenFilePaths.SaveDataFolderPath, LogFileName);


                if (File.Exists(path))
                {
                    try
                    {
                        File.Delete(path);
                    }
                    catch
                    {
                        try
                        {
                            var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                            Writer = new StreamWriter(fs, Encoding.UTF8) { AutoFlush = false };
                            Writer.WriteLine();
                            Writer.WriteLine("===============================================================");
                            Writer.WriteLine($"AutoArm Log - Session resumed: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                            Writer.WriteLine("===============================================================");
                            Writer.WriteLine();
                            return;
                        }
                        catch
                        {
                            path = Path.Combine(GenFilePaths.SaveDataFolderPath, $"AutoArm_{DateTime.Now:yyyyMMdd_HHmmss}.log");
                        }
                    }
                }

                var fileStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
                Writer = new StreamWriter(fileStream, Encoding.UTF8) { AutoFlush = false };

                Writer.WriteLine("===============================================================");
                Writer.WriteLine($"Auto Arm Log - Session started: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                Writer.WriteLine("===============================================================");
                Writer.WriteLine();

                autoArmLogPath = path;
            }
            catch
            {
                Writer = null;
                Verse.Log.Warning("[AutoArm] Could not create log file (file may be in use). Debug logging will be disabled for this session.");
            }
        }

        #endregion static constructor

        #region Verbose Logging Announcement

        /// <summary>
        /// Verbose logging enabled
        /// </summary>
        public static void AnnounceVerboseLogging()
        {
            if (Writer != null)
            {
                string message = $"Debug logs are written to: {AutoArmLogPath}";

                Verse.Log.Message(message);
                Debug("Verbose logging enabled - player notified of log location");
            }
            else
            {
                Verse.Log.Message("[AutoArm] Debug mode enabled (file logging unavailable this session)");
            }
        }

        #endregion Verbose Logging Announcement

        #region Public Methods - Flush and Shutdown

        /// <summary>
        /// Reinit logger
        /// </summary>
        public static void ReinitializeIfNeeded()
        {
            if (Writer == null)
            {
                InitializeLogFile();
                if (Writer != null && AutoArmMod.settings?.debugLogging == true)
                {
                    Debug("Logger reinitialized after shutdown/reload");
                }
            }
        }

        /// <summary>
        /// Flushes buffered log entries to disk
        /// </summary>
        public static void Flush()
        {
            try
            {
                Writer?.Flush();
            }
            catch { /* Ignore flush errors */ }
        }

        /// <summary>
        /// Shuts down the logger, flushing and closing the file
        /// </summary>
        public static void Shutdown()
        {
            try
            {
                Writer?.Flush();
                Writer?.Dispose();
            }
            catch { /* Ignore shutdown errors */ }
            finally
            {
                Writer = null;
            }
        }

        #endregion Public Methods - Flush and Shutdown

        #region Public API : player-facing

        public static void Info(string message)
        { Verse.Log.Message(Prefix(message)); }

        public static void Info(string format, params object[] args)
        { Info(string.Format(format, args)); }

        #endregion Public API : player-facing

        #region Public API : deep diagnostics

        public static void Warn(string message, [CallerMemberName] string member = null, [CallerFilePath] string file = null, [CallerLineNumber] int line = 0)
        { Write(LogLevel.Warn, message, null, member, file, line); }

        public static void Warn(string format, params object[] args)
        { Warn(string.Format(format, args)); }

        public static void Debug(string message, [CallerMemberName] string member = null, [CallerFilePath] string file = null, [CallerLineNumber] int line = 0)
        {

            try
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    Write(LogLevel.Debug, message, null, member, file, line);
                }
            }
            catch
            {
                return;
            }
        }

        public static void Debug(string format, params object[] args)
        { Debug(string.Format(format, args)); }

        /// <summary>
        /// Lazy debug logger - only evaluates message factory if debug logging is enabled.
        /// Avoid string alloc
        /// </summary>
        public static void Debug(Func<string> messageFactory, [CallerMemberName] string member = null, [CallerFilePath] string file = null, [CallerLineNumber] int line = 0)
        {
            try
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    string message = messageFactory();
                    Write(LogLevel.Debug, message, null, member, file, line);
                }
            }
            catch
            {
                return;
            }
        }

        public static void Error(string message, Exception ex = null, [CallerMemberName] string member = null, [CallerFilePath] string file = null, [CallerLineNumber] int line = 0)
        { Write(LogLevel.Error, message, ex, member, file, line); }

        public static void Error(string format, params object[] args)
        { Error(string.Format(format, args)); }

        #region Contextual Exception Handling

        /// <summary>
        /// Logs an exception that occurred during a weapon operation with full context
        /// </summary>
        /// <param name="ex">The exception that occurred</param>
        /// <param name="pawn">The pawn involved in the operation</param>
        /// <param name="weapon">The weapon involved in the operation</param>
        /// <param name="operation">The operation being performed (e.g., "WeaponScoring", "WeaponValidation")</param>
        public static void ErrorWeaponOperation(Exception ex, Pawn pawn, ThingWithComps weapon, string operation)
        {
            string context = BuildWeaponOperationContext(pawn, weapon);
            Error($"[{operation}] Error in weapon operation: {context}", ex);
        }

        /// <summary>
        /// Logs an exception that occurred in a Harmony patch
        /// </summary>
        /// <param name="ex">The exception that occurred</param>
        /// <param name="patchName">The name of the patch (e.g., "WeaponSpawn", "PawnDestroy")</param>
        /// <param name="instance">The instance being patched (optional)</param>
        public static void ErrorPatch(Exception ex, string patchName, object instance = null)
        {
            string context = instance != null ? $"Instance: {instance.GetType().Name}" : "Static";
            Error($"[Patch:{patchName}] Error in Harmony patch: {context}", ex);
        }

        /// <summary>
        /// Logs an exception that occurred during a cleanup operation
        /// </summary>
        /// <param name="ex">The exception that occurred</param>
        /// <param name="cleanupType">The type of cleanup (e.g., "DroppedItems", "WeaponCache", "ForcedWeapons")</param>
        public static void ErrorCleanup(Exception ex, string cleanupType)
        {
            Error($"[Cleanup:{cleanupType}] Error during cleanup", ex);
        }

        /// <summary>
        /// Logs an exception that occurred in a UI component
        /// </summary>
        /// <param name="ex">The exception that occurred</param>
        /// <param name="uiComponent">The UI component (e.g., "WeaponTab", "SettingsUI")</param>
        /// <param name="uiOperation">The UI operation being performed (e.g., "FilterDrawing", "ButtonClick")</param>
        public static void ErrorUI(Exception ex, string uiComponent, string uiOperation)
        {
            Error($"[UI:{uiComponent}] Error in {uiOperation}", ex);
        }


        private static string BuildWeaponOperationContext(Pawn pawn, ThingWithComps weapon)
        {
            string pawnInfo = pawn != null
                ? $"Pawn={GetPawnName(pawn)}(ID:{pawn.thingIDNumber})"
                : "Pawn=null";

            string weaponInfo = weapon != null
                ? $"Weapon={weapon.Label}(Def:{weapon.def.defName})"
                : "Weapon=null";

            return $"{pawnInfo}, {weaponInfo}";
        }

        #endregion Contextual Exception Handling

        #endregion Public API : deep diagnostics

        #region Legacy shims

        public static void Log(string message)
        { Debug(message); }

        public static void Log(string format, params object[] args)
        { Debug(string.Format(format, args)); }

        public static string GetPawnName(Pawn pawn)
        {
            if (pawn == null) return "null";
            return pawn.Name?.ToStringShort ?? pawn.LabelShort;
        }

        public static string GetWeaponLabel(Thing weapon)
        {
            if (weapon == null) return "null";
            try { return weapon.LabelCap; } catch { return weapon.Label; }
        }

        #region Humanized Logging Helpers

        public static string GetWeaponLabelLower(ThingWithComps weapon)
        {
            if (weapon == null) return "null";

            try
            {
                string label = weapon.Label ?? weapon.def?.label ?? weapon.def?.defName ?? "unknown";
                return label.ToLowerInvariant();
            }
            catch
            {
                return weapon.def?.defName?.ToLowerInvariant() ?? "unknown";
            }
        }

        public static string GetDefLabel(ThingDef def)
        {
            if (def == null) return "null";

            try
            {
                return (def.label ?? def.defName ?? "unknown").ToLowerInvariant();
            }
            catch
            {
                return "unknown";
            }
        }

        public static string FormatBool(bool value)
        {
            return value ? "true" : "false";
        }

        public static string GetPawnPossessive(Pawn pawn)
        {
            if (pawn == null) return "null's";

            string name = GetPawnName(pawn);

            return name.EndsWith("s") ? $"{name}'" : $"{name}'s";
        }

        #endregion Humanized Logging Helpers

        public static void LogPawn(Pawn pawn, string message)
        {
            Debug($"[{GetPawnName(pawn)}] {message}");
        }

        public static void LogPawn(string message, Pawn pawn)
        {
            Debug($"[{GetPawnName(pawn)}] {message}");
        }

        public static void LogWeapon(Thing weapon, string message)
        {
            Debug($"[Weapon:{Label(weapon)}] {message}");
        }

        public static void LogWeapon(string message, Thing weapon)
        {
            Debug($"[Weapon:{Label(weapon)}] {message}");
        }

        public static void LogWeapon(Thing weapon, string format, params object[] args)
        {
            LogWeapon(weapon, string.Format(format, args));
        }

        public static void LogWeapon(string format, params object[] args)
        {
            Thing weapon = null;
            for (int i = 0; i < args.Length; i++)
            {
                weapon = args[i] as Thing;
                if (weapon != null) break;
            }
            LogWeapon(weapon, string.Format(format, args));
        }

        public static void LogWeapon(Thing weapon1, Thing weapon2, string message)
        {
            Debug($"[Weapon:{Label(weapon1)} | {Label(weapon2)}] {message}");
        }

        public static void LogWeapon(Thing weapon1, Thing weapon2, string format, params object[] args)
        {
            LogWeapon(weapon1, weapon2, string.Format(format, args));
        }

        public static void LogError(string message, Exception ex = null)
        { Error(message, ex); }

        public static void DebugLog(string message)
        { Debug(message); }

        public static void LogPawnWeapon(Pawn pawn, Thing weapon, string message)
        {
            Debug($"[{GetPawnName(pawn)}] [Weapon:{Label(weapon)}] {message}");
        }

        #endregion Legacy shims

        #region Internals

        private enum LogLevel
        { Debug, Warn, Error }

        private static void Write(LogLevel level, string message, Exception ex = null, string callerMember = null, string callerFile = null, int callerLine = 0)
        {
            int ticks = 0;
            try { ticks = Find.TickManager?.TicksGame ?? 0; } catch { ticks = 0; }

            string normalized;
            int bucket;
            bool shouldWrite = ShouldLog(message, level, out normalized, out bucket);
            if (!shouldWrite)
            {
                return;
            }

            string ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var sanitizedMessage = SanitizeMessage(message);
            string line;
            if (level == LogLevel.Debug)
            {
                line = $"{ts} {sanitizedMessage}";
            }
            else if (level == LogLevel.Warn)
            {
                line = $"{ts} [WARN] {sanitizedMessage}";
            }
            else
            {
                line = $"{ts} [ERROR] {sanitizedMessage}";
            }

            if (Writer == null)
            {
                ReinitializeIfNeeded();
            }

            if (Writer != null)
            {
                try
                {
                    TryWriteSuppressionSummary(normalized, bucket);

                    Writer.WriteLine(line);

                    if (ex != null)
                    {
                        Writer.WriteLine($"Exception: {ex.GetType().Name}: {ex.Message}");
                        Writer.WriteLine($"Stack trace:\n{ex.StackTrace}");
                        Writer.WriteLine("------------------------------------------------");
                        try { Writer.Flush(); } catch { }
                    }
                }
                catch
                {
                    try { Writer?.Dispose(); } catch { }
                    Writer = null;
                }
            }

            if (level == LogLevel.Error)
            {
                Verse.Log.Error(Prefix(message) + (ex != null ? $": {ex.Message}" : string.Empty));
            }
            else if (level == LogLevel.Warn)
            {
                Verse.Log.Warning(Prefix(message));
            }
        }


        private static string NormalizeMessage(string message, LogLevel level)
        {
            if (level != LogLevel.Debug)
                return message;

            message = CoordPattern3.Replace(message, "(x,y,z)");
            message = CoordPattern2.Replace(message, "(x,y)");

            message = IdPattern.Replace(message, "thingIDNumber=X");

            message = PawnIdPattern.Replace(message, "X");

            return message;
        }


        private static bool ShouldLog(string message, LogLevel level, out string normalized, out int bucket)
        {
            normalized = NormalizeMessage(message, level);

            int ticks = 0;
            try { ticks = Find.TickManager?.TicksGame ?? 0; } catch { ticks = 0; }
            bucket = DedupTickWindow > 0 ? ticks / DedupTickWindow : 0;

            var key = $"{(int)level}|{normalized}";

            int lastBucket;
            if (recentMessages.TryGetValue(key, out lastBucket))
            {
                if (bucket == lastBucket)
                {
                    RecordSuppression(key, bucket);
                    return false;
                }

            }

            while (recentMessages.Count >= MaxDedupEntries && dedupOrder.Count > 0)
            {
                var oldKey = dedupOrder.Dequeue();
                recentMessages.Remove(oldKey);
            }

            recentMessages[key] = bucket;
            dedupOrder.Enqueue(key);
            return true;
        }

        private static void RecordSuppression(string key, int bucket)
        {
            SuppressionInfo info;
            if (suppressed.TryGetValue(key, out info))
            {
                info.Count++;
                info.LastBucket = bucket;
                suppressed[key] = info;
            }
            else
            {
                while (suppressed.Count >= MaxDedupEntries && suppressedOrder.Count > 0)
                {
                    var old = suppressedOrder.Dequeue();
                    suppressed.Remove(old);
                }

                info = new SuppressionInfo { Count = 1, FirstBucket = bucket, LastBucket = bucket };
                suppressed[key] = info;
                suppressedOrder.Enqueue(key);
            }
        }

        private static void TryWriteSuppressionSummary(string normalized, int currentBucket)
        {
            var keyDebug = $"{(int)LogLevel.Debug}|{normalized}";
            var keyWarn = $"{(int)LogLevel.Warn}|{normalized}";
            var keyErr = $"{(int)LogLevel.Error}|{normalized}";

            if (!TryWriteSuppressionSummaryForKey(keyDebug, currentBucket))
                if (!TryWriteSuppressionSummaryForKey(keyWarn, currentBucket))
                    TryWriteSuppressionSummaryForKey(keyErr, currentBucket);
        }

        private static bool TryWriteSuppressionSummaryForKey(string key, int currentBucket)
        {
            SuppressionInfo info;
            if (!suppressed.TryGetValue(key, out info) || info.Count <= 0)
                return false;

            int bucketsSpanned = Math.Max(0, info.LastBucket - info.FirstBucket + 1);
            int seconds = Math.Max(1, (DedupTickWindow / 60) * Math.Max(1, bucketsSpanned));


            suppressed.Remove(key);
            return true;
        }

        private static string Prefix(string msg)
        { return "[AutoArm] " + msg; }

        private static string Label(Thing t)
        { return t?.Label ?? "null"; }

        private static string SanitizeMessage(string message)
        {
            if (string.IsNullOrEmpty(message)) return string.Empty;
            var sanitized = message.Replace("\r\n", " âŽ ").Replace("\n", " âŽ ").Replace("\r", " âŽ ").Trim();
            sanitized = sanitized.Replace("[TEST] ", string.Empty).Replace("[TEST]", string.Empty);
            sanitized = sanitized.Replace("[DIAG] ", string.Empty).Replace("[DIAG]", string.Empty);
            return sanitized;
        }

        #endregion Internals
    }
}
