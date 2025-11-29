
using AutoArm.Definitions;
using AutoArm.Logging;
using System.Collections.Generic;
using Verse;

namespace AutoArm.Helpers
{
    /// <summary>
    /// Event-driven cooldown tracking via TickScheduler
    /// </summary>
    public static class CooldownMetrics
    {
        private static int activeCooldownCount = 0;

        private static HashSet<int> pawnsOnCooldown = new HashSet<int>();

        /// <summary>
        /// O(1) query - returns live count of pawns on cooldown
        /// </summary>
        public static int GetActiveCooldowns() => activeCooldownCount;

        /// <summary>
        /// Pawn equips - schedules cooldown expiry via TickScheduler
        /// </summary>
        public static void OnPawnEquippedWeapon(Pawn pawn)
        {
            if (pawn == null || pawn.Destroyed)
                return;

            int pawnId = pawn.thingIDNumber;
            int currentTick = Find.TickManager.TicksGame;
            int expireTick = currentTick + Constants.WeaponEquipCooldownTicks;

            if (!pawnsOnCooldown.Contains(pawnId))
            {
                activeCooldownCount++;
                pawnsOnCooldown.Add(pawnId);
            }
            else
            {
                // Cancel old schedule before rescheduling
                TickScheduler.Cancel(TickScheduler.EventType.CooldownExpiry, pawnId);
            }

            TickScheduler.Schedule(expireTick, TickScheduler.EventType.CooldownExpiry, pawnId);
        }

        /// <summary>
        /// Event handler called by TickScheduler when cooldown expires
        /// </summary>
        public static void OnCooldownExpiredEvent(int pawnId)
        {
            if (pawnsOnCooldown.Remove(pawnId))
            {
                activeCooldownCount--;
            }
        }

        /// <summary>
        /// Legacy method - kept for backward compatibility during transition
        /// Now a no-op since TickScheduler handles expiry
        /// </summary>
        public static void OnCooldownsExpired(int tick)
        {
            // Now handled by TickScheduler.ProcessTick() -> OnCooldownExpiredEvent
        }

        /// <summary>
        /// Pawn removed - cleans up scheduled expiry via TickScheduler
        /// </summary>
        public static void OnPawnRemoved(Pawn pawn)
        {
            if (pawn == null)
                return;

            if (!pawn.Discarded && !pawn.Destroyed && !pawn.Dead)
            {
                return;
            }

            int pawnId = pawn.thingIDNumber;

            if (pawnsOnCooldown.Remove(pawnId))
            {
                activeCooldownCount--;
                TickScheduler.Cancel(TickScheduler.EventType.CooldownExpiry, pawnId);
            }
        }

        /// <summary>
        /// Clear all state (for map changes, game reset)
        /// </summary>
        public static void Reset()
        {
            activeCooldownCount = 0;
            pawnsOnCooldown.Clear();
            // TickScheduler.Reset() handles clearing all scheduled events

            AutoArmLogger.Debug(() => "CooldownMetrics reset");
        }

        /// <summary>
        /// Rebuild state from existing pawn states (for load/initialization)
        /// </summary>
        public static void RebuildFromPawnStates()
        {
            activeCooldownCount = 0;
            pawnsOnCooldown.Clear();

            int currentTick = Find.TickManager.TicksGame;

            if (Find.Maps != null)
            {
                foreach (var map in Find.Maps)
                {
                    var component = Jobs.JobGiverMapComponent.GetComponent(map);
                    if (component?.PawnStates == null)
                        continue;

                    foreach (var kvp in component.PawnStates)
                    {
                        var pawn = kvp.Key;
                        var state = kvp.Value;

                        if (pawn == null || state == null)
                            continue;

                        if (state.LastEquipTick >= 0 &&
                            currentTick - state.LastEquipTick < Constants.WeaponEquipCooldownTicks)
                        {
                            int pawnId = pawn.thingIDNumber;
                            pawnsOnCooldown.Add(pawnId);
                            activeCooldownCount++;

                            int expireTick = state.LastEquipTick + Constants.WeaponEquipCooldownTicks;
                            TickScheduler.Schedule(expireTick, TickScheduler.EventType.CooldownExpiry, pawnId);
                        }
                    }
                }
            }

            AutoArmLogger.Debug(() => $"CooldownMetrics rebuilt: {activeCooldownCount} active cooldowns");
        }

        /// <summary>
        /// Periodic drift check - compares event count against actual pawn states
        /// Self-corrects if events were missed (e.g., from modded equip systems)
        /// </summary>
        public static bool CorrectDrift(out int eventCount, out int actualCount)
        {
            eventCount = activeCooldownCount;
            actualCount = CalculateActualCooldowns();

            if (eventCount != actualCount)
            {
                AutoArmLogger.Warn(
                    $"Cooldown counter drifted: event={eventCount}, actual={actualCount}. " +
                    $"Rebuilding (this should be rare - please report if frequent)");

                RebuildFromPawnStates();
                return true;
            }

            return false;
        }


        private static int CalculateActualCooldowns()
        {
            // Early exit - most common case: no active cooldowns
            if (activeCooldownCount == 0)
                return 0;

            int count = 0;
            int currentTick = Find.TickManager.TicksGame;

            if (Find.Maps != null)
            {
                foreach (var map in Find.Maps)
                {
                    var component = Jobs.JobGiverMapComponent.GetComponent(map);
                    if (component == null)
                        continue;

                    foreach (var state in component.PawnStates.Values)
                    {
                        if (state.LastEquipTick >= 0 &&
                            currentTick - state.LastEquipTick < Constants.WeaponEquipCooldownTicks)
                        {
                            count++;
                        }
                    }
                }
            }

            return count;
        }
    }
}
