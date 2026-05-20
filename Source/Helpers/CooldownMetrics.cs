
using AutoArm.Definitions;
using System.Collections.Generic;
using Verse;

namespace AutoArm.Helpers
{
    internal static class CooldownMetrics
    {
        private static int activeCooldownCount = 0;

        private static readonly HashSet<int> pawnsOnCooldown = new HashSet<int>();

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
                // Cancel old schedule
                TickScheduler.Cancel(TickScheduler.EventType.CooldownExpiry, pawnId);
            }

            TickScheduler.Schedule(expireTick, TickScheduler.EventType.CooldownExpiry, pawnId);
        }

        public static void OnCooldownExpiredEvent(int pawnId)
        {
            if (pawnsOnCooldown.Remove(pawnId))
            {
                activeCooldownCount--;
            }
        }

        public static void OnPawnRemoved(Pawn pawn)
        {
            if (pawn == null)
                return;

            if (!pawn.Discarded && !pawn.Destroyed && !pawn.Dead && pawn.Spawned)
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

        public static void Reset()
        {
            activeCooldownCount = 0;
            pawnsOnCooldown.Clear();
            // TickScheduler clears events

            AutoArmLogger.Debug(() => "CooldownMetrics reset");
        }

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
                        int pawnId = kvp.Key;
                        var state = kvp.Value;

                        if (state == null)
                            continue;

                        if (state.LastEquipTick >= 0 &&
                            currentTick - state.LastEquipTick < Constants.WeaponEquipCooldownTicks)
                        {
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

        public static bool CorrectDrift(out int eventCount, out int actualCount)
        {
            eventCount = activeCooldownCount;
            actualCount = CalculateActualCooldowns();

            if (eventCount != actualCount)
            {
                AutoArmLogger.WarnFileOnly(
                    $"Cooldown counter drifted: event={eventCount}, actual={actualCount}. " +
                    $"Rebuilding (this should be rare - please report if frequent)");

                RebuildFromPawnStates();
                return true;
            }

            return false;
        }


        private static int CalculateActualCooldowns()
        {
            // Early exit
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
