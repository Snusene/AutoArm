using AutoArm.Caching;
using AutoArm.Definitions;
using AutoArm.Helpers;
using AutoArm.Jobs;
using AutoArm.Logging;
using AutoArm.Testing.Helpers;
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
                // Give pawn a weapon
                originalWeapon = ThingMaker.MakeThing(VanillaWeaponDefOf.Gun_AssaultRifle) as ThingWithComps;
                if (originalWeapon != null)
                {
                    testPawn.equipment?.DestroyAllEquipment();
                    testPawn.equipment?.AddEquipment(originalWeapon);
                    
                    // Mark as forced so we can test if it's retained
                    ForcedWeaponHelper.SetForced(testPawn, originalWeapon);
                }

                // Create a nearby weapon for potential pickup
                nearbyWeapon = TestHelpers.CreateWeapon(map, VanillaWeaponDefOf.Gun_Autopistol,
                    testPawn.Position + new IntVec3(3, 0, 0));
                if (nearbyWeapon != null)
                {
                    ImprovedWeaponCacheManager.AddWeaponToCache(nearbyWeapon);
                }
            }
        }

        public TestResult Run()
        {
            if (testPawn == null || originalWeapon == null)
                return TestResult.Failure("Test setup failed");

            var result = new TestResult { Success = true };
            var jobGiver = new JobGiver_PickUpBetterWeapon();

            // Test 1: Berserk mental break
            try
            {
                // Simulate berserk state
                if (testPawn.mindState == null)
                    testPawn.mindState = new Pawn_MindState(testPawn);
                
                var berserkDef = DefDatabase<MentalStateDef>.GetNamed("Berserk", false);
                if (berserkDef != null)
                {
                    testPawn.mindState.mentalStateHandler.TryStartMentalState(berserkDef, null, true);
                    result.Data["BerserkStarted"] = testPawn.InMentalState;
                    
                    // Check if pawn can get weapon job during mental break
                    var job = jobGiver.TestTryGiveJob(testPawn);
                    result.Data["JobDuringBerserk"] = job != null;
                    
                    if (job != null)
                    {
                        // Generally shouldn't create jobs during mental states
                        result.Data["Warning1"] = "Job created during berserk state";
                    }
                    
                    // End mental state
                    testPawn.mindState.mentalStateHandler.Reset();
                }
            }
            catch (Exception e)
            {
                result.Data["BerserkException"] = e.Message;
            }

            // Test 2: Tantrum destroying weapon
            if (testPawn.equipment?.Primary != null)
            {
                var currentWeapon = testPawn.equipment.Primary;
                result.Data["WeaponBeforeTantrum"] = currentWeapon.Label;
                
                // Simulate weapon destruction
                testPawn.equipment.DestroyEquipment(currentWeapon);
                result.Data["WeaponDestroyed"] = currentWeapon.Destroyed;
                
                // Check if pawn immediately tries to get new weapon
                var job = jobGiver.TestTryGiveJob(testPawn);
                result.Data["JobAfterWeaponDestroyed"] = job != null;
                
                if (job != null && nearbyWeapon != null && job.targetA.Thing == nearbyWeapon)
                {
                    result.Data["TargetsNearbyWeapon"] = true;
                }
            }

            // Test 3: Social fight weapon drop
            if (!originalWeapon.Destroyed)
            {
                // Re-equip for test
                if (originalWeapon.Spawned) originalWeapon.DeSpawn();
                testPawn.equipment?.AddEquipment(originalWeapon);
                
                // Simulate dropping weapon during social fight
                testPawn.equipment.TryDropEquipment(originalWeapon, out var dropped, testPawn.Position);
                
                if (dropped != null)
                {
                    result.Data["WeaponDropped"] = true;
                    
                    // Mark as recently dropped
                    DroppedItemTracker.MarkAsDropped(dropped, 600);
                    
                    // Should not immediately pick up
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

            // Test 4: Forced weapon retention after mental break
            if (!originalWeapon.Destroyed && originalWeapon.Spawned)
            {
                // Pick up the forced weapon again
                originalWeapon.DeSpawn();
                testPawn.equipment?.AddEquipment(originalWeapon);
                
                bool stillForced = ForcedWeaponHelper.IsForced(testPawn, originalWeapon);
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
            // Clear mental state
            testPawn?.mindState?.mentalStateHandler?.Reset();
            
            ForcedWeaponHelper.ClearForced(testPawn);
            testPawn?.Destroy();
            originalWeapon?.Destroy();
            nearbyWeapon?.Destroy();
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
                // Give pawn a forced weapon
                forcedWeapon = ThingMaker.MakeThing(VanillaWeaponDefOf.Gun_AssaultRifle) as ThingWithComps;
                if (forcedWeapon != null)
                {
                    testPawn.equipment?.DestroyAllEquipment();
                    testPawn.equipment?.AddEquipment(forcedWeapon);
                    ForcedWeaponHelper.SetForced(testPawn, forcedWeapon);
                }
            }

            // Create a trader pawn
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
                    
                    // Give trader a better weapon
                    tradeWeapon = ThingMaker.MakeThing(VanillaWeaponDefOf.Gun_ChainShotgun) as ThingWithComps;
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

            // Test 1: Forced weapon should not be auto-sold
            result.Data["WeaponIsForced"] = ForcedWeaponHelper.IsForced(testPawn, forcedWeapon);
            result.Data["ForcedWeaponDef"] = forcedWeapon.def.defName;

            // Simulate selling the forced weapon
            if (testPawn.equipment?.Primary == forcedWeapon)
            {
                // Remove from equipment (simulating sale)
                testPawn.equipment.Remove(forcedWeapon);
                
                // Check if forced status is cleared
                bool stillForced = ForcedWeaponHelper.IsForced(testPawn, null);
                result.Data["ForcedStatusAfterSale"] = stillForced;
                
                // Spawn weapon on ground (simulating it's still available to buy back)
                GenSpawn.Spawn(forcedWeapon, testPawn.Position + new IntVec3(5, 0, 0), testPawn.Map);
                ImprovedWeaponCacheManager.AddWeaponToCache(forcedWeapon);
                
                // Check if pawn tries to re-acquire it
                var job = jobGiver.TestTryGiveJob(testPawn);
                if (job != null && job.targetA.Thing == forcedWeapon)
                {
                    result.Data["TriesToReacquireSoldWeapon"] = true;
                }
            }

            // Test 2: Trading for better weapon
            if (trader?.equipment?.Primary != null)
            {
                var traderWeapon = trader.equipment.Primary;
                result.Data["TraderWeapon"] = traderWeapon.Label;
                
                // Simulate trade completion - trader weapon becomes available
                trader.equipment.Remove(traderWeapon);
                GenSpawn.Spawn(traderWeapon, testPawn.Position + new IntVec3(2, 0, 0), testPawn.Map);
                ImprovedWeaponCacheManager.AddWeaponToCache(traderWeapon);
                
                // Check if pawn wants the traded weapon
                var job = jobGiver.TestTryGiveJob(testPawn);
                if (job != null && job.targetA.Thing == traderWeapon)
                {
                    result.Data["WantsTradedWeapon"] = true;
                    
                    // Simulate equipping it
                    if (traderWeapon.Spawned) traderWeapon.DeSpawn();
                    testPawn.equipment?.AddEquipment(traderWeapon);
                    
                    // Check if it becomes forced
                    bool autoForced = ForcedWeaponHelper.IsForced(testPawn, traderWeapon);
                    result.Data["TradedWeaponAutoForced"] = autoForced;
                    
                    if (autoForced)
                    {
                        result.Data["Warning"] = "Traded weapon incorrectly marked as forced";
                    }
                }
            }

            // Test 3: Gifting equipped weapon
            if (testPawn.equipment?.Primary != null)
            {
                var currentWeapon = testPawn.equipment.Primary;
                
                // Simulate gifting (remove without dropping)
                testPawn.equipment.Remove(currentWeapon);
                currentWeapon.Destroy(); // Gifted away
                
                result.Data["WeaponGifted"] = true;
                
                // Should look for new weapon
                var job = jobGiver.TestTryGiveJob(testPawn);
                result.Data["LooksForNewWeaponAfterGift"] = job != null;
            }

            return result;
        }

        public void Cleanup()
        {
            ForcedWeaponHelper.ClearForced(testPawn);
            testPawn?.Destroy();
            trader?.Destroy();
            forcedWeapon?.Destroy();
            tradeWeapon?.Destroy();
        }
    }

    /// <summary>
    /// MEDIUM PRIORITY: Test settings changes during gameplay
    /// </summary>
    public class SettingsChangeTest : ITestScenario
    {
        public string Name => "Settings Changes During Gameplay";
        private Pawn testPawn;
        private ThingWithComps weapon1;
        private ThingWithComps weapon2;
        private bool originalModEnabled;
        private float originalThreshold;
        private bool originalAllowChildren;

        public void Setup(Map map)
        {
            if (map == null) return;

            // Store original settings
            originalModEnabled = AutoArmMod.settings?.modEnabled ?? true;
            originalThreshold = AutoArmMod.settings?.weaponUpgradeThreshold ?? 1.15f;
            originalAllowChildren = AutoArmMod.settings?.allowChildrenToEquipWeapons ?? false;

            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn != null)
            {
                testPawn.equipment?.DestroyAllEquipment();

                // Create weapons with different qualities
                weapon1 = TestHelpers.CreateWeapon(map, VanillaWeaponDefOf.Gun_Autopistol,
                    testPawn.Position + new IntVec3(2, 0, 0), QualityCategory.Normal);
                weapon2 = TestHelpers.CreateWeapon(map, VanillaWeaponDefOf.Gun_Autopistol,
                    testPawn.Position + new IntVec3(-2, 0, 0), QualityCategory.Good);
                    
                if (weapon1 != null) ImprovedWeaponCacheManager.AddWeaponToCache(weapon1);
                if (weapon2 != null) ImprovedWeaponCacheManager.AddWeaponToCache(weapon2);
            }
        }

        public TestResult Run()
        {
            if (testPawn == null)
                return TestResult.Failure("Test setup failed");

            var result = new TestResult { Success = true };
            var jobGiver = new JobGiver_PickUpBetterWeapon();

            // Test 1: Disable mod entirely
            AutoArmMod.settings.modEnabled = false;
            var job1 = jobGiver.TestTryGiveJob(testPawn);
            result.Data["JobWithModDisabled"] = job1 != null;
            
            if (job1 != null)
            {
                result.Success = false;
                result.Data["Error1"] = "Job created with mod disabled!";
            }

            // Test 2: Re-enable mod
            AutoArmMod.settings.modEnabled = true;
            var job2 = jobGiver.TestTryGiveJob(testPawn);
            result.Data["JobWithModEnabled"] = job2 != null;

            // Test 3: Change upgrade threshold
            if (weapon1 != null && weapon2 != null)
            {
                // Equip weapon1
                if (weapon1.Spawned) weapon1.DeSpawn();
                testPawn.equipment?.AddEquipment(weapon1);
                
                float score1 = jobGiver.GetWeaponScore(testPawn, weapon1);
                float score2 = jobGiver.GetWeaponScore(testPawn, weapon2);
                float ratio = score2 / score1;
                
                result.Data["Score1"] = score1;
                result.Data["Score2"] = score2;
                result.Data["Ratio"] = ratio;
                
                // Set threshold very high (no upgrades)
                AutoArmMod.settings.weaponUpgradeThreshold = 2.0f;
                var job3 = jobGiver.TestTryGiveJob(testPawn);
                result.Data["JobWithHighThreshold"] = job3 != null;
                
                // Set threshold very low (any upgrade)
                AutoArmMod.settings.weaponUpgradeThreshold = 1.01f;
                
                // Clear any score cache that might exist
                WeaponScoreCache.ClearAllCaches();
                
                var job4 = jobGiver.TestTryGiveJob(testPawn);
                result.Data["JobWithLowThreshold"] = job4 != null;
                
                if (ratio > 1.01f && job4 == null)
                {
                    result.Data["Warning"] = "Expected job with low threshold but none created";
                }
            }

            // Test 4: Settings persistence after cache clear
            AutoArmMod.settings.weaponUpgradeThreshold = 1.5f;
            
            // Clear all caches
            WeaponScoreCache.ClearAllCaches();
            ImprovedWeaponCacheManager.InvalidateCache(testPawn.Map);
            
            // Settings should still be 1.5f
            float currentThreshold = AutoArmMod.settings.weaponUpgradeThreshold;
            result.Data["ThresholdAfterCacheClear"] = currentThreshold;
            
            if (Math.Abs(currentThreshold - 1.5f) > 0.01f)
            {
                result.Success = false;
                result.Data["Error2"] = $"Settings not persistent after cache clear: {currentThreshold}";
            }

            // Test 5: Child settings change
            if (ModsConfig.BiotechActive)
            {
                // Create a child pawn
                var childPawn = TestHelpers.CreateTestPawn(testPawn.Map, new TestHelpers.TestPawnConfig
                {
                    Name = "TestChild",
                    BiologicalAge = 10
                });
                
                if (childPawn != null)
                {
                    AutoArmMod.settings.allowChildrenToEquipWeapons = false;
                    var childJob1 = jobGiver.TestTryGiveJob(childPawn);
                    result.Data["ChildJobWhenDisabled"] = childJob1 != null;
                    
                    AutoArmMod.settings.allowChildrenToEquipWeapons = true;
                    AutoArmMod.settings.childrenMinAge = 8;
                    var childJob2 = jobGiver.TestTryGiveJob(childPawn);
                    result.Data["ChildJobWhenEnabled"] = childJob2 != null;
                    
                    childPawn.Destroy();
                }
            }

            return result;
        }

        public void Cleanup()
        {
            // Restore original settings
            if (AutoArmMod.settings != null)
            {
                AutoArmMod.settings.modEnabled = originalModEnabled;
                AutoArmMod.settings.weaponUpgradeThreshold = originalThreshold;
                AutoArmMod.settings.allowChildrenToEquipWeapons = originalAllowChildren;
            }

            testPawn?.Destroy();
            weapon1?.Destroy();
            weapon2?.Destroy();
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
                
                testWeapon = TestHelpers.CreateWeapon(map, VanillaWeaponDefOf.Gun_AssaultRifle,
                    testPawn.Position + new IntVec3(3, 0, 0));
                if (testWeapon != null)
                {
                    ImprovedWeaponCacheManager.AddWeaponToCache(testWeapon);
                }
            }
        }

        public TestResult Run()
        {
            if (testPawn == null || testPawn.Map == null)
                return TestResult.Failure("Test setup failed");

            var result = new TestResult { Success = true };
            var jobGiver = new JobGiver_PickUpBetterWeapon();

            // Enable raid detection
            AutoArmMod.settings.disableDuringRaids = true;

            // Test 1: No raid - should create jobs
            bool noRaidActive = ModInit.IsLargeRaidActive;
            result.Data["InitialRaidStatus"] = noRaidActive;
            
            var job1 = jobGiver.TestTryGiveJob(testPawn);
            result.Data["JobWithNoRaid"] = job1 != null;

            // Test 2: Simulate a raid
            try
            {
                var hostileFaction = Find.FactionManager.AllFactions
                    .FirstOrDefault(f => f.HostileTo(Faction.OfPlayer));
                    
                if (hostileFaction != null)
                {
                    // Create raid pawns (need 20+ for new system)
                    for (int i = 0; i < 22; i++) // Create 22 to ensure we exceed threshold
                    {
                        var raiderPos = testPawn.Map.Center + new IntVec3(20 + i * 2, 0, 20);
                        var raider = TestHelpers.CreateTestPawn(testPawn.Map, new TestHelpers.TestPawnConfig
                        {
                            Name = $"Raider{i}",
                            Kind = PawnKindDefOf.Villager, // Changed from Mercenary which doesn't exist
                            SpawnPosition = raiderPos
                        });
                        
                        if (raider != null)
                        {
                            raider.SetFaction(hostileFaction);
                            raidPawns.Add(raider);
                        }
                    }

                    // Create a raid lord
                    if (raidPawns.Count > 0)
                    {
                        var lordJob = new LordJob_AssaultColony(hostileFaction, false, false, false, false, false, false, false);
                        var lord = Verse.AI.Group.LordMaker.MakeNewLord(hostileFaction, lordJob, testPawn.Map, raidPawns);
                        
                        result.Data["RaidLordCreated"] = lord != null;
                        
                        // Check raid detection
                        bool raidDetected = ModInit.IsLargeRaidActive;
                        result.Data["RaidDetected"] = raidDetected;
                        
                        // Should not create job during raid
                        var job2 = jobGiver.TestTryGiveJob(testPawn);
                        result.Data["JobDuringRaid"] = job2 != null;
                        
                        if (job2 != null)
                        {
                            result.Success = false;
                            result.Data["Error1"] = "Job created during active raid!";
                        }
                        
                        // Clean up lord
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

            // Test 3: Raid ends - should resume
            foreach (var raider in raidPawns)
            {
                raider?.Destroy();
            }
            raidPawns.Clear();
            
            // Force the raid detection to update (it checks every 15 seconds)
            // We can force it by setting the tick counter back
            var lastCheckField = typeof(ModInit)
                .GetField("_lastRaidCheckTick", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            if (lastCheckField != null)
            {
                lastCheckField.SetValue(null, 0); // Force recheck on next access
            }
            
            bool raidOver = !ModInit.IsLargeRaidActive;
            result.Data["RaidOver"] = raidOver;
            
            var job3 = jobGiver.TestTryGiveJob(testPawn);
            result.Data["JobAfterRaid"] = job3 != null;

            // Test 4: Friendly visitors shouldn't trigger raid detection
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
                    
                    // Create visitor lord
                    var chillSpot = testPawn.Map.Center; // Add required chillSpot parameter
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
                    visitor.Destroy();
                }
            }

            return result;
        }

        public void Cleanup()
        {
            // Restore setting
            if (AutoArmMod.settings != null)
            {
                AutoArmMod.settings.disableDuringRaids = originalRaidSetting;
            }

            // Clean up any remaining raiders
            foreach (var raider in raidPawns)
            {
                if (raider != null && !raider.Destroyed)
                {
                    raider.Destroy();
                }
            }
            raidPawns.Clear();

            testPawn?.Destroy();
            testWeapon?.Destroy();
        }
    }
}
