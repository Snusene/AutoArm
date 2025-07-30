using HarmonyLib;
using Verse;

namespace AutoArm
{
    // Patches to ensure debug log is flushed at appropriate times
    [HarmonyPatch(typeof(Game), "DeinitAndRemoveMap")]
    public static class Game_DeinitAndRemoveMap_FlushDebugLog
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            AutoArmDebugLogger.EnsureFlush();
        }
    }

    [HarmonyPatch(typeof(GameDataSaveLoader), "SaveGame")]
    public static class GameDataSaveLoader_SaveGame_FlushDebugLog
    {
        [HarmonyPrefix]
        public static void Prefix()
        {
            AutoArmDebugLogger.EnsureFlush();
        }
    }

    [HarmonyPatch(typeof(Root), "Shutdown")]
    public static class Root_Shutdown_FlushDebugLog
    {
        [HarmonyPrefix]
        public static void Prefix()
        {
            AutoArmDebugLogger.FlushAndClose();
        }
    }

    // Periodic flush every 5 seconds of game time
    [HarmonyPatch(typeof(TickManager), "DoSingleTick")]
    public static class TickManager_DoSingleTick_PeriodicFlush
    {
        private const int FLUSH_INTERVAL = 300; // 5 seconds

        [HarmonyPostfix]
        public static void Postfix()
        {
            if (Find.TickManager.TicksGame % FLUSH_INTERVAL == 0)
            {
                AutoArmDebugLogger.EnsureFlush();
            }
        }
    }
}