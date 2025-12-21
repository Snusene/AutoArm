using AutoArm.Caching;
using AutoArm.Definitions;
using AutoArm.Helpers;
using AutoArm.Jobs;
using AutoArm.Weapons;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace AutoArm.Testing.Scenarios
{
    /// <summary>
    /// MEDIUM PRIORITY: Test mental break weapon handling
    /// </summary>
    public class MentalBreakTest : ITestScenario
    {
        public string Name => "Mental Break Weapon Handling";
        private Pawn testPawn;
        private ThingWithComps originalWeapon;
        private ThingWithComps nearbyWeapon;

        public void Setup(Map map)
        {
            if (map == null) return;

            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn != null)
            {
                originalWeapon = ThingMaker.MakeThing(AutoArmDefOf.Gun_AssaultRifle) as ThingWithComps;
                if (originalWeapon != null)
                {
                    testPawn.equipment?.DestroyAllEquipment();
                    testPawn.equipment?.AddEquipment(originalWeapon);

                    ForcedWeapons.SetForced(testPawn, originalWeapon);
                }

                nearbyWeapon = TestHelpers.CreateWeapon(map, AutoArmDefOf.Gun_Autopistol,
                    testPawn.Position + new IntVec3(3, 0, 0));
                if (nearbyWeapon != null)
                {
                    WeaponCacheManager.AddWeaponToCache(nearbyWeapon);
                }
            }
        }

        public TestResult Run()
        {
            if (testPawn == null || originalWeapon == null)
                return TestResult.Failure("Test setup failed");

            var result = new TestResult { Success = true };
            var jobGiver = new JobGiver_PickUpBetterWeapon();

            try
            {
                if (testPawn.mindState == null)
                    testPawn.mindState = new Pawn_MindState(testPawn);

                var berserkDef = DefDatabase<MentalStateDef>.GetNamed("Berserk", false);
                if (berserkDef != null)
                {
                    testPawn.mindState.mentalStateHandler.TryStartMentalState(berserkDef, null, true);
                    result.Data["BerserkStarted"] = testPawn.InMentalState;

                    var job = jobGiver.TestTryGiveJob(testPawn);
                    result.Data["JobDuringBerserk"] = job != null;

                    if (job != null)
                    {
                        result.Data["Warning1"] = "Job created during berserk state";
                    }

                    testPawn.mindState.mentalStateHandler.Reset();
                }
            }
            catch (Exception e)
            {
                result.Data["BerserkException"] = e.Message;
            }

            if (testPawn.equipment?.Primary != null)
            {
                var currentWeapon = testPawn.equipment.Primary;
                result.Data["WeaponBeforeTantrum"] = currentWeapon.Label;

                testPawn.equipment.DestroyEquipment(currentWeapon);
                result.Data["WeaponDestroyed"] = currentWeapon.Destroyed;

                var job = jobGiver.TestTryGiveJob(testPawn);
                result.Data["JobAfterWeaponDestroyed"] = job != null;

                if (job != null && nearbyWeapon != null && job.targetA.Thing == nearbyWeapon)
                {
                    result.Data["TargetsNearbyWeapon"] = true;
                }
            }

            if (!originalWeapon.Destroyed)
            {
                if (originalWeapon.Spawned) originalWeapon.DeSpawn();
                testPawn.equipment?.AddEquipment(originalWeapon);

                testPawn.equipment.TryDropEquipment(originalWeapon, out var dropped, testPawn.Position);

                if (dropped != null)
                {
                    result.Data["WeaponDropped"] = true;

                    DroppedItemTracker.MarkAsDropped(dropped, 600);

                    var job = jobGiver.TestTryGiveJob(testPawn);
                    if (job != null && job.targetA.Thing == dropped)
                    {
                        result.Success = false;
                        result.Data["Error"] = "Immediately picking up weapon dropped in fight!";
                    }
                    else
                    {
                        result.Data["DroppedWeaponIgnored"] = true;
                    }
                }
            }

            if (!originalWeapon.Destroyed && originalWeapon.Spawned)
            {
                originalWeapon.DeSpawn();
                testPawn.equipment?.AddEquipment(originalWeapon);

                bool stillForced = ForcedWeapons.IsForced(testPawn, originalWeapon);
                result.Data["ForcedStatusRetained"] = stillForced;

                if (!stillForced)
                {
                    result.Data["Warning2"] = "Forced status lost after mental break";
                }
            }

            return result;
        }

        public void Cleanup()
        {
            testPawn?.mindState?.mentalStateHandler?.Reset();

            ForcedWeapons.ClearForced(testPawn);
            TestHelpers.SafeDestroyPawn(testPawn);
            TestHelpers.SafeDestroyWeapon(originalWeapon);
            TestHelpers.SafeDestroyWeapon(nearbyWeapon);
        }
    }

    /// <summary>
    /// MEDIUM PRIORITY: Test trading and gifting scenarios
    /// </summary>
    public class TradingTest : ITestScenario
    {
        public string Name => "Trading and Gifting Weapon Handling";
        private Pawn testPawn;
        private ThingWithComps forcedWeapon;
        private ThingWithComps tradeWeapon;
        private Pawn trader;

        public void Setup(Map map)
        {
            if (map == null) return;

            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn != null)
            {
                forcedWeapon = ThingMaker.MakeThing(AutoArmDefOf.Gun_AssaultRifle) as ThingWithComps;
                if (forcedWeapon != null)
                {
                    testPawn.equipment?.DestroyAllEquipment();
                    testPawn.equipment?.AddEquipment(forcedWeapon);
                    ForcedWeapons.SetForced(testPawn, forcedWeapon);
                }
            }

            var traderFaction = Find.FactionManager.AllFactions
                .FirstOrDefault(f => f != Faction.OfPlayer && !f.HostileTo(Faction.OfPlayer));

            if (traderFaction != null)
            {
                trader = TestHelpers.CreateTestPawn(map, new TestHelpers.TestPawnConfig
                {
                    Name = "Trader",
                    Kind = PawnKindDefOf.Villager
                });

                if (trader != null)
                {
                    trader.SetFaction(traderFaction);

                    tradeWeapon = ThingMaker.MakeThing(AutoArmDefOf.Gun_ChainShotgun) as ThingWithComps;
                    if (tradeWeapon != null)
                    {
                        var comp = tradeWeapon.TryGetComp<CompQuality>();
                        comp?.SetQuality(QualityCategory.Masterwork, ArtGenerationContext.Colony);

                        trader.equipment?.DestroyAllEquipment();
                        trader.equipment?.AddEquipment(tradeWeapon);
                    }
                }
            }
        }

        public TestResult Run()
        {
            if (testPawn == null || forcedWeapon == null)
                return TestResult.Failure("Test setup failed");

            var result = new TestResult { Success = true };
            var jobGiver = new JobGiver_PickUpBetterWeapon();

            result.Data["WeaponIsForced"] = ForcedWeapons.IsForced(testPawn, forcedWeapon);
            result.Data["ForcedWeaponDef"] = forcedWeapon.def.defName;

            if (testPawn.equipment?.Primary == forcedWeapon)
            {
                testPawn.equipment.Remove(forcedWeapon);

                bool stillForced = ForcedWeapons.IsForced(testPawn, null);
                result.Data["ForcedStatusAfterSale"] = stillForced;

                GenSpawn.Spawn(forcedWeapon, testPawn.Position + new IntVec3(5, 0, 0), testPawn.Map);
                WeaponCacheManager.AddWeaponToCache(forcedWeapon);

                var job = jobGiver.TestTryGiveJob(testPawn);
                if (job != null && job.targetA.Thing == forcedWeapon)
                {
                    result.Data["TriesToReacquireSoldWeapon"] = true;
                }
            }

            if (trader?.equipment?.Primary != null)
            {
                var traderWeapon = trader.equipment.Primary;
                result.Data["TraderWeapon"] = traderWeapon.Label;

                trader.equipment.Remove(traderWeapon);
                GenSpawn.Spawn(traderWeapon, testPawn.Position + new IntVec3(2, 0, 0), testPawn.Map);
                WeaponCacheManager.AddWeaponToCache(traderWeapon);

                var job = jobGiver.TestTryGiveJob(testPawn);
                if (job != null && job.targetA.Thing == traderWeapon)
                {
                    result.Data["WantsTradedWeapon"] = true;

                    if (traderWeapon.Spawned) traderWeapon.DeSpawn();
                    testPawn.equipment?.AddEquipment(traderWeapon);

                    bool autoForced = ForcedWeapons.IsForced(testPawn, traderWeapon);
                    result.Data["TradedWeaponAutoForced"] = autoForced;

                    if (autoForced)
                    {
                        result.Data["Warning"] = "Traded weapon incorrectly marked as forced";
                    }
                }
            }

            if (testPawn.equipment?.Primary != null)
            {
                var currentWeapon = testPawn.equipment.Primary;

                testPawn.equipment.Remove(currentWeapon);
                TestHelpers.SafeDestroyWeapon(currentWeapon);

                result.Data["WeaponGifted"] = true;

                var job = jobGiver.TestTryGiveJob(testPawn);
                result.Data["LooksForNewWeaponAfterGift"] = job != null;
            }

            return result;
        }

        public void Cleanup()
        {
            ForcedWeapons.ClearForced(testPawn);
            TestHelpers.SafeDestroyPawn(testPawn);
            TestHelpers.SafeDestroyPawn(trader);
            TestHelpers.SafeDestroyWeapon(forcedWeapon);
            TestHelpers.SafeDestroyWeapon(tradeWeapon);
        }
    }

    /// <summary>
    /// MEDIUM PRIORITY: Test raid detection accuracy
    /// </summary>
    public class RaidDetectionTest : ITestScenario
    {
        public string Name => "Raid Detection and Response";
        private Pawn testPawn;
        private ThingWithComps testWeapon;
        private List<Pawn> raidPawns = new List<Pawn>();
        private bool originalRaidSetting;

        public void Setup(Map map)
        {
            if (map == null) return;

            originalRaidSetting = AutoArmMod.settings?.disableDuringRaids ?? false;

            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn != null)
            {
                testPawn.equipment?.DestroyAllEquipment();

                testWeapon = TestHelpers.CreateWeapon(map, AutoArmDefOf.Gun_AssaultRifle,
                    testPawn.Position + new IntVec3(3, 0, 0));
                if (testWeapon != null)
                {
                    WeaponCacheManager.AddWeaponToCache(testWeapon);
                }
            }
        }

        public TestResult Run()
        {
            if (testPawn == null || testPawn.Map == null)
                return TestResult.Failure("Test setup failed");

            var result = new TestResult { Success = true };
            var jobGiver = new JobGiver_PickUpBetterWeapon();

            AutoArmMod.settings.disableDuringRaids = true;

            bool noRaidActive = ModInit.IsLargeRaidActive;
            result.Data["InitialRaidStatus"] = noRaidActive;

            var job1 = jobGiver.TestTryGiveJob(testPawn);
            result.Data["JobWithNoRaid"] = job1 != null;

            try
            {
                var hostileFaction = Find.FactionManager.AllFactions
                    .FirstOrDefault(f => f.HostileTo(Faction.OfPlayer));

                if (hostileFaction != null)
                {
                    for (int i = 0; i < 22; i++)
                    {
                        var raiderPos = testPawn.Map.Center + new IntVec3(20 + i * 2, 0, 20);
                        var raider = TestHelpers.CreateTestPawn(testPawn.Map, new TestHelpers.TestPawnConfig
                        {
                            Name = $"Raider{i}",
                            Kind = PawnKindDefOf.Villager,
                            SpawnPosition = raiderPos
                        });

                        if (raider != null)
                        {
                            raider.SetFaction(hostileFaction);
                            raidPawns.Add(raider);
                        }
                    }

                    if (raidPawns.Count > 0)
                    {
                        var lordJob = new LordJob_AssaultColony(hostileFaction, false, false, false, false, false, false, false);
                        var lord = Verse.AI.Group.LordMaker.MakeNewLord(hostileFaction, lordJob, testPawn.Map, raidPawns);

                        result.Data["RaidLordCreated"] = lord != null;

                        bool raidDetected = ModInit.IsLargeRaidActive;
                        result.Data["RaidDetected"] = raidDetected;

                        var job2 = jobGiver.TestTryGiveJob(testPawn);
                        result.Data["JobDuringRaid"] = job2 != null;

                        if (job2 != null)
                        {
                            result.Success = false;
                            result.Data["Error1"] = "Job created during active raid!";
                        }

                        lord?.Cleanup();
                    }
                }
                else
                {
                    result.Data["NoHostileFaction"] = true;
                }
            }
            catch (Exception e)
            {
                result.Data["RaidSimulationError"] = e.Message;
            }

            foreach (var raider in raidPawns)
            {
                TestHelpers.SafeDestroyPawn(raider);
            }
            raidPawns.Clear();

            var lastCheckField = typeof(ModInit)
                .GetField("_lastRaidCheckTick", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            if (lastCheckField != null)
            {
                lastCheckField.SetValue(null, 0);
            }

            bool raidOver = !ModInit.IsLargeRaidActive;
            result.Data["RaidOver"] = raidOver;

            var job3 = jobGiver.TestTryGiveJob(testPawn);
            result.Data["JobAfterRaid"] = job3 != null;

            var friendlyFaction = Find.FactionManager.AllFactions
                .FirstOrDefault(f => f != Faction.OfPlayer && !f.HostileTo(Faction.OfPlayer));

            if (friendlyFaction != null)
            {
                var visitor = TestHelpers.CreateTestPawn(testPawn.Map, new TestHelpers.TestPawnConfig
                {
                    Name = "Visitor",
                    Kind = PawnKindDefOf.Villager
                });

                if (visitor != null)
                {
                    visitor.SetFaction(friendlyFaction);

                    var chillSpot = testPawn.Map.Center;
                    var visitJob = new LordJob_VisitColony(friendlyFaction, chillSpot);
                    var visitLord = Verse.AI.Group.LordMaker.MakeNewLord(friendlyFaction, visitJob, testPawn.Map, new List<Pawn> { visitor });

                    bool visitorTrigger = ModInit.IsLargeRaidActive;
                    result.Data["VisitorTriggersRaid"] = visitorTrigger;

                    if (visitorTrigger)
                    {
                        result.Success = false;
                        result.Data["Error2"] = "Friendly visitor incorrectly detected as raid!";
                    }

                    visitLord?.Cleanup();
                    TestHelpers.SafeDestroyPawn(visitor);
                }
            }

            return result;
        }

        public void Cleanup()
        {
            if (AutoArmMod.settings != null)
            {
                AutoArmMod.settings.disableDuringRaids = originalRaidSetting;
            }

            foreach (var raider in raidPawns)
            {
                if (raider != null && !raider.Destroyed)
                {
                    TestHelpers.SafeDestroyPawn(raider);
                }
            }
            raidPawns.Clear();

            TestHelpers.SafeDestroyPawn(testPawn);
            TestHelpers.SafeDestroyWeapon(testWeapon);
        }
    }

    public class CaravanTest : ITestScenario
    {
        public string Name => "Caravan Formation Weapon Handling";
        private Pawn testPawn;
        private ThingWithComps weapon;
        private ThingWithComps betterWeapon;

        public void Setup(Map map)
        {
            if (map == null) return;

            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn == null) return;

            var pistolDef = AutoArmDefOf.Gun_Autopistol;
            if (pistolDef != null)
            {
                weapon = TestHelpers.CreateWeapon(map, pistolDef, testPawn.Position);
                if (weapon != null)
                {
                    testPawn.equipment?.AddEquipment(weapon);
                }
            }

            var rifleDef = AutoArmDefOf.Gun_AssaultRifle;
            if (rifleDef != null)
            {
                betterWeapon = TestHelpers.CreateWeapon(map, rifleDef,
                    testPawn.Position + new IntVec3(1, 0, 0));
            }
        }

        public TestResult Run()
        {
            if (testPawn == null)
            {
                return TestResult.Failure("Test pawn not created");
            }

            var result = new TestResult { Success = true };

            TestRunnerFix.PreparePawnForTest(testPawn);
            JobGiver_PickUpBetterWeapon.EnableTestMode(true);
            var jobGiver = new JobGiver_PickUpBetterWeapon();
            var job = jobGiver.TestTryGiveJob(testPawn);
            JobGiver_PickUpBetterWeapon.EnableTestMode(false);

            result.Data["Normal behavior"] = job != null ? "Can upgrade" : "No upgrade";

            if (testPawn.mindState == null)
                testPawn.mindState = new Pawn_MindState(testPawn);

            var originalJob = testPawn.jobs?.curJob;

            if (testPawn.jobs != null)
            {
                var packJob = new Job(JobDefOf.HaulToContainer);
                testPawn.jobs.StartJob(packJob, JobCondition.InterruptForced);
            }

            TestRunnerFix.PreparePawnForTest(testPawn);
            JobGiver_PickUpBetterWeapon.EnableTestMode(true);
            job = jobGiver.TestTryGiveJob(testPawn);
            JobGiver_PickUpBetterWeapon.EnableTestMode(false);

            bool respectsCaravan = job == null || testPawn.jobs?.curJob?.def != JobDefOf.HaulToContainer;
            result.Data["During packing"] = respectsCaravan ? "Respects caravan job" : "May interrupt";

            if (testPawn.jobs != null)
            {
                testPawn.jobs.StopAll();
            }

            result.Data["Cache handling"] = "Would update on map transition";

            return result;
        }

        public void Cleanup()
        {
            TestHelpers.SafeDestroyWeapon(weapon);
            TestHelpers.SafeDestroyWeapon(betterWeapon);
            TestHelpers.SafeDestroyPawn(testPawn);
        }
    }

    public class IncapacitationTest : ITestScenario
    {
        public string Name => "Incapacitation During Equip";
        private Pawn testPawn;
        private ThingWithComps weapon;

        public void Setup(Map map)
        {
            if (map == null) return;

            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn == null) return;

            testPawn.equipment?.DestroyAllEquipment();

            var weaponDef = AutoArmDefOf.Gun_AssaultRifle;
            if (weaponDef != null)
            {
                weapon = TestHelpers.CreateWeapon(map, weaponDef,
                    testPawn.Position + new IntVec3(1, 0, 0));
            }
        }

        public TestResult Run()
        {
            if (testPawn == null)
            {
                return TestResult.Failure("Test pawn not created");
            }

            var result = new TestResult { Success = true };

            TestRunnerFix.PreparePawnForTest(testPawn);
            JobGiver_PickUpBetterWeapon.EnableTestMode(true);
            var jobGiver = new JobGiver_PickUpBetterWeapon();
            var job = jobGiver.TestTryGiveJob(testPawn);
            JobGiver_PickUpBetterWeapon.EnableTestMode(false);

            if (job == null)
            {
                var cur = testPawn.jobs?.curJob;
                if (cur == null)
                {
                    return TestResult.Failure("No equip job created");
                }
                string curName = cur.def?.defName ?? string.Empty;
                bool isEquipLike = cur.def == JobDefOf.Equip ||
                                   curName == "AutoArmSwapPrimary" ||
                                   curName == "AutoArmSwapSidearm" ||
                                   curName == "EquipSecondary" ||
                                   curName.IndexOf("SimpleSidearms", System.StringComparison.OrdinalIgnoreCase) >= 0;
                if (!isEquipLike)
                {
                    return TestResult.Failure($"No equip job created (curJob={curName})");
                }
                result.Data["Job created"] = "Yes (curJob)";
                job = cur;
            }

            result.Data["Job created"] = "Yes";

            if (testPawn.jobs != null)
            {
                testPawn.jobs.StartJob(job, JobCondition.None);
                result.Data["Job started"] = "Yes";
            }

            if (testPawn.health == null)
                testPawn.health = new Pawn_HealthTracker(testPawn);

            testPawn.health.capacities.Clear();
            result.Data["Pawn downed"] = testPawn.Downed ? "Yes" : "No";

            var afterCur = testPawn.jobs?.curJob;
            string afterName = afterCur?.def?.defName ?? string.Empty;
            bool stillEquipLike = afterCur != null && (afterCur.def == JobDefOf.Equip ||
                                   afterName == "AutoArmSwapPrimary" ||
                                   afterName == "AutoArmSwapSidearm" ||
                                   afterName == "EquipSecondary" ||
                                   afterName.IndexOf("SimpleSidearms", System.StringComparison.OrdinalIgnoreCase) >= 0);
            bool jobInterrupted = !stillEquipLike;
            result.Data["Job interrupted"] = jobInterrupted ? "Yes" : "No";

            bool weaponAccessible = weapon != null && weapon.Spawned && !weapon.Destroyed;
            result.Data["Weapon accessible"] = weaponAccessible ? "Yes" : "No";

            testPawn.health = new Pawn_HealthTracker(testPawn);
            TestRunnerFix.PreparePawnForTest(testPawn);

            JobGiver_PickUpBetterWeapon.EnableTestMode(true);
            var recoveryJob = jobGiver.TestTryGiveJob(testPawn);
            JobGiver_PickUpBetterWeapon.EnableTestMode(false);

            result.Data["Can retry after recovery"] = recoveryJob != null ? "Yes" : "No";

            bool blacklisted = WeaponBlacklist.IsBlacklisted(weapon, testPawn);
            if (blacklisted)
            {
                result.Success = false;
                result.Data["Blacklist error"] = "Weapon incorrectly blacklisted after incapacitation";
            }
            else
            {
                result.Data["Blacklist correct"] = "Not blacklisted";
            }

            return result;
        }

        public void Cleanup()
        {
            if (testPawn != null)
            {
                testPawn.health = new Pawn_HealthTracker(testPawn);
            }

            TestHelpers.SafeDestroyWeapon(weapon);
            TestHelpers.SafeDestroyPawn(testPawn);
        }
    }
}
