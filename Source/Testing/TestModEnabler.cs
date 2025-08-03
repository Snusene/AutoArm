// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: Test mod enabler utility
// Ensures mod is enabled during test execution

using System;
using Verse;
using AutoArm.Caching; using AutoArm.Helpers; using AutoArm.Logging;

namespace AutoArm.Testing
{
    /// <summary>
    /// Ensures the mod is enabled during test execution
    /// </summary>
    public static class TestModEnabler
    {
        private static bool? _originalModEnabled;
        private static bool? _originalDebugLogging;
        
        /// <summary>
        /// Force enable the mod for testing and return a disposable that will restore original state
        /// </summary>
        public static IDisposable ForceEnableForTesting()
        {
            // Save original state if not already saved
            if (!_originalModEnabled.HasValue && AutoArmMod.settings != null)
            {
                _originalModEnabled = AutoArmMod.settings.modEnabled;
                _originalDebugLogging = AutoArmMod.settings.debugLogging;
            }
            
            // Ensure settings exist
            if (AutoArmMod.settings == null)
            {
                AutoArmMod.settings = new AutoArmSettings();
                AutoArmLogger.Log("[TestModEnabler] Created new settings instance");
            }
            
            // Force enable mod
            AutoArmMod.settings.modEnabled = true;
            AutoArmMod.settings.debugLogging = false; // Reduce log spam during tests
            
            // Clear settings cache to ensure the enabled state is used
            CleanupHelper.ClearAllCaches();
            
            AutoArmLogger.Log($"[TestModEnabler] Mod force-enabled for testing. Settings hash: {AutoArmMod.settings.GetHashCode()}");
            
            // Verify it worked
            if (!AutoArmMod.settings.modEnabled)
            {
                Log.Error("[TestModEnabler] CRITICAL: Failed to enable mod!");
                // Try again with a new instance
                AutoArmMod.settings = new AutoArmSettings { modEnabled = true };
                CleanupHelper.ClearAllCaches();
            }
            
            return new ModStateRestorer();
        }
        
        /// <summary>
        /// Ensure mod stays enabled during test execution
        /// Call this periodically or before critical operations
        /// </summary>
        public static void EnsureModEnabled()
        {
            if (!TestRunner.IsRunningTests)
                return;
                
            if (AutoArmMod.settings?.modEnabled != true)
            {
                AutoArmLogger.LogError("[TestModEnabler] Mod was disabled during test! Re-enabling...");
                
                if (AutoArmMod.settings == null)
                {
                    AutoArmMod.settings = new AutoArmSettings();
                }
                
                AutoArmMod.settings.modEnabled = true;
                CleanupHelper.ClearAllCaches();
            }
        }
        
        private class ModStateRestorer : IDisposable
        {
            public void Dispose()
            {
                // Only restore if we have original values saved
                if (_originalModEnabled.HasValue && AutoArmMod.settings != null)
                {
                    AutoArmMod.settings.modEnabled = _originalModEnabled.Value;
                    AutoArmMod.settings.debugLogging = _originalDebugLogging.Value;
                    
                    AutoArmLogger.Log($"[TestModEnabler] Restored mod state: enabled={_originalModEnabled.Value}");
                    
                    // Clear saved state
                    _originalModEnabled = null;
                    _originalDebugLogging = null;
                }
            }
        }
    }
}
