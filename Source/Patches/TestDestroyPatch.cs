using AutoArm.Logging;
using AutoArm.Testing.Framework;
using HarmonyLib;
using Verse;
using Verse.AI;

namespace AutoArm.Patches
{
    /// <summary>
    /// Prevent double-destroy
    /// </summary>
    [HarmonyPatch(typeof(Thing), "Destroy")]
    [HarmonyPatchCategory(Patches.PatchCategories.Testing)]
    public static class TestDestroyPatch
    {
        public static bool Prefix(Thing __instance, DestroyMode mode = DestroyMode.Vanish)
        {
            if (!AutoArm.Testing.TestRunner.IsRunningTests)
            {
                return true;
            }

            if (__instance.Destroyed)
            {
                AutoArmLogger.Debug(() => $"[TEST] Blocked double-destroy - already marked as Destroyed");
                return false;
            }

            if (CleanupTracker.IsDestroyed(__instance))
            {
                AutoArmLogger.Debug(() => $"[TEST] Blocked double-destroy - in CleanupTracker");
                return false;
            }


            return true;
        }

        public static void Postfix(Thing __instance)
        {
            if (!AutoArm.Testing.TestRunner.IsRunningTests)
            {
                return;
            }

            if (__instance.Destroyed)
            {
                CleanupTracker.MarkDestroyed(__instance);
            }
        }
    }

    /// <summary>
    /// Track pawn destroy
    /// </summary>
    [HarmonyPatch(typeof(Pawn), "Destroy")]
    [HarmonyPatchCategory(Patches.PatchCategories.Testing)]
    public static class TestPawnDestroyPatch
    {
        public static bool Prefix(Pawn __instance, DestroyMode mode = DestroyMode.Vanish)
        {
            if (!AutoArm.Testing.TestRunner.IsRunningTests)
            {
                return true;
            }

            if (__instance.Destroyed)
            {
                AutoArmLogger.Debug(() => $"[TEST] Blocked pawn double-destroy - already marked as Destroyed");
                return false;
            }

            if (CleanupTracker.IsDestroyed(__instance))
            {
                AutoArmLogger.Debug(() => $"[TEST] Blocked double-destroy - in CleanupTracker");
                return false;
            }

            if (__instance.jobs != null)
            {
                __instance.jobs.StopAll(false);
                __instance.jobs.ClearQueuedJobs();
            }

            if (__instance.Map?.reservationManager != null)
            {
                __instance.Map.reservationManager.ReleaseAllClaimedBy(__instance);
            }


            return true;
        }

        public static void Postfix(Pawn __instance)
        {
            if (!AutoArm.Testing.TestRunner.IsRunningTests)
            {
                return;
            }

            if (__instance.Destroyed)
            {
                CleanupTracker.MarkDestroyed(__instance);
            }
        }
    }

    /// <summary>
    /// Handle destroyed equipment
    /// </summary>
    [HarmonyPatch(typeof(ThingWithComps), "Destroy")]
    [HarmonyPatchCategory(Patches.PatchCategories.Testing)]
    public static class TestWeaponDestroyPatch
    {
        public static bool Prefix(ThingWithComps __instance, DestroyMode mode = DestroyMode.Vanish)
        {
            if (!AutoArm.Testing.TestRunner.IsRunningTests)
            {
                return true;
            }

            if (!__instance.def.IsWeapon)
            {
                return true;
            }

            if (__instance.Destroyed)
            {
                AutoArmLogger.Debug(() => $"[TEST] Blocked weapon double-destroy - already marked as Destroyed");
                return false;
            }

            if (CleanupTracker.IsDestroyed(__instance))
            {
                AutoArmLogger.Debug(() => $"[TEST] Blocked double-destroy - in CleanupTracker");
                return false;
            }

            if (__instance.Map?.mapPawns != null)
            {
                foreach (var pawn in __instance.Map.mapPawns.AllPawnsSpawned)
                {
                    if (pawn?.jobs?.curJob != null)
                    {
                        var job = pawn.jobs.curJob;
                        if (job.targetA.Thing == __instance ||
                            job.targetB.Thing == __instance ||
                            job.targetC.Thing == __instance)
                        {
                            pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, false);
                        }
                    }
                }
            }

            if (__instance.Map?.reservationManager != null)
            {
                __instance.Map.reservationManager.ReleaseAllForTarget(__instance);
            }


            return true;
        }

        public static void Postfix(ThingWithComps __instance)
        {
            if (!AutoArm.Testing.TestRunner.IsRunningTests)
            {
                return;
            }

            if (!__instance.def.IsWeapon)
            {
                return;
            }

            if (__instance.Destroyed)
            {
                CleanupTracker.MarkDestroyed(__instance);
            }
        }
    }
}
