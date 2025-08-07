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
    /// CRITICAL: Test weapon duplication and hoarding prevention with SimpleSidearms
    /// </summary>
    public class SimpleSidearmsAntiDuplicationTest : ITestScenario
    {
        public string Name => "SimpleSidearms Anti-Duplication & Hoarding";
        private List<Pawn> testPawns = new List<Pawn>();
        private List<ThingWithComps> allWeapons = new List<ThingWithComps>();
        private Map testMap;

        public void Setup(Map map)
        {
            if (map == null || !SimpleSidearmsCompat.IsLoaded()) return;
            testMap = map;

            // Create multiple pawns with sidearms
            for (int i = 0; i < 3; i++)
            {
                var pawn = TestHelpers.CreateTestPawn(map, new TestHelpers.TestPawnConfig
                {
                    Name = $"HoarderPawn{i}",
                    SpawnPosition = map.Center + new IntVec3(i * 3, 0, 0)
                });

                if (pawn != null)
                {
                    // Give each pawn a primary and multiple sidearms
                    var primary = ThingMaker.MakeThing(VanillaWeaponDefOf.Gun_AssaultRifle) as ThingWithComps;
                    if (primary != null)
                    {
                        pawn.equipment?.AddEquipment(primary);
                        allWeapons.Add(primary);
                    }

                    // Add sidearms to inventory
                    for (int j = 0; j < 2; j++)
                    {
                        var sidearm = ThingMaker.MakeThing(VanillaWeaponDefOf.Gun_Autopistol) as ThingWithComps;
                        if (sidearm != null)
                        {
                            var comp = sidearm.TryGetComp<CompQuality>();
                            comp?.SetQuality(j == 0 ? QualityCategory.Normal : QualityCategory.Good, ArtGenerationContext.Colony);
                            pawn.inventory?.innerContainer?.TryAdd(sidearm);
                            allWeapons.Add(sidearm);
                        }
                    }
                    testPawns.Add(pawn);
                }
            }

            // Create upgrade weapons on ground
            for (int i = 0; i < 5; i++)
            {
                var quality = i < 2 ? QualityCategory.Excellent : QualityCategory.Masterwork;
                var pos = map.Center + new IntVec3(10 + i * 2, 0, 5);
                var weapon = TestHelpers.CreateWeapon(map, VanillaWeaponDefOf.Gun_Autopistol, pos, quality);
                if (weapon != null)
                {
                    allWeapons.Add(weapon);
                    ImprovedWeaponCacheManager.AddWeaponToCache(weapon);
                }
            }
        }

        public TestResult Run()
        {
            if (!SimpleSidearmsCompat.IsLoaded())
                return TestResult.Pass().WithData("Note", "SimpleSidearms not loaded");

            var result = new TestResult { Success = true };
            var jobGiver = new JobGiver_PickUpBetterWeapon();

            // Test 1: Count initial weapons
            int initialWeaponCount = CountAllWeapons();
            result.Data["InitialTotalWeapons"] = initialWeaponCount;

            // Test 2: Multiple swap jobs simultaneously
            var swapJobs = new List<Job>();
            foreach (var pawn in testPawns)
            {
                var job = SimpleSidearmsCompat.FindBestSidearmJob(pawn, 
                    (p, w) => WeaponScoringHelper.GetTotalScore(p, w), 60);
                if (job != null)
                {
                    swapJobs.Add(job);
                    result.Data[$"{pawn.Name}_SwapJob"] = job.def.defName;
                }
            }

            result.Data["SwapJobsCreated"] = swapJobs.Count;

            // Test 3: Execute swaps and check for duplication
            foreach (var pawn in testPawns)
            {
                if (pawn.CurJob?.def.defName == "AutoArmSwapSidearm")
                {
                    // Let the job execute
                    for (int i = 0; i < 10 && pawn.jobs?.curDriver != null; i++)
                    {
                        try
                        {
                            pawn.jobs.curDriver.DriverTick();
                        }
                        catch (Exception e)
                        {
                            result.Data[$"{pawn.Name}_SwapError"] = e.Message;
                        }
                    }
                }
            }

            // Test 4: Count weapons after swaps - should be same
            int afterSwapCount = CountAllWeapons();
            result.Data["WeaponsAfterSwaps"] = afterSwapCount;
            
            if (afterSwapCount != initialWeaponCount)
            {
                result.Success = false;
                result.Data["CRITICAL_ERROR"] = $"Weapon duplication detected! {initialWeaponCount} -> {afterSwapCount}";
            }

            // Test 5: Check for weapon hoarding (one pawn taking all weapons)
            foreach (var pawn in testPawns)
            {
                int pawnWeaponCount = CountPawnWeapons(pawn);
                result.Data[$"{pawn.Name}_WeaponCount"] = pawnWeaponCount;
                
                if (pawnWeaponCount > 5) // Primary + 4 sidearms max
                {
                    result.Success = false;
                    result.Data[$"{pawn.Name}_HOARDING"] = "Pawn has too many weapons!";
                }
            }

            // Test 6: Verify no weapons stuck in limbo
            int accountedWeapons = 0;
            foreach (var weapon in allWeapons)
            {
                if (weapon.Destroyed)
                {
                    result.Data[$"Destroyed_{weapon.GetUniqueLoadID()}"] = true;
                    continue;
                }
                
                bool found = false;
                // Check if in pawn equipment/inventory
                foreach (var pawn in testPawns)
                {
                    if (pawn.equipment?.Primary == weapon || 
                        (pawn.inventory?.innerContainer?.Contains(weapon) ?? false))
                    {
                        found = true;
                        accountedWeapons++;
                        break;
                    }
                }
                
                // Check if on ground
                if (!found && weapon.Spawned && weapon.Map == testMap)
                {
                    found = true;
                    accountedWeapons++;
                }
                
                if (!found && !weapon.Destroyed)
                {
                    result.Success = false;
                    result.Data[$"LIMBO_{weapon.Label}"] = $"Weapon lost! Spawned:{weapon.Spawned}, Map:{weapon.Map != null}";
                }
            }
            
            result.Data["AccountedWeapons"] = accountedWeapons;

            return result;
        }

        private int CountAllWeapons()
        {
            int count = 0;
            foreach (var weapon in allWeapons)
            {
                if (!weapon.Destroyed)
                    count++;
            }
            return count;
        }

        private int CountPawnWeapons(Pawn pawn)
        {
            int count = 0;
            if (pawn.equipment?.Primary != null)
                count++;
            if (pawn.inventory?.innerContainer != null)
            {
                foreach (Thing t in pawn.inventory.innerContainer)
                {
                    if (t is ThingWithComps && t.def.IsWeapon)
                        count++;
                }
            }
            return count;
        }

        public void Cleanup()
        {
            foreach (var pawn in testPawns)
            {
                pawn?.Destroy();
            }
            foreach (var weapon in allWeapons)
            {
                if (weapon != null && !weapon.Destroyed)
                    weapon.Destroy();
            }
            testPawns.Clear();
            allWeapons.Clear();
        }
    }

    /// <summary>
    /// CRITICAL: Test SimpleSidearms forced weapon upgrade blocking
    /// </summary>
    public class SimpleSidearmsForcedWeaponUpgradeTest : ITestScenario
    {
        public string Name => "SimpleSidearms Forced Weapon Upgrade Blocking";
        private Pawn testPawn;
        private ThingWithComps forcedWeapon;
        private ThingWithComps betterSameType;
        private ThingWithComps betterDifferentType;
        private bool originalSetting;

        public void Setup(Map map)
        {
            if (!SimpleSidearmsCompat.IsLoaded()) return;

            originalSetting = AutoArmMod.settings?.allowForcedWeaponUpgrades ?? false;
            
            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn != null)
            {
                // Create a poor quality weapon to force
                forcedWeapon = ThingMaker.MakeThing(VanillaWeaponDefOf.Gun_Autopistol) as ThingWithComps;
                if (forcedWeapon != null)
                {
                    var comp = forcedWeapon.TryGetComp<CompQuality>();
                    comp?.SetQuality(QualityCategory.Poor, ArtGenerationContext.Colony);
                    
                    // Add to inventory as sidearm
                    testPawn.inventory?.innerContainer?.TryAdd(forcedWeapon);
                    
                    // Tell SimpleSidearms to force this weapon type
                    SimpleSidearmsCompat.SetWeaponAsForced(testPawn, forcedWeapon);
                }

                // Create better version of same weapon
                betterSameType = TestHelpers.CreateWeapon(map, VanillaWeaponDefOf.Gun_Autopistol,
                    testPawn.Position + new IntVec3(2, 0, 0), QualityCategory.Legendary);
                    
                // Create better different weapon
                betterDifferentType = TestHelpers.CreateWeapon(map, VanillaWeaponDefOf.Gun_AssaultRifle,
                    testPawn.Position + new IntVec3(-2, 0, 0), QualityCategory.Masterwork);
                    
                if (betterSameType != null) ImprovedWeaponCacheManager.AddWeaponToCache(betterSameType);
                if (betterDifferentType != null) ImprovedWeaponCacheManager.AddWeaponToCache(betterDifferentType);
            }
        }

        public TestResult Run()
        {
            if (!SimpleSidearmsCompat.IsLoaded())
                return TestResult.Pass().WithData("Note", "SimpleSidearms not loaded");

            if (testPawn == null || forcedWeapon == null)
                return TestResult.Failure("Test setup failed");

            var result = new TestResult { Success = true };

            // Test 1: Verify weapon is forced in SimpleSidearms
            bool isForced, isPreferred;
            bool forcedStatus = SimpleSidearmsCompat.IsWeaponTypeForced(testPawn, forcedWeapon.def, out isForced, out isPreferred);
            result.Data["WeaponForced"] = isForced;
            result.Data["WeaponPreferred"] = isPreferred;

            // Test 2: Try to upgrade to different weapon type (should always block)
            AutoArmMod.settings.allowForcedWeaponUpgrades = false;
            bool shouldSkipDifferent = SimpleSidearmsCompat.ShouldSkipWeaponUpgrade(testPawn, forcedWeapon.def, betterDifferentType.def);
            result.Data["BlocksDifferentType_UpgradesOff"] = shouldSkipDifferent;
            
            AutoArmMod.settings.allowForcedWeaponUpgrades = true;
            bool shouldSkipDifferent2 = SimpleSidearmsCompat.ShouldSkipWeaponUpgrade(testPawn, forcedWeapon.def, betterDifferentType.def);
            result.Data["BlocksDifferentType_UpgradesOn"] = shouldSkipDifferent2;
            
            if (!shouldSkipDifferent || !shouldSkipDifferent2)
            {
                result.Success = false;
                result.Data["ERROR1"] = "Allows changing forced weapon type!";
            }

            // Test 3: Try to upgrade to same weapon type
            AutoArmMod.settings.allowForcedWeaponUpgrades = false;
            bool shouldSkipSame = SimpleSidearmsCompat.ShouldSkipWeaponUpgrade(testPawn, forcedWeapon.def, betterSameType.def);
            result.Data["BlocksSameType_UpgradesOff"] = shouldSkipSame;
            
            if (!shouldSkipSame)
            {
                result.Success = false;
                result.Data["ERROR2"] = "Allows upgrading forced weapon when disabled!";
            }
            
            AutoArmMod.settings.allowForcedWeaponUpgrades = true;
            bool shouldSkipSame2 = SimpleSidearmsCompat.ShouldSkipWeaponUpgrade(testPawn, forcedWeapon.def, betterSameType.def);
            result.Data["AllowsSameType_UpgradesOn"] = !shouldSkipSame2;
            
            if (shouldSkipSame2)
            {
                result.Success = false;
                result.Data["ERROR3"] = "Blocks forced weapon upgrade when allowed!";
            }

            // Test 4: Check sidearm job creation respects forcing
            AutoArmMod.settings.allowForcedWeaponUpgrades = false;
            var job1 = SimpleSidearmsCompat.FindBestSidearmJob(testPawn,
                (p, w) => WeaponScoringHelper.GetTotalScore(p, w), 60);
                
            if (job1 != null && job1.targetA.Thing == betterSameType)
            {
                result.Success = false;
                result.Data["ERROR4"] = "Created upgrade job for forced weapon when disabled!";
            }
            
            AutoArmMod.settings.allowForcedWeaponUpgrades = true;
            var job2 = SimpleSidearmsCompat.FindBestSidearmJob(testPawn,
                (p, w) => WeaponScoringHelper.GetTotalScore(p, w), 60);
                
            result.Data["CreatesUpgradeJob_WhenAllowed"] = job2 != null && job2.targetA.Thing == betterSameType;

            return result;
        }

        public void Cleanup()
        {
            AutoArmMod.settings.allowForcedWeaponUpgrades = originalSetting;
            testPawn?.Destroy();
            forcedWeapon?.Destroy();
            betterSameType?.Destroy();
            betterDifferentType?.Destroy();
        }
    }

    /// <summary>
    /// CRITICAL: Test SimpleSidearms concurrent weapon access
    /// </summary>
    public class SimpleSidearmsConcurrentAccessTest : ITestScenario
    {
        public string Name => "SimpleSidearms Concurrent Weapon Access";
        private List<Pawn> testPawns = new List<Pawn>();
        private ThingWithComps targetWeapon;
        private List<ThingWithComps> sidearms = new List<ThingWithComps>();

        public void Setup(Map map)
        {
            if (!SimpleSidearmsCompat.IsLoaded()) return;

            // Create 3 pawns with same sidearm type
            for (int i = 0; i < 3; i++)
            {
                var pawn = TestHelpers.CreateTestPawn(map, new TestHelpers.TestPawnConfig
                {
                    Name = $"ConcurrentPawn{i}",
                    SpawnPosition = map.Center + new IntVec3(i * 2, 0, 0)
                });
                
                if (pawn != null)
                {
                    // Give each a poor quality sidearm
                    var sidearm = ThingMaker.MakeThing(VanillaWeaponDefOf.Gun_Autopistol) as ThingWithComps;
                    if (sidearm != null)
                    {
                        var comp = sidearm.TryGetComp<CompQuality>();
                        comp?.SetQuality(QualityCategory.Poor, ArtGenerationContext.Colony);
                        pawn.inventory?.innerContainer?.TryAdd(sidearm);
                        sidearms.Add(sidearm);
                    }
                    testPawns.Add(pawn);
                }
            }

            // Create single excellent weapon they all want
            targetWeapon = TestHelpers.CreateWeapon(map, VanillaWeaponDefOf.Gun_Autopistol,
                map.Center + new IntVec3(3, 0, 3), QualityCategory.Legendary);
                
            if (targetWeapon != null)
            {
                ImprovedWeaponCacheManager.AddWeaponToCache(targetWeapon);
            }
        }

        public TestResult Run()
        {
            if (!SimpleSidearmsCompat.IsLoaded())
                return TestResult.Pass().WithData("Note", "SimpleSidearms not loaded");

            if (testPawns.Count < 2 || targetWeapon == null)
                return TestResult.Failure("Test setup failed");

            var result = new TestResult { Success = true };
            var jobs = new Dictionary<Pawn, Job>();

            // Test 1: All pawns create jobs simultaneously
            foreach (var pawn in testPawns)
            {
                var job = SimpleSidearmsCompat.FindBestSidearmJob(pawn,
                    (p, w) => WeaponScoringHelper.GetTotalScore(p, w), 60);
                    
                if (job != null)
                {
                    jobs[pawn] = job;
                    result.Data[$"{pawn.Name}_Job"] = job.def.defName;
                    result.Data[$"{pawn.Name}_Target"] = job.targetA.Thing?.Label ?? "null";
                }
            }

            result.Data["JobsCreated"] = jobs.Count;
            result.Data["AllTargetSame"] = jobs.Values.All(j => j.targetA.Thing == targetWeapon);

            // Test 2: First pawn reserves weapon
            if (jobs.Count > 0)
            {
                var firstPawn = jobs.Keys.First();
                var firstJob = jobs[firstPawn];
                
                bool reserved = firstPawn.Reserve(targetWeapon, firstJob);
                result.Data["FirstReserved"] = reserved;
                
                // Test 3: Other pawns should fail to reserve
                foreach (var pawn in testPawns.Skip(1))
                {
                    bool canReserve = pawn.CanReserve(targetWeapon);
                    result.Data[$"{pawn.Name}_CanReserve"] = canReserve;
                    
                    if (canReserve)
                    {
                        result.Success = false;
                        result.Data["ERROR"] = "Multiple pawns can reserve same sidearm upgrade!";
                    }
                }
            }

            // Test 4: Swap job execution with concurrent access
            if (jobs.Count > 0 && targetWeapon != null)
            {
                var firstPawn = jobs.Keys.First();
                
                // Start swap job
                if (firstPawn.jobs != null && jobs.ContainsKey(firstPawn))
                {
                    firstPawn.jobs.StartJob(jobs[firstPawn]);
                    
                    // Another pawn tries to take it mid-swap
                    if (testPawns.Count > 1)
                    {
                        var secondPawn = testPawns[1];
                        var hijackJob = SimpleSidearmsCompat.FindBestSidearmJob(secondPawn,
                            (p, w) => WeaponScoringHelper.GetTotalScore(p, w), 60);
                            
                        result.Data["HijackJobCreated"] = hijackJob != null;
                        
                        if (hijackJob != null && hijackJob.targetA.Thing == targetWeapon)
                        {
                            result.Success = false;
                            result.Data["ERROR2"] = "Can create job for weapon being swapped!";
                        }
                    }
                }
            }

            return result;
        }

        public void Cleanup()
        {
            foreach (var pawn in testPawns)
            {
                pawn?.jobs?.StopAll();
                pawn?.Destroy();
            }
            foreach (var sidearm in sidearms)
            {
                sidearm?.Destroy();
            }
            targetWeapon?.Destroy();
            testPawns.Clear();
            sidearms.Clear();
        }
    }

    /// <summary>
    /// CRITICAL: Test bonded weapon handling with SimpleSidearms
    /// </summary>
    public class SimpleSidearmsBondedWeaponTest : ITestScenario
    {
        public string Name => "SimpleSidearms Bonded Weapon Sync";
        private Pawn testPawn;
        private ThingWithComps bondedWeapon;
        private ThingWithComps normalWeapon;
        private ThingWithComps betterWeapon;
        private bool originalBondSetting;

        public void Setup(Map map)
        {
            if (!SimpleSidearmsCompat.IsLoaded()) return;

            originalBondSetting = AutoArmMod.settings?.respectWeaponBonds ?? true;
            AutoArmMod.settings.respectWeaponBonds = true;

            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn != null)
            {
                // Create bonded weapon
                bondedWeapon = ThingMaker.MakeThing(VanillaWeaponDefOf.Gun_AssaultRifle) as ThingWithComps;
                if (bondedWeapon != null)
                {
                    var biocomp = bondedWeapon.TryGetComp<CompBiocodable>();
                    if (biocomp != null)
                    {
                        biocomp.CodeFor(testPawn);
                    }
                    testPawn.equipment?.AddEquipment(bondedWeapon);
                }

                // Create normal sidearm
                normalWeapon = ThingMaker.MakeThing(VanillaWeaponDefOf.Gun_Autopistol) as ThingWithComps;
                if (normalWeapon != null)
                {
                    testPawn.inventory?.innerContainer?.TryAdd(normalWeapon);
                }

                // Create better weapon on ground
                betterWeapon = TestHelpers.CreateWeapon(map, VanillaWeaponDefOf.Gun_ChainShotgun,
                    testPawn.Position + new IntVec3(3, 0, 0), QualityCategory.Legendary);
                    
                if (betterWeapon != null)
                {
                    ImprovedWeaponCacheManager.AddWeaponToCache(betterWeapon);
                }
            }
        }

        public TestResult Run()
        {
            if (!SimpleSidearmsCompat.IsLoaded())
                return TestResult.Pass().WithData("Note", "SimpleSidearms not loaded");

            if (testPawn == null || bondedWeapon == null)
                return TestResult.Failure("Test setup failed");

            var result = new TestResult { Success = true };

            // Test 1: Verify bonded weapon is recognized
            var biocomp = bondedWeapon.TryGetComp<CompBiocodable>();
            result.Data["WeaponBonded"] = biocomp?.Biocoded ?? false;
            result.Data["BondedToPawn"] = biocomp?.CodedPawn == testPawn;

            // Test 2: Sync bonded weapon with SimpleSidearms
            SimpleSidearmsCompat.SyncBondedPrimaryWeapon(testPawn, bondedWeapon);
            
            bool isForced, isPreferred;
            bool forcedInSS = SimpleSidearmsCompat.IsWeaponTypeForced(testPawn, bondedWeapon.def, out isForced, out isPreferred);
            result.Data["BondedWeaponForcedInSS"] = isForced;
            
            if (!isForced && AutoArmMod.settings.respectWeaponBonds)
            {
                result.Data["Warning"] = "Bonded weapon not synced to SimpleSidearms forced status";
            }

            // Test 3: Try to replace bonded weapon
            var jobGiver = new JobGiver_PickUpBetterWeapon();
            var job = jobGiver.TestTryGiveJob(testPawn);
            
            if (job != null && job.targetA.Thing == betterWeapon)
            {
                result.Success = false;
                result.Data["ERROR1"] = "Trying to replace bonded weapon!";
            }

            // Test 4: Drop bonded weapon and check recovery
            testPawn.equipment.TryDropEquipment(bondedWeapon, out var dropped, testPawn.Position);
            if (dropped != null)
            {
                result.Data["BondedWeaponDropped"] = true;
                
                // Should immediately want to pick it back up
                var recoveryJob = jobGiver.TestTryGiveJob(testPawn);
                if (recoveryJob != null && recoveryJob.targetA.Thing == dropped)
                {
                    result.Data["TriesToRecoverBonded"] = true;
                }
                else
                {
                    result.Data["Warning2"] = "Not trying to recover dropped bonded weapon";
                }
                
                // Check if SimpleSidearms still considers it forced
                bool stillForced = SimpleSidearmsCompat.IsWeaponTypeForced(testPawn, bondedWeapon.def, out isForced, out isPreferred);
                result.Data["StillForcedAfterDrop"] = isForced;
            }

            // Test 5: Bonded weapon as sidearm
            if (dropped != null && dropped.Spawned)
            {
                dropped.DeSpawn();
                testPawn.inventory?.innerContainer?.TryAdd(dropped);
                
                // Check if SS recognizes bonded sidearm
                bool managedAsSidearm = SimpleSidearmsCompat.IsSimpleSidearmsManaged(testPawn, bondedWeapon);
                result.Data["BondedRecognizedAsSidearm"] = managedAsSidearm;
            }

            return result;
        }

        public void Cleanup()
        {
            AutoArmMod.settings.respectWeaponBonds = originalBondSetting;
            testPawn?.Destroy();
            bondedWeapon?.Destroy();
            normalWeapon?.Destroy();
            betterWeapon?.Destroy();
        }
    }
}
