using System;
using Verse;

namespace AutoArm
{
    /// <summary>
    /// Simple memory cleanup manager that uses the consolidated CleanupHelper
    /// </summary>
    [StaticConstructorOnStartup]
    public static class MemoryCleanupManager
    {
        static MemoryCleanupManager()
        {
            try
            {
                HarmonyLib.Harmony harmony = new HarmonyLib.Harmony("AutoArm.MemoryCleanup");
                harmony.Patch(
                    typeof(Game).GetMethod("UpdatePlay"),
                    postfix: new HarmonyLib.HarmonyMethod(typeof(MemoryCleanupManager).GetMethod(nameof(GameUpdatePlay_Postfix)))
                );
            }
            catch (Exception e)
            {
                Log.Error($"[AutoArm] Failed to patch Game.UpdatePlay for memory cleanup: {e.Message}");
            }
        }

        public static void GameUpdatePlay_Postfix()
        {
            // Use consolidated cleanup helper (fixes #4, #11, #28)
            if (CleanupHelper.ShouldRunCleanup())
            {
                CleanupHelper.PerformFullCleanup();
            }
        }
    }
}