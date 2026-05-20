using AutoArm.Caching;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace AutoArm.Testing.Helpers
{
    internal static class CleanupHelper
    {
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

                WeaponCache.RemoveWeaponFromCache(weapon);

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

        public static void DestroyPawns(IEnumerable<Pawn> pawns)
        {
            if (pawns == null) return;

            var pawnList = pawns.ToList();

            foreach (var pawn in pawnList)
            {
                DestroyPawn(pawn);
            }
        }

        public static void ClearWeaponsInArea(Map map, CellRect area)
        {
            if (map == null) return;

            var weaponsToDestroy = map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon)
                .Where(t => area.Contains(t.Position))
                .OfType<ThingWithComps>()
                .ToList();

            DestroyWeapons(weaponsToDestroy);
        }

        public static void ClearTestWeaponsOnMap(Map map)
        {
            if (map == null) return;

            var testWeapons = CleanupTracker.CreatedThings
                .OfType<ThingWithComps>()
                .Where(w => w != null && !w.Destroyed && w.Map == map)
                .ToList();

            if (testWeapons.Count > 0)
            {
                AutoArmLogger.Debug(() => $"[TEST] ClearTestWeaponsOnMap: Destroying {testWeapons.Count} test-created weapons");
                DestroyWeapons(testWeapons);
            }
        }

        public static void ClearTestPawnsOnMap(Map map)
        {
            if (map == null) return;

            var testPawns = CleanupTracker.CreatedPawns
                .Where(p => p != null && !p.Destroyed && p.Map == map)
                .ToList();

            if (testPawns.Count > 0)
            {
                AutoArmLogger.Debug(() => $"[TEST] ClearTestPawnsOnMap: Destroying {testPawns.Count} test-created pawns");
                DestroyPawns(testPawns);
            }
        }

        public static void ResetMapForTesting(Map map)
        {
            if (map == null) return;

            try
            {
                ClearTestWeaponsOnMap(map);
                ClearTestPawnsOnMap(map);

                WeaponCache.ClearAllCaches();

                if (map.reservationManager != null)
                {
                    foreach (var thing in CleanupTracker.CreatedThings)
                    {
                        if (thing != null && !thing.Destroyed)
                            map.reservationManager.ReleaseAllForTarget(thing);
                    }
                    foreach (var pawn in CleanupTracker.CreatedPawns)
                    {
                        if (pawn != null && !pawn.Destroyed)
                            map.reservationManager.ReleaseAllClaimedBy(pawn);
                    }
                }

                AutoArmLogger.Debug(() => "[TEST] ResetMapForTesting done");
            }
            catch (Exception e)
            {
                AutoArmLogger.Warn("[TEST] Error during map reset for testing", e);
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

        public static void ClearAllCooldownsForPawn(Pawn pawn)
        {
            if (pawn == null) return;
            try
            {
                AutoArm.Blacklist.ClearBlacklist(pawn);
                AutoArm.ForcedWeapons.ClearForced(pawn);
            }
            catch (Exception e)
            {
                AutoArmLogger.Warn($"Error clearing cooldowns for pawn {pawn.Name}", e);
            }
        }

        public static void ResetAllSystems()
        {
            try
            {
                if (AutoArmMod.settings != null)
                    AutoArmMod.settings.modEnabled = true;

                WeaponCache.ClearAllCaches();
                WeaponCache.CleanupDestroyedMaps();
                AutoArm.Helpers.DroppedItems.ClearAll();

                var allPawns = Find.Maps?.SelectMany(m => m.mapPawns?.AllPawns ?? Enumerable.Empty<Pawn>())
                             ?? Enumerable.Empty<Pawn>();
                foreach (var pawn in allPawns)
                {
                    AutoArm.ForcedWeapons.ClearForced(pawn);
                    AutoArm.Blacklist.ClearBlacklist(pawn);
                }
                AutoArm.ForcedWeapons.Cleanup();
                AutoArm.Blacklist.CleanupOldEntries();

                AutoArm.Jobs.JobGiver_PickUpBetterWeapon.ResetForTesting();
                AutoArm.Jobs.JobGiver_PickUpBetterWeapon.CleanupCaches();

                AutoArm.Helpers.Cleanup.PerformFullCleanup();
            }
            catch (Exception e)
            {
                AutoArmLogger.Warn("Error resetting AutoArm systems", e);
            }
        }

        public static void PreparePawnForTest(Pawn pawn)
        {
            if (pawn == null) return;
            try
            {
                ClearAllCooldownsForPawn(pawn);
                AutoArm.Jobs.JobGiver_PickUpBetterWeapon.ResetForTesting();
                AutoArm.Jobs.JobGiver_PickUpBetterWeapon.CleanupCaches();

                pawn.jobs?.StopAll();

                if (pawn.Drafted)
                    pawn.drafter.Drafted = false;

                if (pawn.Downed && pawn.health != null)
                {
                    var hediffsToRemove = pawn.health.hediffSet.hediffs
                        .Where(h => h.def.stages?.Any(s => s.capMods?.Any() == true) == true)
                        .ToList();

                    foreach (var hediff in hediffsToRemove)
                        pawn.health.RemoveHediff(hediff);
                }
            }
            catch (Exception e)
            {
                AutoArmLogger.Warn($"Error preparing pawn {pawn.Name} for test", e);
            }
        }
    }
}
