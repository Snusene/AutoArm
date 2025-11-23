
using AutoArm.Definitions;
using AutoArm.Logging;
using System.Collections.Generic;
using Verse;

namespace AutoArm.Helpers
{
    /// <summary>
    /// Event-driven cooldown tracking
    /// Event hooks
    /// </summary>
    public static class CooldownMetrics
    {
        private static int activeCooldownCount = 0;

        private static Dictionary<int, List<int>> cooldownExpirySchedule = new Dictionary<int, List<int>>();

        private static HashSet<int> pawnsOnCooldown = new HashSet<int>();

        /// <summary>
        /// O(1) query - returns live count of pawns on cooldown
        /// </summary>
        public static int GetActiveCooldowns() => activeCooldownCount;

        /// <summary>
        /// Pawn equips
        /// Increments counter and schedules expiry
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

                if (!cooldownExpirySchedule.TryGetValue(expireTick, out var list))
                {
                    list = new List<int>();
                    cooldownExpirySchedule[expireTick] = list;
                }
                list.Add(pawnId);

                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug(() =>
                        $"[CooldownEvent] {AutoArmLogger.GetPawnName(pawn)} started cooldown, " +
                        $"count: {activeCooldownCount}, expires: tick {expireTick}");
                }
            }
            else
            {
                RemoveFromExpirySchedule(pawnId);

                if (!cooldownExpirySchedule.TryGetValue(expireTick, out var list))
                {
                    list = new List<int>();
                    cooldownExpirySchedule[expireTick] = list;
                }
                list.Add(pawnId);

                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug(() =>
                        $"[CooldownEvent] {AutoArmLogger.GetPawnName(pawn)} extended cooldown, " +
                        $"new expiry: tick {expireTick}");
                }
            }
        }

        /// <summary>
        /// Process expired
        /// O(1) dictionary lookup instead of O(n) iteration
        /// </summary>
        public static void OnCooldownsExpired(int tick)
        {
            if (cooldownExpirySchedule.TryGetValue(tick, out var expiredPawnIds))
            {
                foreach (int pawnId in expiredPawnIds)
                {
                    pawnsOnCooldown.Remove(pawnId);
                }

                activeCooldownCount -= expiredPawnIds.Count;
                cooldownExpirySchedule.Remove(tick);

                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug(() =>
                        $"[CooldownEvent] {expiredPawnIds.Count} cooldowns expired, " +
                        $"remaining: {activeCooldownCount}");
                }
            }
        }

        /// <summary>
        /// Pawn removed
        /// Decrements counter and cleans up scheduled expiry
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

                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug(() =>
                        $"[CooldownEvent] {AutoArmLogger.GetPawnName(pawn)} removed while on cooldown, " +
                        $"count: {activeCooldownCount}");
                }

                RemoveFromExpirySchedule(pawnId);
            }
        }


        private static void RemoveFromExpirySchedule(int pawnId)
        {
            int keyToRemove = -1;
            foreach (var kvp in cooldownExpirySchedule)
            {
                if (kvp.Value.Remove(pawnId))
                {
                    if (kvp.Value.Count == 0)
                    {
                        keyToRemove = kvp.Key;
                    }
                    break;
                }
            }
            if (keyToRemove != -1)
            {
                cooldownExpirySchedule.Remove(keyToRemove);
            }
        }

        /// <summary>
        /// Clear all state (for map changes, game reset)
        /// </summary>
        public static void Reset()
        {
            activeCooldownCount = 0;
            cooldownExpirySchedule.Clear();
            pawnsOnCooldown.Clear();

            AutoArmLogger.Debug(() => "CooldownMetrics reset");
        }

        /// <summary>
        /// Rebuild state from existing pawn states (for load/initialization)
        /// Rebuild on load from saved pawn data
        /// </summary>
        public static void RebuildFromPawnStates()
        {
            Reset();

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
                            if (!cooldownExpirySchedule.TryGetValue(expireTick, out var list))
                            {
                                list = new List<int>();
                                cooldownExpirySchedule[expireTick] = list;
                            }
                            list.Add(pawnId);
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
