using System;
using System.IO;
using System.Text;
using Verse;

namespace AutoArm
{
    public static class AutoArmDebugLogger
    {
        private static string logFilePath;
        private static StringBuilder logBuffer = new StringBuilder();
        private static int bufferSize = 0;
        private const int MAX_BUFFER_SIZE = 1000; // Flush every 1000 lines
        private static object lockObject = new object();
        private static bool initialized = false;

        static AutoArmDebugLogger()
        {
            Initialize();
        }

        private static void Initialize()
        {
            if (initialized) return;

            try
            {
                // Use the same path as RimWorld's Player.log
                string documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "Low";
                string rimworldDataPath = Path.Combine(documentsPath, "Ludeon Studios", "RimWorld by Ludeon Studios");

                // Ensure directory exists
                if (!Directory.Exists(rimworldDataPath))
                {
                    Directory.CreateDirectory(rimworldDataPath);
                }

                logFilePath = Path.Combine(rimworldDataPath, "AutoArm_Debug.txt");

                // Create or clear the file
                File.WriteAllText(logFilePath, $"=== AutoArm Debug Log Started at {DateTime.Now} ===\n");
                initialized = true;

                Log.Message($"[AutoArm] Debug logging initialized. Log file: {logFilePath}");
            }
            catch (Exception e)
            {
                Log.Error($"[AutoArm] Failed to initialize debug logger: {e}");
            }
        }

        public static void DebugLog(string message, bool forceFlush = false)
        {
            // Only log if debug logging is enabled
            if (AutoArmMod.settings?.debugLogging != true)
                return;

            if (!initialized)
                Initialize();

            lock (lockObject)
            {
                try
                {
                    string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                    string logEntry = $"[{timestamp}] {message}\n";

                    // Add to buffer
                    logBuffer.Append(logEntry);
                    bufferSize++;

                    // Also log to console
                    Log.Message($"[AutoArm Debug] {message}");

                    // Flush if buffer is full or forced
                    if (bufferSize >= MAX_BUFFER_SIZE || forceFlush)
                    {
                        FlushBuffer();
                    }
                }
                catch (Exception e)
                {
                    Log.Error($"[AutoArm] Debug logging error: {e}");
                }
            }
        }

        private static void FlushBuffer()
        {
            if (logBuffer.Length == 0) return;

            try
            {
                File.AppendAllText(logFilePath, logBuffer.ToString());
                logBuffer.Clear();
                bufferSize = 0;
            }
            catch (Exception e)
            {
                Log.Error($"[AutoArm] Failed to write to debug log: {e}");
            }
        }

        public static void FlushAndClose()
        {
            lock (lockObject)
            {
                FlushBuffer();
                File.AppendAllText(logFilePath, $"\n=== AutoArm Debug Log Ended at {DateTime.Now} ===\n");
            }
        }

        // Call this periodically or on game save/exit
        public static void EnsureFlush()
        {
            if (AutoArmMod.settings?.debugLogging == true && bufferSize > 0)
            {
                lock (lockObject)
                {
                    FlushBuffer();
                }
            }
        }
    }

    // Extension methods to make it easy to use throughout the mod
    public static class AutoArmDebugExtensions
    {
        public static void DebugLog(this string message)
        {
            AutoArmDebugLogger.DebugLog(message);
        }

        public static void DebugLogFormat(string format, params object[] args)
        {
            AutoArmDebugLogger.DebugLog(string.Format(format, args));
        }
    }
}