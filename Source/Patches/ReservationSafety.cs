using HarmonyLib;
using Verse;
using Verse.AI;

namespace AutoArm.Patches
{
    [HarmonyPatch(typeof(ReservationManager), "Reserve")]
    [HarmonyPatchCategory(PatchCategories.Testing)]
    internal static class ReservationSafetyPatch
    {
        public static bool Prefix(Pawn claimant, Job job, LocalTargetInfo target, ref bool __result)
        {
            if (!AutoArm.Testing.TestRunner.IsRunningTests)
            {
                return true;
            }

            try
            {
                if (job == null)
                {
                    AutoArmLogger.Debug(() => $"[TEST] Blocked reservation attempt by {claimant?.Name} - no valid job");
                    __result = false;
                    return false;
                }

                if (target.Thing != null)
                {
                    if (target.Thing.Destroyed)
                    {
                        AutoArmLogger.Debug(() => $"[TEST] Blocked reservation attempt by {claimant?.Name} on destroyed thing {target.Thing}");
                        __result = false;
                        return false;
                    }

                    if (Testing.Helpers.CleanupTracker.IsDestroyed(target.Thing))
                    {
                        AutoArmLogger.Debug(() => $"[TEST] Blocked reservation attempt by {claimant?.Name} on cleanup-tracked destroyed thing {target.Thing}");
                        __result = false;
                        return false;
                    }
                }

                if (claimant == null || claimant.Destroyed || !claimant.Spawned)
                {
                    AutoArmLogger.Debug(() => $"[TEST] Blocked reservation attempt - invalid pawn state");
                    __result = false;
                    return false;
                }

                return true;
            }
            catch (System.Exception e)
            {
                AutoArmLogger.ErrorPatch(e, "ReservationSafetyPatch.Prefix");
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(ReservationManager), "CanReserve")]
    [HarmonyPatchCategory(PatchCategories.Testing)]
    internal static class CanReserveSafetyPatch
    {
        public static bool Prefix(Pawn claimant, LocalTargetInfo target, ref bool __result)
        {
            if (!AutoArm.Testing.TestRunner.IsRunningTests)
            {
                return true;
            }

            try
            {
                if (target.Thing != null && target.Thing.Destroyed)
                {
                    __result = false;
                    return false;
                }

                if (claimant == null || claimant.Destroyed || !claimant.Spawned)
                {
                    __result = false;
                    return false;
                }

                return true;
            }
            catch (System.Exception e)
            {
                AutoArmLogger.ErrorPatch(e, "CanReserveSafetyPatch.Prefix");
                return true;
            }
        }
    }
}
