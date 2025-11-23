using AutoArm.Logging;
using HarmonyLib;
using Verse;
using Verse.AI;

namespace AutoArm.Patches
{
    /// <summary>
    /// Prevent bad reservations
    /// Fix reserve errors
    /// </summary>
    [HarmonyPatch(typeof(ReservationManager), "Reserve")]
    [HarmonyPatchCategory(PatchCategories.Testing)]
    public static class ReservationSafetyPatch
    {
        public static bool Prefix(Pawn claimant, Job job, LocalTargetInfo target, ref bool __result)
        {
            if (!AutoArm.Testing.TestRunner.IsRunningTests)
            {
                return true;
            }

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

                if (Testing.Framework.CleanupTracker.IsDestroyed(target.Thing))
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
    }

    /// <summary>
    /// Prevent destroyed checks
    /// </summary>
    [HarmonyPatch(typeof(ReservationManager), "CanReserve")]
    [HarmonyPatchCategory(PatchCategories.Testing)]
    public static class CanReserveSafetyPatch
    {
        public static bool Prefix(Pawn claimant, LocalTargetInfo target, ref bool __result)
        {
            if (!AutoArm.Testing.TestRunner.IsRunningTests)
            {
                return true;
            }

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
    }
}
