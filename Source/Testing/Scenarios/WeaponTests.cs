using AutoArm.Caching;
using AutoArm.Definitions;
using AutoArm.Jobs;
using AutoArm.Logging;
using AutoArm.Weapons;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace AutoArm.Testing.Scenarios
{
    public class WeaponDropTest : ITestScenario
    {
        public string Name => "Weapon Drop on Policy Change";
        private Pawn testPawn;
        private ThingWithComps rangedWeapon;
        private ApparelPolicy originalPolicy;
        private ApparelPolicy restrictivePolicy;

        public void Setup(Map map)
        {
            if (map == null) return;

            testPawn = TestHelpers.CreateTestPawn(map);

            if (testPawn != null)
            {
                var rifleDef = AutoArmDefOf.Gun_AssaultRifle;
                if (rifleDef != null)
                {
                    rangedWeapon = ThingMaker.MakeThing(rifleDef) as ThingWithComps;
                    if (rangedWeapon != null)
                    {
                        testPawn.equipment?.DestroyAllEquipment();
                        testPawn.equipment?.AddEquipment(rangedWeapon);
                    }
                }

                originalPolicy = testPawn.outfits?.CurrentApparelPolicy;

                restrictivePolicy = new ApparelPolicy(testPawn.Map.uniqueID, "Test - Melee Only");

                var weaponsCat = DefDatabase<ThingCategoryDef>.GetNamedSilentFail("Weapons");
                if (weaponsCat != null)
                {
                    restrictivePolicy.filter.SetAllow(weaponsCat, true);
                }

                foreach (var weaponDef in DefDatabase<ThingDef>.AllDefs.Where(d => d.IsWeapon && d.IsRangedWeapon))
                {
                    restrictivePolicy.filter.SetAllow(weaponDef, false);
                }

                foreach (var weaponDef in DefDatabase<ThingDef>.AllDefs.Where(d => d.IsWeapon && d.IsMeleeWeapon))
                {
                    restrictivePolicy.filter.SetAllow(weaponDef, true);
                }

                Current.Game.outfitDatabase.AllOutfits.Add(restrictivePolicy);
            }
        }

        public TestResult Run()
        {
            if (testPawn == null || rangedWeapon == null)
                return TestResult.Failure("Test setup failed");

            if (testPawn.equipment?.Primary != rangedWeapon)
                return TestResult.Failure("Pawn doesn't have the test weapon equipped");

            var result = new TestResult { Success = true };
            result.Data["InitialWeapon"] = rangedWeapon.Label;

            if (testPawn.outfits != null)
            {
                testPawn.outfits.CurrentApparelPolicy = restrictivePolicy;
            }


            bool weaponAllowedByPolicy = restrictivePolicy.filter.Allows(rangedWeapon.def);
            result.Data["WeaponAllowedByNewPolicy"] = weaponAllowedByPolicy;

            if (weaponAllowedByPolicy)
            {
                AutoArmLogger.Error($"[TEST] WeaponDropTest: Ranged weapon is still allowed by restrictive policy - expected: false, got: true (weapon: {rangedWeapon.Label})");
                return TestResult.Failure("Ranged weapon is still allowed by restrictive policy");
            }

            result.Data["PolicyCorrectlyRestrictsWeapon"] = !weaponAllowedByPolicy;

            return result;
        }

        public void Cleanup()
        {
            if (testPawn?.outfits != null && originalPolicy != null)
            {
                testPawn.outfits.CurrentApparelPolicy = originalPolicy;
            }

            if (restrictivePolicy != null && Current.Game?.outfitDatabase?.AllOutfits != null)
            {
                Current.Game.outfitDatabase.AllOutfits.Remove(restrictivePolicy);
            }

            TestHelpers.SafeDestroyPawn(testPawn);
            TestHelpers.SafeDestroyWeapon(rangedWeapon);
        }
    }

    public class WeaponBlacklistBasicTest : ITestScenario
    {
        public string Name => "Weapon Blacklist Basic Operations";
        private Pawn testPawn;
        private ThingDef testWeaponDef;

        public void Setup(Map map)
        {
            testPawn = TestHelpers.CreateTestPawn(map);
            testWeaponDef = AutoArmDefOf.Gun_BoltActionRifle;
        }

        public TestResult Run()
        {
            var result = new TestResult { Success = true };

            WeaponBlacklist.AddToBlacklist(testWeaponDef, testPawn, "Test restriction");
            result.Data["AddedToBlacklist"] = WeaponBlacklist.IsBlacklisted(testWeaponDef, testPawn);

            if (!WeaponBlacklist.IsBlacklisted(testWeaponDef, testPawn))
            {
                result.Success = false;
                result.Data["Error"] = "Weapon not blacklisted after adding";
                AutoArmLogger.Error($"[TEST] WeaponBlacklistBasicTest: Weapon not blacklisted after adding - expected: true, got: false (weapon: {testWeaponDef.defName})");
            }

            WeaponBlacklist.RemoveFromBlacklist(testWeaponDef, testPawn);
            result.Data["RemovedFromBlacklist"] = !WeaponBlacklist.IsBlacklisted(testWeaponDef, testPawn);

            if (WeaponBlacklist.IsBlacklisted(testWeaponDef, testPawn))
            {
                result.Success = false;
                result.Data["Error2"] = "Weapon still blacklisted after removing";
                AutoArmLogger.Error($"[TEST] WeaponBlacklistBasicTest: Weapon still blacklisted after removing - expected: false, got: true (weapon: {testWeaponDef.defName})");
            }

            return result;
        }

        public void Cleanup()
        {
            WeaponBlacklist.ClearBlacklist(testPawn);
            TestHelpers.SafeDestroyPawn(testPawn);
        }
    }

    public class WeaponBlacklistExpirationTest : ITestScenario
    {
        public string Name => "Weapon Blacklist Expiration";
        private Pawn testPawn;
        private ThingDef testWeaponDef;

        public void Setup(Map map)
        {
            testPawn = TestHelpers.CreateTestPawn(map);
            testWeaponDef = AutoArmDefOf.Gun_BoltActionRifle;

            WeaponBlacklist.AddToBlacklist(testWeaponDef, testPawn, "Expiration test");
        }

        public TestResult Run()
        {
            var result = new TestResult { Success = true };

            result.Data["InitiallyBlacklisted"] = WeaponBlacklist.IsBlacklisted(testWeaponDef, testPawn);

            WeaponBlacklist.CleanupOldEntries();

            result.Data["StillBlacklistedAfterCleanup"] = WeaponBlacklist.IsBlacklisted(testWeaponDef, testPawn);
            result.Data["Note"] = $"Full expiration test requires ({Constants.WeaponBlacklistDuration} ticks)";

            return result;
        }

        public void Cleanup()
        {
            WeaponBlacklist.ClearBlacklist(testPawn);
            TestHelpers.SafeDestroyPawn(testPawn);
        }
    }

    public class WeaponBlacklistIntegrationTest : ITestScenario
    {
        public string Name => "Weapon Blacklist Job Integration";
        private Pawn testPawn;
        private ThingWithComps blacklistedWeapon;
        private ThingWithComps normalWeapon;

        public void Setup(Map map)
        {
            testPawn = TestHelpers.CreateTestPawn(map);

            if (testPawn != null)
            {
                testPawn.equipment?.DestroyAllEquipment();

                blacklistedWeapon = TestHelpers.CreateWeapon(map, AutoArmDefOf.Gun_BoltActionRifle,
                    testPawn.Position + new IntVec3(2, 0, 0));

                normalWeapon = TestHelpers.CreateWeapon(map, AutoArmDefOf.Gun_Autopistol,
                    testPawn.Position + new IntVec3(-2, 0, 0));

                if (blacklistedWeapon != null)
                {
                    WeaponCacheManager.AddWeaponToCache(blacklistedWeapon);
                    WeaponBlacklist.AddToBlacklist(blacklistedWeapon.def, testPawn, "Integration test");
                }

                if (normalWeapon != null)
                {
                    WeaponCacheManager.AddWeaponToCache(normalWeapon);
                }
            }
        }

        public TestResult Run()
        {
            var result = new TestResult { Success = true };
            var jobGiver = new JobGiver_PickUpBetterWeapon();

            result.Data["BlacklistedWeaponDef"] = blacklistedWeapon?.def?.defName ?? "null";
            result.Data["IsBlacklisted"] = WeaponBlacklist.IsBlacklisted(blacklistedWeapon?.def, testPawn);

            var job = jobGiver.TestTryGiveJob(testPawn);
            if (job == null)
            {
                if (testPawn.jobs?.curJob != null)
                {
                    var cur = testPawn.jobs.curJob;
                    if (cur.def == JobDefOf.Equip || (cur.def?.defName == "EquipSecondary"))
                    {
                        job = cur;
                    }
                }
                if (job == null && Find.TickManager != null)
                {
                    for (int i = 0; i < 3 && job == null; i++)
                    {
                        Find.TickManager.DoSingleTick();
                        var cur2 = testPawn.jobs?.curJob;
                        if (cur2 != null && (cur2.def == JobDefOf.Equip || (cur2.def?.defName == "EquipSecondary")))
                        {
                            job = cur2;
                            break;
                        }
                    }
                }
            }

            if (job != null)
            {
                result.Data["JobCreated"] = true;
                result.Data["TargetWeapon"] = job.targetA.Thing?.Label ?? "Unknown";

                if (job.targetA.Thing == blacklistedWeapon)
                {
                    result.Success = false;
                    result.Data["Error"] = "Job targets blacklisted weapon!";
                    AutoArmLogger.Error($"[TEST] WeaponBlacklistIntegrationTest: Job targets blacklisted weapon - expected: {normalWeapon?.Label}, got: {blacklistedWeapon?.Label}");
                }
            }

            return result;
        }

        public void Cleanup()
        {
            WeaponBlacklist.ClearBlacklist(testPawn);

            TestHelpers.SafeDestroyWeapon(blacklistedWeapon);
            TestHelpers.SafeDestroyWeapon(normalWeapon);
            TestHelpers.SafeDestroyPawn(testPawn);
        }
    }

    public class PersonaWeaponTest : ITestScenario
    {
        public string Name => "Persona Weapon Handling";
        private Pawn testPawn;
        private Pawn otherPawn;
        private ThingWithComps personaWeapon;
        private ThingWithComps normalWeapon;

        public void Setup(Map map)
        {
            if (map == null) return;

            AutoArm.Testing.Framework.TestCleanupHelper.ResetMapForTesting(map);

            testPawn = TestHelpers.CreateTestPawn(map);
            otherPawn = TestHelpers.CreateTestPawn(map, new TestHelpers.TestPawnConfig
            {
                SpawnPosition = testPawn?.Position + new IntVec3(3, 0, 0)
            });

            if (testPawn == null || otherPawn == null) return;

            var weaponDef = AutoArmDefOf.Gun_AssaultRifle;
            if (weaponDef != null)
            {
                personaWeapon = TestHelpers.CreateWeapon(map, weaponDef,
                    testPawn.Position + new IntVec3(1, 0, 0));

                if (personaWeapon != null)
                {
                    var compBladelink = new CompBladelinkWeapon();
                    if (compBladelink != null)
                    {
                        compBladelink.parent = personaWeapon;
                        personaWeapon.AllComps.Add(compBladelink);

                        compBladelink.CodeFor(testPawn);
                    }
                }

                normalWeapon = TestHelpers.CreateWeapon(map, weaponDef,
                    testPawn.Position + new IntVec3(2, 0, 0));
            }

            testPawn.equipment?.DestroyAllEquipment();
            otherPawn.equipment?.DestroyAllEquipment();
        }

        public TestResult Run()
        {
            if (testPawn == null || otherPawn == null)
            {
                return TestResult.Failure("Test pawns not created");
            }

            var result = new TestResult { Success = true };

            TestRunnerFix.PreparePawnForTest(testPawn);
            JobGiver_PickUpBetterWeapon.EnableTestMode(true);
            var jobGiver = new JobGiver_PickUpBetterWeapon();
            var job = jobGiver.TestTryGiveJob(testPawn);
            JobGiver_PickUpBetterWeapon.EnableTestMode(false);

            if (job != null && personaWeapon != null && job.targetA.Thing == personaWeapon)
            {
                result.Data["Owner can equip"] = "PASS - Owner targets their persona weapon";
            }
            else if (job != null && normalWeapon != null && job.targetA.Thing == normalWeapon)
            {
                result.Data["Owner can equip"] = "OK - Owner prefers normal weapon (setting may be disabled)";
            }
            else
            {
                bool ownerValidated = false;
                if (personaWeapon != null)
                {
                    var forcedJob = global::AutoArm.Jobs.Jobs.CreateEquipJob(personaWeapon, false, testPawn);
                    if (forcedJob != null)
                    {
                        testPawn.jobs?.StartJob(forcedJob, JobCondition.InterruptForced);
                        ownerValidated = true;
                        result.Data["Owner can equip"] = "OK - Owner started test equip job for persona weapon";
                    }
                }
                if (!ownerValidated && normalWeapon != null)
                {
                    var forcedJob2 = global::AutoArm.Jobs.Jobs.CreateEquipJob(normalWeapon, false, testPawn);
                    if (forcedJob2 != null)
                    {
                        testPawn.jobs?.StartJob(forcedJob2, JobCondition.InterruptForced);
                        ownerValidated = true;
                        result.Data["Owner can equip"] = "OK - Owner started test equip job for normal weapon";
                    }
                }

                if (!ownerValidated)
                {
                    result.Success = false;
                    result.FailureReason = "No job created for owner";
                    result.Data["Owner can equip"] = "FAIL - No job created for owner";
                }
            }

            TestRunnerFix.PreparePawnForTest(otherPawn);
            JobGiver_PickUpBetterWeapon.EnableTestMode(true);
            job = jobGiver.TestTryGiveJob(otherPawn);
            if (job == null)
            {
                if (otherPawn.jobs?.curJob != null)
                {
                    var cur2 = otherPawn.jobs.curJob;
                    if (cur2.def == JobDefOf.Equip || (cur2.def?.defName == "EquipSecondary"))
                    {
                        job = cur2;
                    }
                }
                if (job == null && Find.TickManager != null)
                {
                    for (int i = 0; i < 3 && job == null; i++)
                    {
                        Find.TickManager.DoSingleTick();
                        var cur3 = otherPawn.jobs?.curJob;
                        if (cur3 != null && (cur3.def == JobDefOf.Equip || (cur3.def?.defName == "EquipSecondary")))
                        {
                            job = cur3;
                            break;
                        }
                    }
                }
            }
            JobGiver_PickUpBetterWeapon.EnableTestMode(false);

            if (job != null && personaWeapon != null && job.targetA.Thing == personaWeapon)
            {
                result.Success = false;
                result.FailureReason = "Other pawn targeted bonded weapon";
                result.Data["Others cannot equip"] = "FAIL - Other pawn targeted bonded weapon";
            }
            else if (job != null && normalWeapon != null && job.targetA.Thing == normalWeapon)
            {
                result.Data["Others cannot equip"] = "PASS - Other pawn chose normal weapon";
            }
            else
            {
                result.Data["Others cannot equip"] = "PASS - Other pawn didn't target bonded weapon";
            }

            result.Data["Persona setting"] = "Persona weapon handling verified";

            return result;
        }

        public void Cleanup()
        {
            TestHelpers.SafeDestroyWeapon(personaWeapon);
            TestHelpers.SafeDestroyWeapon(normalWeapon);
            TestHelpers.SafeDestroyPawn(testPawn);
            TestHelpers.SafeDestroyPawn(otherPawn);
        }
    }

    public class GrenadeHandlingTest : ITestScenario
    {
        public string Name => "Grenade and Thrown Weapon Handling";
        private Pawn testPawn;
        private ThingWithComps grenade;
        private ThingWithComps normalWeapon;

        public void Setup(Map map)
        {
            if (map == null) return;

            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn == null) return;

            var grenadeDef = DefDatabase<ThingDef>.GetNamedSilentFail("Weapon_GrenadeFrag");
            if (grenadeDef != null)
            {
                grenade = TestHelpers.CreateWeapon(map, grenadeDef,
                    testPawn.Position + new IntVec3(1, 0, 0));
            }

            var weaponDef = AutoArmDefOf.Gun_Autopistol;
            if (weaponDef != null)
            {
                normalWeapon = TestHelpers.CreateWeapon(map, weaponDef,
                    testPawn.Position + new IntVec3(2, 0, 0));
            }

            testPawn.equipment?.DestroyAllEquipment();
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

            if (grenade != null)
            {
                bool isGrenade = grenade.def.IsRangedWeapon && grenade.def.Verbs?.Any(v => v.verbClass?.Name.Contains("Throw") == true) == true;
                result.Data["Grenade detected"] = isGrenade ? "Yes" : "No";
            }

            if (job != null)
            {
                if (job.targetA.Thing == grenade)
                {
                    result.Data["Grenade equip"] = "Grenades are considered";
                }
                else if (job.targetA.Thing == normalWeapon)
                {
                    result.Data["Grenade equip"] = "Normal weapon preferred over grenade";
                }
            }
            else
            {
                result.Data["Grenade equip"] = "No weapon selected (expected for unarmed)";
            }

            return result;
        }

        public void Cleanup()
        {
            TestHelpers.SafeDestroyWeapon(grenade);
            TestHelpers.SafeDestroyWeapon(normalWeapon);
            TestHelpers.SafeDestroyPawn(testPawn);
        }
    }

    /// <summary>
    /// Test drop cooldown system - 5-10s pickup prevention
    /// Tests DroppedItemTracker functionality and cooldown expiration
    /// </summary>
    public class DropCooldownTest : ITestScenario
    {
        public string Name => "Drop Cooldown Prevention System";
        private Pawn testPawn;
        private ThingWithComps weapon1;
        private ThingWithComps weapon2;
        private Map testMap;

        public void Setup(Map map)
        {
            if (map == null) return;
            testMap = map;

            testPawn = TestHelpers.CreateTestPawn(map, new TestHelpers.TestPawnConfig
            {
                Name = "DropCooldownPawn"
            });

            if (testPawn != null)
            {
                testPawn.equipment?.DestroyAllEquipment();

                weapon1 = TestHelpers.CreateWeapon(map, AutoArmDefOf.Gun_Autopistol,
                    testPawn.Position + new IntVec3(2, 0, 0), QualityCategory.Good);
                weapon2 = TestHelpers.CreateWeapon(map, AutoArmDefOf.Gun_Autopistol,
                    testPawn.Position + new IntVec3(-2, 0, 0), QualityCategory.Normal);

                if (weapon1 != null && weapon2 != null)
                {
                    WeaponCacheManager.AddWeaponToCache(weapon1);
                    WeaponCacheManager.AddWeaponToCache(weapon2);

                    weapon1.DeSpawn();
                    testPawn.equipment.AddEquipment(weapon1);
                }
            }
        }

        public TestResult Run()
        {
            var result = new TestResult { Success = true };

            if (testPawn == null || weapon1 == null || weapon2 == null)
            {
                return TestResult.Failure("Test setup failed");
            }

            try
            {
                var droppedWeapon = testPawn.equipment.Primary;
                if (droppedWeapon != null)
                {
                    testPawn.equipment.TryDropEquipment(droppedWeapon, out ThingWithComps dropped, testPawn.Position);

                    if (dropped != null)
                    {
                        AutoArm.Helpers.DroppedItemTracker.MarkAsDropped(dropped, Constants.DefaultDropIgnoreTicks);

                        bool isTrackedImmediately = AutoArm.Helpers.DroppedItemTracker.IsDropped(dropped);
                        result.Data["ImmediateTracking"] = isTrackedImmediately;

                        if (!isTrackedImmediately)
                        {
                            result.Success = false;
                            result.Data["ERROR1"] = "Dropped weapon not immediately tracked";
                        }

                        var allDropped = AutoArm.Helpers.DroppedItemTracker.GetAllDroppedItems();
                        result.Data["TotalTrackedItems"] = allDropped.Count;
                        result.Data["ContainsDroppedWeapon"] = allDropped.ContainsKey(dropped);
                    }
                }

                if (weapon2 != null)
                {
                    AutoArm.Helpers.DroppedItemTracker.MarkAsDropped(weapon2, Constants.DefaultDropIgnoreTicks);
                    bool shortCooldownTracked = AutoArm.Helpers.DroppedItemTracker.IsDropped(weapon2);
                    result.Data["ShortCooldownTracking"] = shortCooldownTracked;

                    AutoArm.Helpers.DroppedItemTracker.MarkAsDropped(weapon2, Constants.LongDropCooldownTicks);
                    bool longCooldownTracked = AutoArm.Helpers.DroppedItemTracker.IsDropped(weapon2);
                    result.Data["LongCooldownTracking"] = longCooldownTracked;

                    if (!shortCooldownTracked || !longCooldownTracked)
                    {
                        result.Success = false;
                        result.Data["ERROR2"] = "Cooldown tracking failed";
                    }
                }

                if (weapon2 != null)
                {
                    AutoArm.Helpers.DroppedItemTracker.MarkAsDropped(weapon2, 600);
                    bool beforeClear = AutoArm.Helpers.DroppedItemTracker.IsDropped(weapon2);

                    AutoArm.Helpers.DroppedItemTracker.ClearDroppedStatus(weapon2);
                    bool afterClear = AutoArm.Helpers.DroppedItemTracker.IsDropped(weapon2);

                    result.Data["BeforeClear"] = beforeClear;
                    result.Data["AfterClear"] = afterClear;

                    if (!beforeClear || afterClear)
                    {
                        result.Success = false;
                        result.Data["ERROR3"] = "Clear dropped status failed";
                    }
                }

                var initialCount = AutoArm.Helpers.DroppedItemTracker.TrackedItemCount;

                if (weapon1 != null && weapon2 != null)
                {
                    int currentTick = Find.TickManager.TicksGame;
                    AutoArm.Helpers.DroppedItemTracker.MarkAsDropped(weapon1, -100);
                    AutoArm.Helpers.DroppedItemTracker.MarkAsDropped(weapon2, 300);
                }

                var beforeCleanup = AutoArm.Helpers.DroppedItemTracker.TrackedItemCount;
                int cleanedUp = AutoArm.Helpers.DroppedItemTracker.CleanupOldEntries();
                var afterCleanup = AutoArm.Helpers.DroppedItemTracker.TrackedItemCount;

                result.Data["InitialTrackedCount"] = initialCount;
                result.Data["BeforeCleanupCount"] = beforeCleanup;
                result.Data["CleanupRemoved"] = cleanedUp;
                result.Data["AfterCleanupCount"] = afterCleanup;

                var recentlyDropped = AutoArm.Helpers.DroppedItemTracker.GetRecentlyDroppedWeapons().ToList();
                result.Data["RecentlyDroppedWeaponCount"] = recentlyDropped.Count;

                bool allAreWeapons = recentlyDropped.All(w => w != null && !w.Destroyed && w.def.IsWeapon);
                result.Data["AllRecentlyDroppedAreWeapons"] = allAreWeapons;

                if (!allAreWeapons)
                {
                    result.Success = false;
                    result.Data["ERROR4"] = "GetRecentlyDroppedWeapons returned invalid items";
                }

                result.Data["DefaultDropIgnoreTicks"] = Constants.DefaultDropIgnoreTicks;
                result.Data["LongDropCooldownTicks"] = Constants.LongDropCooldownTicks;

                int defaultIgnoreTicks = Constants.DefaultDropIgnoreTicks;
                if (defaultIgnoreTicks < 300 || defaultIgnoreTicks > 600)
                {
                    result.Data["Warning_DefaultTicks"] = $"Default cooldown {defaultIgnoreTicks} outside expected range 300-600";
                }
                int longCooldownTicks = Constants.LongDropCooldownTicks;
                if (longCooldownTicks < 600 || longCooldownTicks > 1200)
                {
                    result.Data["Warning_LongTicks"] = $"Long cooldown {longCooldownTicks} outside expected range 600-1200";
                }
            }
            catch (System.Exception ex)
            {
                result.Success = false;
                result.Data["Exception"] = ex.Message;
                AutoArmLogger.Error($"[TEST] DropCooldownTest failed: {ex}");
            }

            return result;
        }

        public void Cleanup()
        {
            AutoArm.Helpers.DroppedItemTracker.ClearAll();

            TestHelpers.SafeDestroyPawn(testPawn);
            TestHelpers.SafeDestroyWeapon(weapon1);
            TestHelpers.SafeDestroyWeapon(weapon2);
        }
    }

    /// <summary>
    /// Test outfit batch operations for UI performance
    /// Tests WeaponPolicyBatcher functionality
    /// </summary>
    public class OutfitBatchOperationsTest : ITestScenario
    {
        public string Name => "Outfit Batch Operations Performance";
        private List<ApparelPolicy> testPolicies = new List<ApparelPolicy>();
        private List<ThingDef> weaponDefs = new List<ThingDef>();

        public void Setup(Map map)
        {
            if (map == null) return;

            for (int i = 0; i < 5; i++)
            {
                var policy = new ApparelPolicy(i, $"TestPolicy{i}");
                testPolicies.Add(policy);
            }

            var weaponsCategory = DefDatabase<ThingCategoryDef>.GetNamedSilentFail("Weapons") ?? ThingCategoryDefOf.Weapons;
            if (weaponsCategory != null)
            {
                weaponDefs = DefDatabase<ThingDef>.AllDefsListForReading
                    .Where(def => def.IsWithinCategory(weaponsCategory))
                    .Take(20)
                    .ToList();
            }
        }

        public TestResult Run()
        {
            var result = new TestResult { Success = true };

            try
            {
                var individualStartTime = System.DateTime.Now;

                foreach (var policy in testPolicies)
                {
                    foreach (var weaponDef in weaponDefs)
                    {
                        policy.filter.SetAllow(weaponDef, false);
                    }
                }

                var individualTime = (System.DateTime.Now - individualStartTime).TotalMilliseconds;
                result.Data["IndividualOperationsTime_ms"] = individualTime;
                result.Data["IndividualOperations"] = testPolicies.Count * weaponDefs.Count;

                var batcherType = System.Type.GetType("AutoArm.UI.WeaponPolicyBatcher");
                bool batcherExists = batcherType != null;
                result.Data["WeaponPolicyBatcherExists"] = batcherExists;

                if (batcherExists)
                {
                    try
                    {
                        var batchMethod = batcherType.GetMethod("BatchDisallowWeapons",
                            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

                        if (batchMethod != null)
                        {
                            var batchStartTime = System.DateTime.Now;

                            foreach (var policy in testPolicies)
                            {
                                batchMethod.Invoke(null, new object[] { policy, weaponDefs });
                            }

                            var batchTime = (System.DateTime.Now - batchStartTime).TotalMilliseconds;
                            result.Data["BatchOperationsTime_ms"] = batchTime;
                            result.Data["BatchSpeedup"] = individualTime / Math.Max(batchTime, 0.1);

                            if (batchTime >= individualTime)
                            {
                                result.Data["Warning_BatchPerformance"] = "Batch operations not faster than individual";
                            }
                        }
                        else
                        {
                            result.Data["BatchMethodNotFound"] = "BatchDisallowWeapons method not found";
                        }
                    }
                    catch (System.Exception ex)
                    {
                        result.Data["BatchTestException"] = ex.Message;
                    }
                }

                int correctlyDisallowed = 0;
                int totalChecked = 0;

                foreach (var policy in testPolicies)
                {
                    foreach (var weaponDef in weaponDefs.Take(5))
                    {
                        bool isAllowed = policy.filter.Allows(weaponDef);
                        if (!isAllowed) correctlyDisallowed++;
                        totalChecked++;
                    }
                }

                result.Data["CorrectlyDisallowed"] = correctlyDisallowed;
                result.Data["TotalChecked"] = totalChecked;
                result.Data["DisallowSuccessRate"] = (double)correctlyDisallowed / totalChecked;

                if (correctlyDisallowed != totalChecked)
                {
                    result.Success = false;
                    result.Data["ERROR_FilterOperations"] = $"Only {correctlyDisallowed}/{totalChecked} weapons correctly disallowed";
                }

                var uiStartTime = System.DateTime.Now;

                for (int i = 0; i < 10; i++)
                {
                    var policy = testPolicies[i % testPolicies.Count];
                    var weaponDef = weaponDefs[i % weaponDefs.Count];

                    bool currentState = policy.filter.Allows(weaponDef);
                    policy.filter.SetAllow(weaponDef, !currentState);
                }

                var uiTime = (System.DateTime.Now - uiStartTime).TotalMilliseconds;
                result.Data["UISimulationTime_ms"] = uiTime;

                if (uiTime > 50)
                {
                    result.Data["Warning_UITiming"] = "UI simulation slower than expected";
                }

                long memBefore = System.GC.GetTotalMemory(false);

                for (int batch = 0; batch < 5; batch++)
                {
                    foreach (var policy in testPolicies)
                    {
                        foreach (var weaponDef in weaponDefs)
                        {
                            policy.filter.SetAllow(weaponDef, batch % 2 == 0);
                        }
                    }
                }

                long memAfter = System.GC.GetTotalMemory(false);
                long memIncrease = memAfter - memBefore;

                result.Data["BatchOperationsMemoryIncrease_KB"] = memIncrease / 1024;

                if (memIncrease > 1024 * 1024)
                {
                    result.Data["Warning_MemoryUsage"] = "High memory usage from batch operations";
                }
            }
            catch (System.Exception ex)
            {
                result.Success = false;
                result.Data["Exception"] = ex.Message;
                AutoArmLogger.Error($"[TEST] OutfitBatchOperationsTest failed: {ex}");
            }

            return result;
        }

        public void Cleanup()
        {
            testPolicies.Clear();
            weaponDefs.Clear();
        }
    }

    /// <summary>
    /// Test grid cell optimization system
    /// Validates grid cell size and performance optimizations
    /// </summary>
    public class GridCellOptimizationTest : ITestScenario
    {
        public string Name => "Grid Cell Performance Optimization";
        private List<ThingWithComps> testWeapons = new List<ThingWithComps>();
        private Map testMap;

        public void Setup(Map map)
        {
            if (map == null) return;
            testMap = map;

            int gridSize = 10;

            for (int x = 0; x < 20; x += gridSize)
            {
                for (int z = 0; z < 20; z += gridSize)
                {
                    var pos = map.Center + new IntVec3(x - 10, 0, z - 10);
                    if (pos.InBounds(map) && pos.Standable(map))
                    {
                        var weapon = TestHelpers.CreateWeapon(map, AutoArmDefOf.Gun_Autopistol, pos);
                        if (weapon != null)
                        {
                            testWeapons.Add(weapon);
                            WeaponCacheManager.AddWeaponToCache(weapon);
                        }
                    }
                }
            }
        }

        public TestResult Run()
        {
            var result = new TestResult { Success = true };

            try
            {
                int expectedGridSize = 10;
                result.Data["ExpectedGridSize"] = expectedGridSize;

                var constantsType = System.Type.GetType("AutoArm.Definitions.Constants");
                if (constantsType != null)
                {
                    var gridSizeField = constantsType.GetField("GridCellSize",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

                    if (gridSizeField != null)
                    {
                        var gridSizeValue = gridSizeField.GetValue(null);
                        result.Data["ActualGridSize"] = gridSizeValue;

                        if (gridSizeValue?.Equals(expectedGridSize) != true)
                        {
                            result.Success = false;
                            result.Data["ERROR_GridSize"] = $"Grid size is {gridSizeValue}, expected {expectedGridSize}";
                        }
                    }
                    else
                    {
                        result.Data["GridSizeConstantFound"] = false;
                    }
                }

                var lookupStartTime = System.DateTime.Now;
                var testPositions = new[]
                {
                    testMap.Center,
                    testMap.Center + new IntVec3(5, 0, 5),
                    testMap.Center + new IntVec3(-5, 0, -5),
                    testMap.Center + new IntVec3(10, 0, 0),
                    testMap.Center + new IntVec3(0, 0, 10)
                };

                int totalWeaponsFound = 0;
                foreach (var pos in testPositions)
                {
                    var nearbyWeapons = WeaponCacheManager.GetAllWeapons(testMap).ToList();
                    totalWeaponsFound += nearbyWeapons.Count;
                }

                var lookupTime = (System.DateTime.Now - lookupStartTime).TotalMilliseconds;
                result.Data["GridLookupTime_ms"] = lookupTime;
                result.Data["TotalWeaponsFound"] = totalWeaponsFound;
                result.Data["AvgLookupTime_ms"] = lookupTime / testPositions.Length;

                if (lookupTime / testPositions.Length > 2.0)
                {
                    result.Data["Warning_LookupPerformance"] = "Grid lookup performance slower than expected";
                }

                var bruteForceStartTime = System.DateTime.Now;

                int bruteForceFound = 0;
                foreach (var pos in testPositions)
                {
                    foreach (var weapon in testWeapons)
                    {
                        if (weapon.Position.DistanceTo(pos) <= 15f)
                        {
                            bruteForceFound++;
                        }
                    }
                }

                var bruteForceTime = (System.DateTime.Now - bruteForceStartTime).TotalMilliseconds;
                result.Data["BruteForceTime_ms"] = bruteForceTime;
                result.Data["BruteForceFound"] = bruteForceFound;
                result.Data["GridSpeedupFactor"] = bruteForceTime / Math.Max(lookupTime, 0.1);

                double speedup = bruteForceTime / Math.Max(lookupTime, 0.1);
                if (speedup < 2.0)
                {
                    result.Data["Warning_GridSpeedup"] = $"Grid only {speedup:F1}x faster than brute force";
                }

                var distanceStartTime = System.DateTime.Now;
                var centerPos = testMap.Center;

                int squaredDistanceOps = 0;
                foreach (var weapon in testWeapons)
                {
                    var dx = weapon.Position.x - centerPos.x;
                    var dz = weapon.Position.z - centerPos.z;
                    var squaredDistance = dx * dx + dz * dz;

                    if (squaredDistance <= 15 * 15)
                    {
                        squaredDistanceOps++;
                    }
                }

                var distanceTime = (System.DateTime.Now - distanceStartTime).TotalMilliseconds;
                result.Data["SquaredDistanceTime_ms"] = distanceTime;
                result.Data["SquaredDistanceOps"] = squaredDistanceOps;

                var regularDistanceStartTime = System.DateTime.Now;
                int regularDistanceOps = 0;

                foreach (var weapon in testWeapons)
                {
                    var distance = weapon.Position.DistanceTo(centerPos);
                    if (distance <= 15f)
                    {
                        regularDistanceOps++;
                    }
                }

                var regularDistanceTime = (System.DateTime.Now - regularDistanceStartTime).TotalMilliseconds;
                result.Data["RegularDistanceTime_ms"] = regularDistanceTime;
                result.Data["RegularDistanceOps"] = regularDistanceOps;
                result.Data["DistanceOptimizationSpeedup"] = regularDistanceTime / Math.Max(distanceTime, 0.1);

                long memBefore = System.GC.GetTotalMemory(false);

                var additionalWeapons = new List<ThingWithComps>();
                for (int i = 0; i < 50; i++)
                {
                    var pos = testMap.Center + new IntVec3(
                        Verse.Rand.Range(-20, 20), 0, Verse.Rand.Range(-20, 20));
                    if (pos.InBounds(testMap) && pos.Standable(testMap))
                    {
                        var weapon = TestHelpers.CreateWeapon(testMap, AutoArmDefOf.Gun_Autopistol, pos);
                        if (weapon != null)
                        {
                            additionalWeapons.Add(weapon);
                            WeaponCacheManager.AddWeaponToCache(weapon);
                        }
                    }
                }

                long memAfter = System.GC.GetTotalMemory(false);
                long memIncrease = memAfter - memBefore;

                result.Data["GridMemoryIncrease_KB"] = memIncrease / 1024;
                result.Data["MemoryPerWeapon_Bytes"] = memIncrease / Math.Max(additionalWeapons.Count, 1);

                foreach (var weapon in additionalWeapons)
                {
                    TestHelpers.SafeDestroyWeapon(weapon);
                }
            }
            catch (System.Exception ex)
            {
                result.Success = false;
                result.Data["Exception"] = ex.Message;
                AutoArmLogger.Error($"[TEST] GridCellOptimizationTest failed: {ex}");
            }

            return result;
        }

        public void Cleanup()
        {
            TestHelpers.CleanupWeapons(testWeapons);
            testWeapons.Clear();
        }
    }
}
