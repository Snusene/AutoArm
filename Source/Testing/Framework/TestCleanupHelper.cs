using AutoArm.Caching;
using AutoArm.Logging;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace AutoArm.Testing.Framework
{
    /// <summary>
    /// Centralized cleanup helper for all test scenarios
    /// ALWAYS use these methods instead of calling Destroy() directly!
    /// </summary>
    public static class TestCleanupHelper
    {
        /// <summary>
        /// Safely destroy a weapon - handles jobs, reservations, tracking, and cache cleanup
        /// </summary>
        public static void DestroyWeapon(ThingWithComps weapon)
        {
            if (weapon == null || weapon.Destroyed) return;

            if (CleanupTracker.IsDestroyed(weapon)) return;

            try
            {
                if (weapon.Map?.reservationManager != null)
                {
                    weapon.Map.reservationManager.ReleaseAllForTarget(weapon);
                }

                if (weapon.Map?.mapPawns != null)
                {
                    foreach (var pawn in weapon.Map.mapPawns.AllPawnsSpawned.ToList())
                    {
                        if (pawn?.jobs?.curJob != null)
                        {
                            var job = pawn.jobs.curJob;
                            if (job.targetA.Thing == weapon ||
                                job.targetB.Thing == weapon ||
                                job.targetC.Thing == weapon)
                            {
                                pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, false);
                            }
                        }
                    }
                }

                if (weapon.holdingOwner != null)
                {
                    weapon.holdingOwner.Remove(weapon);
                }

                WeaponCacheManager.RemoveWeaponFromCache(weapon);

                if (weapon.Spawned)
                {
                    weapon.DeSpawn(DestroyMode.Vanish);
                }

                if (!weapon.Destroyed)
                {
                    bool weaponInUse = false;
                    if (weapon.Map?.mapPawns != null)
                    {
                        foreach (var pawn in weapon.Map.mapPawns.AllPawnsSpawned)
                        {
                            if (pawn?.jobs?.curJob != null)
                            {
                                var job = pawn.jobs.curJob;
                                if ((job.targetA.Thing == weapon || job.targetB.Thing == weapon || job.targetC.Thing == weapon) &&
                                    job.def == JobDefOf.Equip)
                                {
                                    weaponInUse = true;
                                    break;
                                }
                            }
                        }
                    }

                    if (!weaponInUse)
                    {
                        CleanupTracker.MarkDestroyed(weapon);
                        weapon.Destroy(DestroyMode.Vanish);
                    }
                }
            }
            catch (Exception ex)
            {
                if (TestRunner.IsRunningTests)
                {
                    AutoArmLogger.Debug(() => $"[TEST] Exception during weapon cleanup: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Safely destroy a pawn - handles jobs, equipment, reservations, and tracking
        /// </summary>
        public static void DestroyPawn(Pawn pawn)
        {
            if (pawn == null || pawn.Destroyed) return;

            if (CleanupTracker.IsDestroyed(pawn)) return;

            try
            {
                if (pawn.Map?.reservationManager != null)
                {
                    pawn.Map.reservationManager.ReleaseAllClaimedBy(pawn);
                    pawn.Map.reservationManager.ReleaseAllForTarget(pawn);
                }

                if (pawn.jobs != null)
                {
                    pawn.jobs.StopAll(false);
                    pawn.jobs.ClearQueuedJobs();
                    if (pawn.jobs.jobQueue != null)
                    {
                        pawn.jobs.jobQueue.Clear(pawn, false);
                    }
                }

                if (pawn.equipment?.Primary != null)
                {
                    var weapon = pawn.equipment.Primary;
                    pawn.equipment.Remove(weapon);
                    DestroyWeapon(weapon);
                }

                if (pawn.inventory?.innerContainer != null)
                {
                    var items = pawn.inventory.innerContainer.ToList();
                    foreach (var item in items)
                    {
                        if (item is ThingWithComps twc)
                        {
                            DestroyWeapon(twc);
                        }
                        else if (item != null && !item.Destroyed)
                        {
                            item.Destroy(DestroyMode.Vanish);
                        }
                    }
                    pawn.inventory.innerContainer.Clear();
                }

                if (pawn.Spawned)
                {
                    pawn.DeSpawn(DestroyMode.Vanish);
                }

                if (pawn.Map != null)
                {
                    pawn.Map.mapPawns.DeRegisterPawn(pawn);
                }

                if (!pawn.Destroyed)
                {
                    CleanupTracker.MarkDestroyed(pawn);
                    pawn.Destroy(DestroyMode.Vanish);
                }
            }
            catch (Exception ex)
            {
                if (TestRunner.IsRunningTests)
                {
                    AutoArmLogger.Debug(() => $"[TEST] Exception during pawn cleanup: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Safely destroy multiple weapons
        /// </summary>
        public static void DestroyWeapons(IEnumerable<ThingWithComps> weapons)
        {
            if (weapons == null) return;

            var weaponList = weapons.ToList();

            StopJobsTargetingThings(weaponList);

            foreach (var weapon in weaponList)
            {
                DestroyWeapon(weapon);
            }
        }

        /// <summary>
        /// Safely destroy multiple pawns
        /// </summary>
        public static void DestroyPawns(IEnumerable<Pawn> pawns)
        {
            if (pawns == null) return;

            var pawnList = pawns.ToList();

            foreach (var pawn in pawnList)
            {
                DestroyPawn(pawn);
            }
        }

        /// <summary>
        /// Clear weapons in an area (for test setup)
        /// </summary>
        public static void ClearWeaponsInArea(Map map, CellRect area)
        {
            if (map == null) return;

            var weaponsToDestroy = map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon)
                .Where(t => area.Contains(t.Position))
                .OfType<ThingWithComps>()
                .ToList();

            DestroyWeapons(weaponsToDestroy);
        }

        /// <summary>
        /// Clear ALL weapons on the entire map (for comprehensive test isolation)
        /// </summary>
        public static void ClearAllWeaponsOnMap(Map map)
        {
            if (map == null) return;

            var allWeapons = map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon)
                .OfType<ThingWithComps>()
                .ToList();

            if (allWeapons.Count > 0)
            {
                AutoArmLogger.Debug(() => $"[TEST] ClearAllWeaponsOnMap: Destroying {allWeapons.Count} weapons for test isolation");
                DestroyWeapons(allWeapons);
            }
        }

        /// <summary>
        /// Clear ALL test pawns on the map (non-player pawns only for safety)
        /// </summary>
        public static void ClearTestPawnsOnMap(Map map)
        {
            if (map == null) return;

            var testPawns = map.mapPawns.AllPawnsSpawned
                .Where(p => p.Name?.ToStringShort?.Contains("Test") == true ||
                           p.Name?.ToStringShort?.Contains("Race") == true ||
                           p.Name?.ToStringShort?.Contains("Pawn") == true)
                .ToList();

            if (testPawns.Count > 0)
            {
                AutoArmLogger.Debug(() => $"[TEST] ClearTestPawnsOnMap: Destroying {testPawns.Count} test pawns for test isolation");
                DestroyPawns(testPawns);
            }
        }

        /// <summary>
        /// Complete map reset for maximum test isolation
        /// </summary>
        public static void ResetMapForTesting(Map map)
        {
            if (map == null) return;

            try
            {
                ClearAllWeaponsOnMap(map);
                ClearTestPawnsOnMap(map);

                WeaponCacheManager.ClearAllCaches();

                if (map.reservationManager != null)
                {
                    var allThings = map.listerThings?.AllThings?.ToList() ?? new List<Thing>();
                    foreach (var thing in allThings)
                    {
                        map.reservationManager.ReleaseAllForTarget(thing);
                    }
                }


                AutoArmLogger.Debug(() => "[TEST] ResetMapForTesting: Complete map reset performed");
            }
            catch (Exception e)
            {
                AutoArmLogger.Error("[TEST] Error during map reset for testing", e);
            }
        }


        private static void StopJobsTargetingThings(IEnumerable<Thing> things)
        {
            if (things == null || !things.Any()) return;

            var thingSet = new HashSet<Thing>(things);

            foreach (var map in Find.Maps ?? Enumerable.Empty<Map>())
            {
                foreach (var pawn in map.mapPawns?.AllPawnsSpawned?.ToList() ?? new List<Pawn>())
                {
                    var curJob = pawn?.jobs?.curJob;
                    if (curJob == null) continue;

                    if ((curJob.targetA.HasThing && thingSet.Contains(curJob.targetA.Thing)) ||
                        (curJob.targetB.HasThing && thingSet.Contains(curJob.targetB.Thing)) ||
                        (curJob.targetC.HasThing && thingSet.Contains(curJob.targetC.Thing)))
                    {
                        pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, false);
                    }
                }
            }
        }

        /// <summary>
        /// Convenience helper used by tests to clean up a pawn and any number of weapons
        /// </summary>
        public static void CleanupTest(Pawn pawn, params ThingWithComps[] weapons)
        {
            if (weapons != null)
            {
                foreach (var w in weapons)
                {
                    DestroyWeapon(w);
                }
            }

            DestroyPawn(pawn);
        }
    }
}
