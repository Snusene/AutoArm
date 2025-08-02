// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: Test log filtering utilities
// Suppresses expected test warnings during execution

using HarmonyLib;
using System;
using System.Collections.Generic;
using Verse;

namespace AutoArm.Testing.Helpers
{
    /// <summary>
    /// Harmony patch to filter out expected test-related warnings during test execution
    /// </summary>
    [HarmonyPatch(typeof(Log), "Error")]
    public static class TestLogFilter
    {
        private static bool isTestRunning = false;
        private static HashSet<string> suppressedMessages = new HashSet<string>
        {
            "Tried to destroy already-destroyed thing",
            "Use TryAddOrTransfer",
            "thing is already in another container"
        };
        
        public static void StartTestRun()
        {
            isTestRunning = true;
        }
        
        public static void EndTestRun()
        {
            isTestRunning = false;
        }
        
        [HarmonyPrefix]
        public static bool Prefix(string text)
        {
            // If we're not in a test, allow all messages
            if (!isTestRunning)
                return true;
                
            // Check if this is a message we want to suppress
            if (text != null)
            {
                foreach (var suppressedMsg in suppressedMessages)
                {
                    if (text.Contains(suppressedMsg))
                    {
                        // Suppress this message
                        return false;
                    }
                }
            }
            
            // Allow the message through
            return true;
        }
    }
    
    /// <summary>
    /// Disposable context for test execution that suppresses expected warnings
    /// </summary>
    public class TestExecutionContext : IDisposable
    {
        public TestExecutionContext()
        {
            TestLogFilter.StartTestRun();
        }
        
        public void Dispose()
        {
            TestLogFilter.EndTestRun();
        }
    }
}
