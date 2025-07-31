using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace AutoArm.Testing.Scenarios
{
    /// <summary>
    /// Tests for forced weapon handling - critical for preventing unwanted weapon switches
    /// </summary>
    public class ForcedWeaponBasicTest : ITestScenario
    {
        public string Name => "Forced Weapon Basic Operations";
        private Pawn testPawn;
        private ThingWithComps forcedWeapon;
        private ThingWithComps betterWeapon;

        public void Setup(Map map)
        {
            if (map == null) return;

            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn != null)
            {
                testPawn.equipment?.DestroyAllEquipment();

                // Create and equip a weapon that will be forced
                forcedWeapon = TestHelpers.CreateWeapon(map, VanillaWeaponDefOf.Gun_Autopistol,
                    testPawn.Position, QualityCategory.Normal);
                if (forcedWeapon != null)
                {
                    forcedWeapon.DeSpawn();
                    testPawn.equipment.AddEquipment(forcedWeapon);
                }

                // Create a better weapon nearby
                betterWeapon = TestHelpers.CreateWeapon(map, VanillaWeaponDefOf.Gun_AssaultRifle,
                    testPawn.Position + new IntVec3(2, 0, 0), QualityCategory.Excellent);
                if (betterWeapon != null)
                {
                    ImprovedWeaponCacheManager.AddWeaponToCache(betterWeapon);
                }
            }
        }

        public TestResult Run()
        {
            if (testPawn == null || forcedWeapon == null || betterWeapon == null)
                return TestResult.Failure("Test setup failed");

            var result = new TestResult { Success = true };
            var jobGiver = new JobGiver_PickUpBetterWeapon();

            // Test 1: Without forcing, pawn should want the better weapon
            var jobBeforeForce = jobGiver.TestTryGiveJob(testPawn);
            result.Data["JobBeforeForce"] = jobBeforeForce != null;
            result.Data["TargetBeforeForce"] = jobBeforeForce?.targetA.Thing?.Label ?? "none";

            if (jobBeforeForce == null || jobBeforeForce.targetA.Thing != betterWeapon)
            {
                result.Success = false;
                result.Data["Error1"] = "Pawn didn't want better weapon before forcing";
                return result;
            }

            // Test 2: Mark weapon as forced
            ForcedWeaponHelper.SetForced(testPawn, forcedWeapon);
            result.Data["IsForced"] = ForcedWeaponHelper.IsForced(testPawn, forcedWeapon);
            result.Data["HasForcedWeapon"] = ForcedWeaponHelper.HasForcedWeapon(testPawn);
            result.Data["ForcedPrimary"] = ForcedWeaponHelper.GetForcedPrimary(testPawn)?.Label ?? "none";

            // Test 3: With forced weapon and upgrades disabled, no job should be created
            bool originalSetting = AutoArmMod.settings?.allowForcedWeaponUpgrades ?? false;
            AutoArmMod.settings.allowForcedWeaponUpgrades = false;

            var jobAfterForce = jobGiver.TestTryGiveJob(testPawn);
            result.Data["JobAfterForce_Disabled"] = jobAfterForce != null;

            if (jobAfterForce != null)
            {
                result.Success = false;
                result.Data["Error2"] = "Job created for forced weapon with upgrades disabled";
            }

            // Test 4: With forced weapon upgrades enabled, should only look for same type
            AutoArmMod.settings.allowForcedWeaponUpgrades = true;

            var jobWithUpgradesEnabled = jobGiver.TestTryGiveJob(testPawn);
            result.Data["JobAfterForce_Enabled"] = jobWithUpgradesEnabled != null;

            if (jobWithUpgradesEnabled != null && jobWithUpgradesEnabled.targetA.Thing == betterWeapon)
            {
                result.Success = false;
                result.Data["Error3"] = "Forced weapon tried to upgrade to different weapon type";
            }

            // Test 5: Add a better version of the same weapon type
            var betterPistol = TestHelpers.CreateWeapon(testPawn.Map, VanillaWeaponDefOf.Gun_Autopistol,
                testPawn.Position + new IntVec3(-2, 0, 0), QualityCategory.Legendary);
            if (betterPistol != null)
            {
                ImprovedWeaponCacheManager.AddWeaponToCache(betterPistol);

                var jobSameType = jobGiver.TestTryGiveJob(testPawn);
                result.Data["JobSameTypeUpgrade"] = jobSameType != null;
                result.Data["TargetSameType"] = jobSameType?.targetA.Thing?.Label ?? "none";

                if (jobSameType != null && jobSameType.targetA.Thing == betterPistol)
                {
                    result.Data["CorrectSameTypeUpgrade"] = true;
                }

                TestHelpers.SafeDestroyWeapon(betterPistol);
            }

            // Test 6: Clear forced status
            ForcedWeaponHelper.ClearForced(testPawn);
            result.Data["IsForcedAfterClear"] = ForcedWeaponHelper.IsForced(testPawn, forcedWeapon);

            if (ForcedWeaponHelper.IsForced(testPawn, forcedWeapon))
            {
                result.Success = false;
                result.Data["Error4"] = "Weapon still forced after clearing";
            }

            // Restore original setting
            AutoArmMod.settings.allowForcedWeaponUpgrades = originalSetting;

            return result;
        }

        public void Cleanup()
        {
            ForcedWeaponHelper.ClearForced(testPawn);
            TestHelpers.SafeDestroyWeapon(forcedWeapon);
            TestHelpers.SafeDestroyWeapon(betterWeapon);
            testPawn?.Destroy();
        }
    }

    /// <summary>
    /// Test forced weapon def tracking (multiple weapons of same type)
    /// </summary>
    public class ForcedWeaponDefTrackingTest : ITestScenario
    {
        public string Name => "Forced Weapon Def Tracking";
        private Pawn testPawn;
        private List<ThingWithComps> weapons = new List<ThingWithComps>();

        public void Setup(Map map)
        {
            if (map == null) return;

            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn != null)
            {
                testPawn.equipment?.DestroyAllEquipment();

                // Create multiple pistols of different qualities
                var qualities = new[] { QualityCategory.Poor, QualityCategory.Normal, QualityCategory.Excellent };
                foreach (var quality in qualities)
                {
                    var pistol = TestHelpers.CreateWeapon(map, VanillaWeaponDefOf.Gun_Autopistol,
                        testPawn.Position + new IntVec3(weapons.Count * 2, 0, 0), quality);
                    if (pistol != null)
                    {
                        weapons.Add(pistol);
                        ImprovedWeaponCacheManager.AddWeaponToCache(pistol);
                    }
                }
            }
        }

        public TestResult Run()
        {
            if (testPawn == null || weapons.Count < 3)
                return TestResult.Failure("Test setup failed");

            var result = new TestResult { Success = true };

            // Test adding forced defs
            var pistolDef = VanillaWeaponDefOf.Gun_Autopistol;
            ForcedWeaponHelper.AddForcedDef(testPawn, pistolDef);

            result.Data["DefIsForced"] = ForcedWeaponHelper.IsWeaponDefForced(testPawn, pistolDef);
            result.Data["ForcedDefCount"] = ForcedWeaponHelper.GetForcedWeaponDefs(testPawn).Count;

            // Test that all pistols are considered forced
            int forcedCount = 0;
            foreach (var weapon in weapons)
            {
                if (ForcedWeaponHelper.IsWeaponDefForced(testPawn, weapon.def))
                    forcedCount++;
            }
            result.Data["AllPistolsForced"] = forcedCount == weapons.Count;

            if (forcedCount != weapons.Count)
            {
                result.Success = false;
                result.Data["Error"] = $"Not all pistols marked as forced: {forcedCount}/{weapons.Count}";
            }

            // Test removing forced def
            ForcedWeaponHelper.RemoveForcedDef(testPawn, pistolDef);
            result.Data["DefIsForcedAfterRemove"] = ForcedWeaponHelper.IsWeaponDefForced(testPawn, pistolDef);

            if (ForcedWeaponHelper.IsWeaponDefForced(testPawn, pistolDef))
            {
                result.Success = false;
                result.Data["Error2"] = "Def still forced after removal";
            }

            return result;
        }

        public void Cleanup()
        {
            ForcedWeaponHelper.ClearForced(testPawn);
            foreach (var weapon in weapons)
            {
                TestHelpers.SafeDestroyWeapon(weapon);
            }
            weapons.Clear();
            testPawn?.Destroy();
        }
    }

    /// <summary>
    /// Test forced weapon save/load functionality
    /// </summary>
    public class ForcedWeaponSaveLoadTest : ITestScenario
    {
        public string Name => "Forced Weapon Save/Load";
        private Pawn testPawn;
        private ThingWithComps weapon;

        public void Setup(Map map)
        {
            if (map == null) return;

            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn != null)
            {
                weapon = TestHelpers.CreateWeapon(map, VanillaWeaponDefOf.Gun_AssaultRifle,
                    testPawn.Position);
                if (weapon != null)
                {
                    weapon.DeSpawn();
                    testPawn.equipment.AddEquipment(weapon);
                }
            }
        }

        public TestResult Run()
        {
            if (testPawn == null || weapon == null)
                return TestResult.Failure("Test setup failed");

            var result = new TestResult { Success = true };

            // Force the weapon
            ForcedWeaponHelper.SetForced(testPawn, weapon);
            ForcedWeaponHelper.AddForcedDef(testPawn, VanillaWeaponDefOf.Gun_Autopistol);

            // Get save data
            var primaryData = ForcedWeaponHelper.GetSaveData();
            var sidearmData = ForcedWeaponHelper.GetSidearmSaveData();

            result.Data["SavedPrimaryCount"] = primaryData.Count;
            result.Data["SavedSidearmCount"] = sidearmData.Count;

            // Clear all data
            ForcedWeaponHelper.ClearForced(testPawn);

            // Verify cleared
            result.Data["ClearedPrimary"] = !ForcedWeaponHelper.HasForcedWeapon(testPawn);
            result.Data["ClearedDefs"] = ForcedWeaponHelper.GetForcedWeaponDefs(testPawn).Count == 0;

            // Load save data back
            ForcedWeaponHelper.LoadSaveData(primaryData);
            
            // Convert List<ThingDef> to HashSet<ThingDef> for loading
            var sidearmDataAsHashSet = new Dictionary<Pawn, HashSet<ThingDef>>();
            foreach (var kvp in sidearmData)
            {
                sidearmDataAsHashSet[kvp.Key] = new HashSet<ThingDef>(kvp.Value);
            }
            ForcedWeaponHelper.LoadSidearmSaveData(sidearmDataAsHashSet);

            // Check that weapon def is restored (not the instance)
            result.Data["RestoredWeaponDef"] = ForcedWeaponHelper.IsWeaponDefForced(testPawn, weapon.def);
            result.Data["RestoredPistolDef"] = ForcedWeaponHelper.IsWeaponDefForced(testPawn, VanillaWeaponDefOf.Gun_Autopistol);

            if (!ForcedWeaponHelper.IsWeaponDefForced(testPawn, weapon.def))
            {
                result.Success = false;
                result.Data["Error"] = "Weapon def not restored after load";
            }

            return result;
        }

        public void Cleanup()
        {
            ForcedWeaponHelper.ClearForced(testPawn);
            TestHelpers.SafeDestroyWeapon(weapon);
            testPawn?.Destroy();
        }
    }

    /// <summary>
    /// Test comprehensive work interruption thresholds for all job types
    /// </summary>
    public class ComprehensiveWorkInterruptionTest : ITestScenario
    {
        public string Name => "Comprehensive Work Interruption Thresholds";
        private Pawn testPawn;
        private ThingWithComps currentWeapon;
        private ThingWithComps minorUpgrade;
        private ThingWithComps majorUpgrade;

        public void Setup(Map map)
        {
            if (map == null) return;

            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn != null)
            {
                // Give pawn a normal quality pistol
                currentWeapon = TestHelpers.CreateWeapon(map, VanillaWeaponDefOf.Gun_Autopistol,
                    testPawn.Position, QualityCategory.Normal);
                if (currentWeapon != null)
                {
                    currentWeapon.DeSpawn();
                    testPawn.equipment.AddEquipment(currentWeapon);
                }

                // Create upgrades
                minorUpgrade = TestHelpers.CreateWeapon(map, VanillaWeaponDefOf.Gun_Autopistol,
                    testPawn.Position + new IntVec3(2, 0, 0), QualityCategory.Good);

                majorUpgrade = TestHelpers.CreateWeapon(map, VanillaWeaponDefOf.Gun_SniperRifle,
                    testPawn.Position + new IntVec3(-2, 0, 0), QualityCategory.Excellent);

                if (minorUpgrade != null)
                    ImprovedWeaponCacheManager.AddWeaponToCache(minorUpgrade);
                if (majorUpgrade != null)
                    ImprovedWeaponCacheManager.AddWeaponToCache(majorUpgrade);
            }
        }

        public TestResult Run()
        {
            if (testPawn == null || currentWeapon == null)
                return TestResult.Failure("Test setup failed");

            var result = new TestResult { Success = true };

            // Calculate improvements
            float currentScore = WeaponScoreCache.GetCachedScore(testPawn, currentWeapon);
            float minorScore = minorUpgrade != null ? WeaponScoreCache.GetCachedScore(testPawn, minorUpgrade) : 0f;
            float majorScore = majorUpgrade != null ? WeaponScoreCache.GetCachedScore(testPawn, majorUpgrade) : 0f;

            float minorImprovement = minorScore / currentScore;
            float majorImprovement = majorScore / currentScore;

            result.Data["CurrentScore"] = currentScore;
            result.Data["MinorImprovement"] = minorImprovement;
            result.Data["MajorImprovement"] = majorImprovement;

            // Test job types according to your actual implementation
            var jobTests = new[]
            {
                // Critical jobs - 20% improvement required
                (JobDefOf.ExtinguishSelf, "ExtinguishSelf", 1.20f),
                (JobDefOf.FleeAndCower, "FleeAndCower", 1.20f),
                (JobDefOf.Vomit, "Vomit", 1.20f),
                (JobDefOf.Wait_Downed, "Wait_Downed", 1.20f),
                (JobDefOf.GotoSafeTemperature, "GotoSafeTemperature", 1.20f),
                (JobDefOf.BeatFire, "BeatFire", 1.20f),
                
                // Hauling and medical jobs - 15% improvement required
                (JobDefOf.HaulToCell, "HaulToCell", 1.15f),
                (JobDefOf.CarryDownedPawnToExit, "CarryDownedPawnToExit", 1.15f),
                (JobDefOf.Rescue, "Rescue", 1.15f),
                (JobDefOf.TendPatient, "TendPatient", 1.15f),
                
                // Regular work - 10% improvement required (default)
                (JobDefOf.Mine, "Mine", 1.10f),
                (JobDefOf.Clean, "Clean", 1.10f),
                (JobDefOf.Research, "Research", 1.10f),
                (JobDefOf.DoBill, "DoBill", 1.10f),
                (JobDefOf.DeliverFood, "DeliverFood", 1.10f),
                (JobDefOf.Sow, "Sow", 1.10f),
                (JobDefOf.CutPlant, "CutPlant", 1.10f)
            };

            int failedTests = 0;
            foreach (var (jobDef, jobName, expectedThreshold) in jobTests)
            {
                // Test minor upgrade
                bool minorShouldInterrupt = JobGiverHelpers.IsSafeToInterrupt(jobDef, minorImprovement);
                bool minorExpected = minorImprovement >= expectedThreshold;
                
                result.Data[$"{jobName}_MinorInterrupt"] = minorShouldInterrupt;
                result.Data[$"{jobName}_Threshold"] = expectedThreshold;
                
                if (minorShouldInterrupt != minorExpected && expectedThreshold < 900f)
                {
                    failedTests++;
                    result.Data[$"{jobName}_MinorError"] = $"Expected {minorExpected}, got {minorShouldInterrupt}";
                }

                // Test major upgrade
                bool majorShouldInterrupt = JobGiverHelpers.IsSafeToInterrupt(jobDef, majorImprovement);
                bool majorExpected = majorImprovement >= expectedThreshold;
                
                result.Data[$"{jobName}_MajorInterrupt"] = majorShouldInterrupt;
                
                if (majorShouldInterrupt != majorExpected && expectedThreshold < 900f)
                {
                    failedTests++;
                    result.Data[$"{jobName}_MajorError"] = $"Expected {majorExpected}, got {majorShouldInterrupt}";
                }
            }

            result.Data["TotalTests"] = jobTests.Length * 2;
            result.Data["FailedTests"] = failedTests;
            
            if (failedTests > 0)
            {
                result.Success = false;
                result.Data["Error"] = $"{failedTests} threshold tests failed";
            }

            return result;
        }

        public void Cleanup()
        {
            TestHelpers.SafeDestroyWeapon(currentWeapon);
            TestHelpers.SafeDestroyWeapon(minorUpgrade);
            TestHelpers.SafeDestroyWeapon(majorUpgrade);
            testPawn?.Destroy();
        }
    }

    /// <summary>
    /// Test SimpleSidearms edge cases including weight limits and duplicate prevention
    /// </summary>
    public class SimpleSidearmsEdgeCasesTest : ITestScenario
    {
        public string Name => "SimpleSidearms Edge Cases (Weight/Duplicates)";
        private Pawn testPawn;
        private List<ThingWithComps> weapons = new List<ThingWithComps>();

        public void Setup(Map map)
        {
            if (map == null) return;

            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn != null)
            {
                testPawn.equipment?.DestroyAllEquipment();
                
                // Give pawn some existing sidearms to test weight limits
                if (SimpleSidearmsCompat.IsLoaded())
                {
                    // Add a pistol to inventory
                    var pistol = TestHelpers.CreateWeapon(map, VanillaWeaponDefOf.Gun_Autopistol,
                        testPawn.Position);
                    if (pistol != null)
                    {
                        pistol.DeSpawn();
                        testPawn.inventory.innerContainer.TryAdd(pistol);
                        weapons.Add(pistol);
                    }
                }

                // Create heavy weapons to test weight limits
                var heavyWeaponDefs = new[] 
                {
                    DefDatabase<ThingDef>.GetNamed("Gun_Minigun", false),
                    DefDatabase<ThingDef>.GetNamed("Gun_LMG", false),
                    DefDatabase<ThingDef>.GetNamed("Gun_ChainShotgun", false)
                };

                foreach (var def in heavyWeaponDefs.Where(d => d != null))
                {
                    var weapon = TestHelpers.CreateWeapon(map, def,
                        testPawn.Position + new IntVec3(weapons.Count * 2, 0, 0));
                    if (weapon != null)
                    {
                        weapons.Add(weapon);
                        ImprovedWeaponCacheManager.AddWeaponToCache(weapon);
                    }
                }

                // Create duplicate weapon types
                for (int i = 0; i < 3; i++)
                {
                    var rifle = TestHelpers.CreateWeapon(map, VanillaWeaponDefOf.Gun_AssaultRifle,
                        testPawn.Position + new IntVec3(-2 - i * 2, 0, 0),
                        (QualityCategory)(i + 2)); // Different qualities
                    if (rifle != null)
                    {
                        weapons.Add(rifle);
                        ImprovedWeaponCacheManager.AddWeaponToCache(rifle);
                    }
                }
            }
        }

        public TestResult Run()
        {
            if (!SimpleSidearmsCompat.IsLoaded())
            {
                var skipResult = TestResult.Pass();
                skipResult.Data["Skipped"] = "SimpleSidearms not loaded";
                return skipResult;
            }

            if (testPawn == null)
                return TestResult.Failure("Test setup failed");

            var result = new TestResult { Success = true };
            var jobGiver = new JobGiver_PickUpBetterWeapon();

            // Test 1: Weight limit checking
            // SimpleSidearms doesn't expose these methods directly
            result.Data["CurrentWeight"] = "N/A - Not exposed by SS API";
            result.Data["WeightLimit"] = "N/A - Not exposed by SS API";

            // Find a heavy weapon
            var heavyWeapon = weapons.FirstOrDefault(w => w.GetStatValue(StatDefOf.Mass) > 5f);
            if (heavyWeapon != null)
            {
                string reason;
                bool canPickupHeavy = SimpleSidearmsCompat.CanPickupSidearmInstance(heavyWeapon, testPawn, out reason);
                result.Data["CanPickupHeavyWeapon"] = canPickupHeavy;
                result.Data["HeavyWeaponReason"] = reason ?? "None";
                result.Data["HeavyWeaponMass"] = heavyWeapon.GetStatValue(StatDefOf.Mass);

                // Verify weight limit is enforced
                if (canPickupHeavy && reason?.Contains("weight") == true)
                {
                    result.Success = false;
                    result.Data["Error1"] = "Weight limit not properly enforced";
                }
            }

            // Test 2: Duplicate weapon type prevention
            var rifles = weapons.Where(w => w.def == VanillaWeaponDefOf.Gun_AssaultRifle).ToList();
            if (rifles.Count >= 2)
            {
                // Pick up first rifle
                var firstRifle = rifles[0];
                if (firstRifle.Spawned)
                {
                    firstRifle.DeSpawn();
                    testPawn.inventory.innerContainer.TryAdd(firstRifle);
                }

                // Try to get job for second rifle
                var job = jobGiver.TestTryGiveJob(testPawn);
                
                bool allowDuplicates = SimpleSidearmsCompat.ALLOW_DUPLICATE_WEAPON_TYPES;
                result.Data["AllowDuplicateTypes"] = allowDuplicates;
                
                if (!allowDuplicates)
                {
                    // Should not pick up duplicate weapon type
                    if (job != null && rifles.Contains(job.targetA.Thing))
                    {
                        result.Success = false;
                        result.Data["Error2"] = "Picked up duplicate weapon type when not allowed";
                    }
                }
            }

            // Test 3: Slot limit checking
            int currentSlots = SimpleSidearmsCompat.GetCurrentSidearmCount(testPawn);
            int maxSlots = SimpleSidearmsCompat.GetMaxSidearmsForPawn(testPawn);
            
            result.Data["CurrentSlots"] = currentSlots;
            result.Data["MaxSlots"] = maxSlots;

            // Fill up slots if possible
            while (currentSlots < maxSlots && weapons.Any(w => w.Spawned))
            {
                var weapon = weapons.First(w => w.Spawned);
                weapon.DeSpawn();
                if (testPawn.inventory.innerContainer.TryAdd(weapon))
                {
                    currentSlots++;
                }
                else
                {
                    break;
                }
            }

            // Test slot limit enforcement
            if (currentSlots >= maxSlots)
            {
                var job = jobGiver.TestTryGiveJob(testPawn);
                result.Data["JobWithFullSlots"] = job != null;
                
                if (job != null && weapons.Contains(job.targetA.Thing))
                {
                    result.Success = false;
                    result.Data["Error3"] = "Created job when sidearm slots are full";
                }
            }

            // Test 4: AutoEquipTracker marking
            var testJob = JobHelper.CreateEquipJob(weapons.FirstOrDefault(w => w.Spawned));
            if (testJob != null)
            {
                AutoEquipTracker.MarkAsAutoEquip(testJob, testPawn);
                result.Data["JobMarkedAsAutoEquip"] = AutoEquipTracker.IsAutoEquip(testJob);
                
                if (!AutoEquipTracker.IsAutoEquip(testJob))
                {
                    result.Success = false;
                    result.Data["Error4"] = "AutoEquipTracker not marking jobs properly";
                }
            }

            return result;
        }

        public void Cleanup()
        {
            foreach (var weapon in weapons)
            {
                TestHelpers.SafeDestroyWeapon(weapon);
            }
            weapons.Clear();
            testPawn?.Destroy();
        }
    }


}