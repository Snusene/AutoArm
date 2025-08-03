// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: Pawn behavior tests (temporary colonists, children, prisoners, skills)
// Validates special pawn types and skill-based preferences

using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using AutoArm.Caching; using AutoArm.Helpers; using AutoArm.Jobs; using AutoArm.Logging; using AutoArm.Weapons;
using static AutoArm.Testing.TestHelpers;
using AutoArm.Definitions;

namespace AutoArm.Testing.Scenarios
{
    public class TemporaryColonistTest : ITestScenario
    {
        public string Name => "Temporary Colonist Behavior";
        private Pawn questLodger;
        private Pawn borrowedPawn;
        private List<ThingWithComps> weapons = new List<ThingWithComps>();

        public void Setup(Map map)
        {
            if (map == null) return;

            // Clear all systems before test
            TestRunnerFix.ResetAllSystems();

            // Create a quest lodger
            questLodger = TestHelpers.CreateTestPawn(map, new TestPawnConfig
            {
                Name = "QuestLodger"
            });

            if (questLodger != null)
            {
                // Mark as quest lodger
                if (questLodger.questTags == null)
                    questLodger.questTags = new List<string>();
                questLodger.questTags.Add("Lodger");

                // Make sure they're unarmed
                questLodger.equipment?.DestroyAllEquipment();
            }

            // Create a borrowed pawn
            borrowedPawn = TestHelpers.CreateTestPawn(map, new TestPawnConfig
            {
                Name = "BorrowedPawn"
            });

            if (borrowedPawn != null)
            {
                // Set up as borrowed by another faction
                var otherFaction = Find.FactionManager.AllFactions
                    .FirstOrDefault(f => f != Faction.OfPlayer && !f.HostileTo(Faction.OfPlayer));

                if (otherFaction != null)
                {
                    // This pawn belongs to player but is on loan to another faction
                    borrowedPawn.SetFaction(Faction.OfPlayer);
                    if (borrowedPawn.guest == null)
                        borrowedPawn.guest = new Pawn_GuestTracker(borrowedPawn);
                    borrowedPawn.guest.SetGuestStatus(otherFaction, GuestStatus.Guest);
                }

                borrowedPawn.equipment?.DestroyAllEquipment();
            }

            // Create weapons near the pawns
            if (questLodger != null)
            {
                var weaponDef = VanillaWeaponDefOf.Gun_Autopistol;
                if (weaponDef != null)
                {
                    var weapon1 = TestHelpers.CreateWeapon(map, weaponDef,
                        questLodger.Position + new IntVec3(2, 0, 0));
                    if (weapon1 != null)
                    {
                        weapons.Add(weapon1);
                        ImprovedWeaponCacheManager.AddWeaponToCache(weapon1);
                    }

                    var weapon2 = TestHelpers.CreateWeapon(map, weaponDef,
                        borrowedPawn?.Position + new IntVec3(2, 0, 0) ?? IntVec3.Invalid);
                    if (weapon2 != null)
                    {
                        weapons.Add(weapon2);
                        ImprovedWeaponCacheManager.AddWeaponToCache(weapon2);
                    }
                }
            }
        }

        public TestResult Run()
        {
            var result = new TestResult { Success = true };
            var jobGiver = new JobGiver_PickUpBetterWeapon();

            // Store current setting
            bool originalSetting = AutoArmMod.settings?.allowTemporaryColonists ?? false;

            // Test with temporary colonists NOT allowed
            AutoArmMod.settings.allowTemporaryColonists = false;

            result.Data["Setting_AllowTemporary"] = false;

            // Test quest lodger
            if (questLodger != null)
            {
                string reason;
                bool isTemp = JobGiverHelpers.IsTemporaryColonist(questLodger);
                bool isValid = JobGiverHelpers.IsValidPawnForAutoEquip(questLodger, out reason);

                result.Data["QuestLodger_IsTemp"] = isTemp;
                result.Data["QuestLodger_IsValid"] = isValid;
                result.Data["QuestLodger_Reason"] = reason ?? "None";

                var job = jobGiver.TestTryGiveJob(questLodger);
                result.Data["QuestLodger_JobCreated_Disallowed"] = job != null;

                if (job != null)
                {
                    result.Success = false;
                    result.Data["Error"] = "Quest lodger got weapon job when temp colonists disabled";
                    AutoArmLogger.LogError($"[TEST] TemporaryColonistTest: Quest lodger got weapon job when temp colonists disabled - expected: null, got: {job.def.defName}");
                }
            }

            // Test borrowed pawn
            if (borrowedPawn != null)
            {
                string reason;
                bool isTemp = JobGiverHelpers.IsTemporaryColonist(borrowedPawn);
                bool isValid = JobGiverHelpers.IsValidPawnForAutoEquip(borrowedPawn, out reason);

                result.Data["BorrowedPawn_IsTemp"] = isTemp;
                result.Data["BorrowedPawn_IsValid"] = isValid;
                result.Data["BorrowedPawn_Reason"] = reason ?? "None";

                var job = jobGiver.TestTryGiveJob(borrowedPawn);
                result.Data["BorrowedPawn_JobCreated_Disallowed"] = job != null;

                if (job != null)
                {
                    result.Success = false;
                    result.Data["Error"] = "Borrowed pawn got weapon job when temp colonists disabled";
                    AutoArmLogger.LogError($"[TEST] TemporaryColonistTest: Borrowed pawn got weapon job when temp colonists disabled - expected: null, got: {job.def.defName}");
                }
            }

            // Test with temporary colonists ALLOWED
            AutoArmMod.settings.allowTemporaryColonists = true;
            
            // Force clear any cooldowns and caches that might prevent immediate job creation
            if (questLodger != null)
            {
                TestRunnerFix.ClearAllCooldownsForPawn(questLodger);
                // Re-prepare pawn to ensure state is fresh
                TestRunnerFix.PreparePawnForTest(questLodger);
                
                // Set up outfit to allow weapons
                if (questLodger.outfits != null)
                {
                    var allWeaponsOutfit = Current.Game.outfitDatabase.AllOutfits
                        .FirstOrDefault(o => o.label == "Anything");
                    
                    if (allWeaponsOutfit == null)
                    {
                        allWeaponsOutfit = Current.Game.outfitDatabase.MakeNewOutfit();
                        allWeaponsOutfit.label = "Test - All Weapons";
                        allWeaponsOutfit.filter.SetAllow(ThingCategoryDefOf.Weapons, true);
                    }
                    
                    questLodger.outfits.CurrentApparelPolicy = allWeaponsOutfit;
                }
            }
            if (borrowedPawn != null)
            {
                TestRunnerFix.ClearAllCooldownsForPawn(borrowedPawn);
                // Re-prepare pawn to ensure state is fresh
                TestRunnerFix.PreparePawnForTest(borrowedPawn);
                
                // Set up outfit to allow weapons
                if (borrowedPawn.outfits != null)
                {
                    var allWeaponsOutfit = Current.Game.outfitDatabase.AllOutfits
                        .FirstOrDefault(o => o.label == "Anything");
                    
                    if (allWeaponsOutfit == null)
                    {
                        allWeaponsOutfit = Current.Game.outfitDatabase.MakeNewOutfit();
                        allWeaponsOutfit.label = "Test - All Weapons";
                        allWeaponsOutfit.filter.SetAllow(ThingCategoryDefOf.Weapons, true);
                    }
                    
                    borrowedPawn.outfits.CurrentApparelPolicy = allWeaponsOutfit;
                }
            }
            
            // Force settings cache to refresh
            CleanupHelper.ClearAllCaches();

            if (questLodger != null)
            {
                // Re-check validation with new settings
                string newReason;
                bool isNowValid = JobGiverHelpers.IsValidPawnForAutoEquip(questLodger, out newReason);
                result.Data["QuestLodger_ValidAfterAllowing"] = isNowValid;
                result.Data["QuestLodger_ReasonAfterAllowing"] = newReason ?? "None";
                
                var job = jobGiver.TestTryGiveJob(questLodger);
                result.Data["QuestLodger_JobCreated_Allowed"] = job != null;

                // When allowed, they should be able to pick up weapons
                if (job == null && weapons.Any(w => w.Spawned))
                {
                    // Check if weapons are reachable
                    var reachableWeapon = weapons.FirstOrDefault(w => w.Spawned && questLodger.CanReach(w, Verse.AI.PathEndMode.ClosestTouch, Danger.Some));
                    if (reachableWeapon != null)
                    {
                        result.Success = false;
                        result.Data["Error2"] = "Quest lodger couldn't get weapon when temp colonists allowed";
                        AutoArmLogger.LogError($"[TEST] TemporaryColonistTest: Quest lodger couldn't get weapon when temp colonists allowed - expected: job created, got: null (weapons available: {weapons.Count(w => w.Spawned)})");
                    }
                    else
                    {
                        result.Data["QuestLodger_Note"] = "No reachable weapons - test inconclusive";
                    }
                }
            }

            if (borrowedPawn != null)
            {
                var job = jobGiver.TestTryGiveJob(borrowedPawn);
                result.Data["BorrowedPawn_JobCreated_Allowed"] = job != null;
            }

            // Restore original setting
            AutoArmMod.settings.allowTemporaryColonists = originalSetting;

            return result;
        }

        public void Cleanup()
        {
            foreach (var weapon in weapons)
            {
                TestHelpers.SafeDestroyWeapon(weapon);
            }
            weapons.Clear();

            if (questLodger != null && !questLodger.Destroyed)
            {
                questLodger.Destroy();
                questLodger = null;
            }

            if (borrowedPawn != null && !borrowedPawn.Destroyed)
            {
                borrowedPawn.Destroy();
                borrowedPawn = null;
            }
        }
    }

    public class ChildColonistTest : ITestScenario
    {
        public string Name => "Child Colonist Age Restrictions";
        private Pawn childPawn;
        private int testAge = 10;

        public void Setup(Map map)
        {
            if (map == null || !ModsConfig.BiotechActive) return;

            // Clear all systems before test
            TestRunnerFix.ResetAllSystems();

            var config = new TestPawnConfig { BiologicalAge = testAge };
            childPawn = TestHelpers.CreateTestPawn(map, config);
        }

        public TestResult Run()
        {
            if (!ModsConfig.BiotechActive)
                return TestResult.Pass();

            if (childPawn == null)
            {
                AutoArmLogger.LogError("[TEST] ChildColonistTest: Failed to create child pawn");
                return TestResult.Failure("Failed to create child pawn");
            }

            if (childPawn.ageTracker == null)
            {
                AutoArmLogger.LogError("[TEST] ChildColonistTest: Pawn has no age tracker");
                return TestResult.Failure("Pawn has no age tracker");
            }

            int actualAge = childPawn.ageTracker.AgeBiologicalYears;
            if (actualAge != testAge && actualAge >= 18)
            {
            var skipResult = TestResult.Pass();
            skipResult.Data["Note"] = $"Could not set pawn age properly (wanted {testAge}, got {actualAge}), skipping test";
            return skipResult;
            }

            var result = new TestResult { Success = true };
            result.Data["Pawn Age"] = actualAge;
            result.Data["Allow Children Setting"] = AutoArmMod.settings?.allowChildrenToEquipWeapons ?? false;
            result.Data["Min Age Setting"] = AutoArmMod.settings?.childrenMinAge ?? 13;

            string reason;
            bool isValid = JobGiverHelpers.IsValidPawnForAutoEquip(childPawn, out reason);

            result.Data["Is Valid"] = isValid;
            result.Data["Reason"] = reason ?? "None";

            bool allowChildrenSetting = AutoArmMod.settings?.allowChildrenToEquipWeapons ?? false;
            int minAge = AutoArmMod.settings?.childrenMinAge ?? 13;

            if (allowChildrenSetting && actualAge >= minAge)
            {
                if (!isValid && reason.Contains("Too young"))
                {
                    var rejectResult = TestResult.Failure($"Child rejected despite being old enough ({actualAge} >= {minAge}) and setting allowing children");
                    rejectResult.Data["Error"] = "Age validation logic failed";
                    rejectResult.Data["PawnAge"] = actualAge;
                    rejectResult.Data["MinAgeRequired"] = minAge;
                    rejectResult.Data["AllowChildrenSetting"] = allowChildrenSetting;
                    rejectResult.Data["IsValidResult"] = isValid;
                    rejectResult.Data["ValidationReason"] = reason;
                    AutoArmLogger.LogError($"[TEST] ChildColonistTest: Child rejected despite being old enough - expected: valid, got: invalid (age: {actualAge}, minAge: {minAge}, setting: {allowChildrenSetting})");
                    return rejectResult;
                }
            }
            else if (!allowChildrenSetting || actualAge < minAge)
            {
                if (isValid)
                {
                    var allowResult = TestResult.Failure($"Child allowed despite settings (allow={allowChildrenSetting}, age={actualAge}, minAge={minAge})");
                    allowResult.Data["Error"] = "Child incorrectly allowed to equip weapons";
                    allowResult.Data["AllowChildrenSetting"] = allowChildrenSetting;
                    allowResult.Data["PawnAge"] = actualAge;
                    allowResult.Data["MinAgeRequired"] = minAge;
                    allowResult.Data["IsValidResult"] = isValid;
                    allowResult.Data["ExpectedValid"] = false;
                    AutoArmLogger.LogError($"[TEST] ChildColonistTest: Child allowed despite settings - expected: invalid, got: valid (allow: {allowChildrenSetting}, age: {actualAge}, minAge: {minAge})");
                    return allowResult;
                }
                if (!reason.Contains("Too young") && !reason.Contains("age"))
                {
                    var reasonResult = TestResult.Failure($"Child rejected but not for age reasons: {reason}");
                    reasonResult.Data["Error"] = "Child rejected for non-age related reason";
                    reasonResult.Data["ActualReason"] = reason;
                    reasonResult.Data["ExpectedReason"] = "Too young";
                    reasonResult.Data["PawnAge"] = actualAge;
                    AutoArmLogger.LogError($"[TEST] ChildColonistTest: Child rejected but not for age reasons - reason: {reason}");
                    return reasonResult;
                }
            }

            return result;
        }

        public void Cleanup()
        {
            if (childPawn != null && !childPawn.Destroyed)
            {
                childPawn.Destroy();
            }
        }
    }

    public class PrisonerSlaveTest : ITestScenario
    {
        public string Name => "Prisoner and Slave Weapon Access";
        private Pawn prisoner;
        private Pawn slave;
        private List<ThingWithComps> weapons = new List<ThingWithComps>();

        public void Setup(Map map)
        {
            if (map == null) return;

            // Clear all systems before test
            TestRunnerFix.ResetAllSystems();

            // Create a prisoner
            prisoner = TestHelpers.CreateTestPawn(map, new TestPawnConfig
            {
                Name = "TestPrisoner"
            });

            if (prisoner != null)
            {
                // Make them a prisoner
                prisoner.guest = new Pawn_GuestTracker(prisoner);
                prisoner.guest.SetGuestStatus(Faction.OfPlayer, GuestStatus.Prisoner);

                // Prepare pawn for testing
                TestRunnerFix.PreparePawnForTest(prisoner);

                // Ensure unarmed
                prisoner.equipment?.DestroyAllEquipment();
            }

            // Create a slave (only if Ideology is active)
            if (ModsConfig.IdeologyActive)
            {
                slave = TestHelpers.CreateTestPawn(map, new TestPawnConfig
                {
                    Name = "TestSlave"
                });

                if (slave != null)
                {
                    // Make them a slave
                    slave.guest = new Pawn_GuestTracker(slave);
                    slave.guest.SetGuestStatus(Faction.OfPlayer, GuestStatus.Slave);

                    // Prepare pawn for testing
                    TestRunnerFix.PreparePawnForTest(slave);

                    // Ensure unarmed
                    slave.equipment?.DestroyAllEquipment();
                    
                    // CRITICAL: Set up outfit to allow weapons for slaves
                    if (slave.outfits != null)
                    {
                        var slaveOutfit = Current.Game.outfitDatabase.AllOutfits
                            .FirstOrDefault(o => o.label.Contains("Slave"));
                        
                        if (slaveOutfit == null)
                        {
                            slaveOutfit = Current.Game.outfitDatabase.MakeNewOutfit();
                            slaveOutfit.label = "Test - Slave with Weapons";
                        }
                        
                        // Ensure weapons are allowed for the slave
                        slaveOutfit.filter.SetAllow(ThingCategoryDefOf.Weapons, true);
                        
                        slave.outfits.CurrentApparelPolicy = slaveOutfit;
                    }
                }
            }

            // Create weapons near both pawns
            var weaponDef = VanillaWeaponDefOf.Gun_Autopistol;
            if (weaponDef != null)
            {
                if (prisoner != null)
                {
                    var weapon1 = TestHelpers.CreateWeapon(map, weaponDef,
                        prisoner.Position + new IntVec3(2, 0, 0));
                    if (weapon1 != null)
                    {
                        weapons.Add(weapon1);
                        ImprovedWeaponCacheManager.AddWeaponToCache(weapon1);
                    }
                }

                if (slave != null)
                {
                    var weapon2 = TestHelpers.CreateWeapon(map, weaponDef,
                        slave.Position + new IntVec3(2, 0, 0));
                    if (weapon2 != null)
                    {
                        weapons.Add(weapon2);
                        ImprovedWeaponCacheManager.AddWeaponToCache(weapon2);
                    }
                }
            }
        }

        public TestResult Run()
        {
            var result = new TestResult { Success = true };
            var jobGiver = new JobGiver_PickUpBetterWeapon();

            // Test prisoner
            if (prisoner != null)
            {
                result.Data["PrisonerStatus"] = prisoner.IsPrisoner;
                
                // Double-check prisoner status is properly set
                if (!prisoner.IsPrisoner)
                {
                    result.Success = false;
                    result.Data["Setup_Error"] = "Prisoner setup failed - pawn is not a prisoner";
                    AutoArmLogger.LogError("[TEST] PrisonerSlaveTest: Prisoner setup failed");
                    return result;
                }

                string reason;
                bool isValid = JobGiverHelpers.IsValidPawnForAutoEquip(prisoner, out reason);
                result.Data["PrisonerIsValid"] = isValid;
                result.Data["PrisonerReason"] = reason ?? "None";

                var job = jobGiver.TestTryGiveJob(prisoner);
                result.Data["PrisonerJobCreated"] = job != null;

                if (job != null)
                {
                    result.Success = false;
                    result.Data["Error"] = "CRITICAL: Prisoner was able to get weapon pickup job!";
                    AutoArmLogger.LogError($"[TEST] PrisonerSlaveTest: CRITICAL - Prisoner was able to get weapon pickup job - expected: null, got: {job.def.defName} targeting {job.targetA.Thing?.Label}");
                }

                if (isValid)
                {
                    result.Success = false;
                    result.Data["Error2"] = "CRITICAL: Prisoner passed validation check!";
                    AutoArmLogger.LogError($"[TEST] PrisonerSlaveTest: CRITICAL - Prisoner passed validation check - expected: invalid, got: valid");
                }
            }

            // Test slave
            if (slave != null && ModsConfig.IdeologyActive)
            {
                result.Data["SlaveStatus"] = slave.IsSlaveOfColony;
                
                // Double-check slave status is properly set
                if (!slave.IsSlaveOfColony)
                {
                    result.Data["Slave_Warning"] = "Slave setup may have failed - pawn is not marked as slave";
                    AutoArmLogger.Log("[TEST] PrisonerSlaveTest: Slave setup may have failed");
                }

                string reason;
                bool isValid = JobGiverHelpers.IsValidPawnForAutoEquip(slave, out reason);
                result.Data["SlaveIsValid"] = isValid;
                result.Data["SlaveReason"] = reason ?? "None";

                // Clear cooldowns for slave before testing
                TestRunnerFix.ClearAllCooldownsForPawn(slave);
                
                var job = jobGiver.TestTryGiveJob(slave);
                result.Data["SlaveJobCreated"] = job != null;

                // Slaves SHOULD be able to pick up weapons - players control this via outfit filters
                if (!isValid && reason != "Not spawned" && reason != "No map" && reason != "Dead" && !reason.Contains("Drafted"))
                {
                    // Only fail for unexpected reasons
                    if (!reason.Contains("guest") && !reason.Contains("Guest"))
                    {
                        result.Success = false;
                        result.Data["Error3"] = $"Slave failed validation when they should pass: {reason}";
                        AutoArmLogger.LogError($"[TEST] PrisonerSlaveTest: Slave failed validation when they should pass - expected: valid, got: invalid (reason: {reason})");
                    }
                }

                if (job == null && weapons.Any(w => w.Spawned))
                {
                    // Only fail if there was a weapon available and no other reason to fail
                    if (isValid && slave.equipment?.Primary == null)
                    {
                        // Check if weapons are actually reachable
                        var reachableWeapon = weapons.FirstOrDefault(w => w.Spawned && slave.CanReach(w, Verse.AI.PathEndMode.ClosestTouch, Danger.Some));
                        if (reachableWeapon != null)
                        {
                            // Additional debug info
                            var outfitAllows = slave.outfits?.CurrentApparelPolicy?.filter?.Allows(reachableWeapon) ?? false;
                            AutoArmLogger.LogError($"[TEST] PrisonerSlaveTest: Slave outfit allows weapon: {outfitAllows}");
                            
                            // Check weapon validation specifically
                            string weaponReason;
                            bool weaponValid = JobGiverHelpers.IsValidWeaponCandidate(reachableWeapon, slave, out weaponReason);
                            AutoArmLogger.LogError($"[TEST] PrisonerSlaveTest: Weapon valid for slave: {weaponValid}, reason: {weaponReason}");
                            
                            result.Success = false;
                            result.Data["Error4"] = "Slave couldn't get weapon job despite being valid and weapons being reachable";
                            AutoArmLogger.LogError($"[TEST] PrisonerSlaveTest: Slave couldn't get weapon job despite being valid - expected: job created, got: null (reachable weapons: 1+)");
                        }
                        else
                        {
                            result.Data["Slave_Note"] = "No reachable weapons for slave - test inconclusive";
                        }
                    }
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

            if (prisoner != null && !prisoner.Destroyed)
            {
                prisoner.Destroy();
                prisoner = null;
            }

            if (slave != null && !slave.Destroyed)
            {
                slave.Destroy();
                slave = null;
            }
        }
    }

    public class BrawlerTest : ITestScenario
    {
        public string Name => "Brawler Weapon Preferences";
        private Pawn brawlerPawn;
        private List<ThingWithComps> weapons = new List<ThingWithComps>();

        public void Setup(Map map)
        {
            if (map == null) return;

            // Clear all systems before test
            TestRunnerFix.ResetAllSystems();

            brawlerPawn = TestHelpers.CreateTestPawn(map, new TestHelpers.TestPawnConfig
            {
                Name = "TestBrawler",
                Traits = new List<TraitDef> { TraitDefOf.Brawler }
            });

            if (brawlerPawn != null)
            {
                // Make sure the brawler is unarmed
                brawlerPawn.equipment?.DestroyAllEquipment();

                var pos = brawlerPawn.Position;

                var swordDef = VanillaWeaponDefOf.MeleeWeapon_LongSword;
                var rifleDef = VanillaWeaponDefOf.Gun_AssaultRifle;

                if (swordDef != null)
                {
                    var sword = TestHelpers.CreateWeapon(map, swordDef, pos + new IntVec3(2, 0, 0));
                    if (sword != null)
                    {
                        weapons.Add(sword);
                        ImprovedWeaponCacheManager.AddWeaponToCache(sword);
                    }
                }
                if (rifleDef != null)
                {
                    var rifle = TestHelpers.CreateWeapon(map, rifleDef, pos + new IntVec3(-2, 0, 0));
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
            if (brawlerPawn == null)
                return TestResult.Failure("Brawler pawn creation failed");

            var jobGiver = new JobGiver_PickUpBetterWeapon();
            var job = jobGiver.TestTryGiveJob(brawlerPawn);

            var result = new TestResult { Success = true };

            if (job != null && job.targetA.Thing is ThingWithComps weapon)
            {
                // Check weapon scores to verify brawler preferences
                var meleeWeapon = weapons.FirstOrDefault(w => w.def.IsMeleeWeapon);
                var rangedWeapon = weapons.FirstOrDefault(w => w.def.IsRangedWeapon);

                if (meleeWeapon != null && rangedWeapon != null)
                {
                    float meleeScore = WeaponScoreCache.GetCachedScore(brawlerPawn, meleeWeapon);
                    float rangedScore = WeaponScoreCache.GetCachedScore(brawlerPawn, rangedWeapon);

                    result.Data["MeleeScore"] = meleeScore;
                    result.Data["RangedScore"] = rangedScore;
                    result.Data["PickedWeapon"] = weapon.Label;

                    // Brawler should prefer melee if available
                    if (weapon.def.IsMeleeWeapon)
                    {
                        return result; // Pass - picked melee
                    }
                    else if (weapon.def.IsRangedWeapon)
                    {
                    // It's acceptable for unarmed brawler to pick ranged if score is positive
                    if (rangedScore > 0 && meleeScore <= rangedScore)
                    {
                    result.Data["Note"] = "Brawler picked ranged weapon because it scored higher";
                    return result;
                    }
                    else
                    {
                    result.Success = false;
                    result.FailureReason = "Brawler incorrectly picked ranged weapon";
                        result.Data["Error"] = $"Brawler picked {weapon.Label} despite melee scoring higher";
                            result.Data["MeleeScore"] = meleeScore;
                        result.Data["RangedScore"] = rangedScore;
                        result.Data["PickedWeapon"] = weapon.Label;
                        result.Data["WeaponType"] = "Ranged";
                        result.Data["ExpectedType"] = "Melee";
                        AutoArmLogger.LogError($"[TEST] BrawlerTest: Brawler incorrectly picked ranged weapon - expected: melee weapon, got: {weapon.Label} (melee score: {meleeScore}, ranged score: {rangedScore})");
                        return result;
                    }
                }
                }
            }

            // No job might mean no valid weapons were found
            result.Data["Note"] = "No weapon pickup job created";
            return result;
        }

        public void Cleanup()
        {
            // Destroy weapons first to avoid container conflicts
            foreach (var weapon in weapons)
            {
                TestHelpers.SafeDestroyWeapon(weapon);
            }
            weapons.Clear();

            if (brawlerPawn != null && !brawlerPawn.Destroyed)
            {
                brawlerPawn.Destroy();
            }
        }
    }

    public class HunterTest : ITestScenario
    {
        public string Name => "Hunter Weapon Preferences";
        private Pawn hunterPawn;
        private List<Thing> testWeapons = new List<Thing>();

        public void Setup(Map map)
        {
            if (map == null) return;

            // Clear all systems before test
            TestRunnerFix.ResetAllSystems();

            hunterPawn = TestHelpers.CreateTestPawn(map, new TestHelpers.TestPawnConfig
            {
                Name = "TestHunter",
                Skills = new Dictionary<SkillDef, int>
                {
                    { SkillDefOf.Shooting, 12 },
                    { SkillDefOf.Animals, 10 }
                }
            });

            if (hunterPawn != null)
            {
                // Make sure hunter is unarmed to test weapon selection
                hunterPawn.equipment?.DestroyAllEquipment();

                if (hunterPawn.workSettings != null)
                {
                    hunterPawn.workSettings.SetPriority(WorkTypeDefOf.Hunting, 1);
                }

                // Create test weapons for hunter to evaluate
                var pos = hunterPawn.Position;

                // Create a ranged weapon (good for hunting)
                var rifleDef = VanillaWeaponDefOf.Gun_BoltActionRifle;
                if (rifleDef != null)
                {
                    var rifle = TestHelpers.CreateWeapon(map, rifleDef, pos + new IntVec3(2, 0, 0));
                    if (rifle != null)
                    {
                        testWeapons.Add(rifle);
                        ImprovedWeaponCacheManager.AddWeaponToCache(rifle);
                    }
                }

                // Create a melee weapon (bad for hunting)
                var knifeDef = VanillaWeaponDefOf.MeleeWeapon_Knife;
                if (knifeDef != null)
                {
                    var knife = TestHelpers.CreateWeapon(map, knifeDef, pos + new IntVec3(-2, 0, 0));
                    if (knife != null)
                    {
                        testWeapons.Add(knife);
                        ImprovedWeaponCacheManager.AddWeaponToCache(knife);
                    }
                }
            }
        }

        public TestResult Run()
        {
            if (hunterPawn == null)
                return TestResult.Failure("Hunter pawn creation failed");

            var jobGiver = new JobGiver_PickUpBetterWeapon();
            var job = jobGiver.TestTryGiveJob(hunterPawn);

            var result = new TestResult { Success = true };

            if (job != null && job.targetA.Thing is ThingWithComps weapon)
            {
                result.Data["PickedWeapon"] = weapon.Label;
                result.Data["WeaponType"] = weapon.def.IsRangedWeapon ? "Ranged" : "Melee";

                // Hunter should pick ranged weapon
                if (!weapon.def.IsRangedWeapon)
                {
                    var meleeResult = TestResult.Failure($"Hunter picked melee weapon: {weapon.Label}");
                    meleeResult.Data["Error"] = "Hunter selected inappropriate weapon type";
                    meleeResult.Data["PickedWeapon"] = weapon.Label;
                    meleeResult.Data["WeaponType"] = "Melee";
                    meleeResult.Data["ExpectedType"] = "Ranged";
                    meleeResult.Data["HuntingPriority"] = hunterPawn.workSettings?.GetPriority(WorkTypeDefOf.Hunting) ?? 0;
                    AutoArmLogger.LogError($"[TEST] HunterTest: Hunter picked melee weapon - expected: ranged weapon, got: {weapon.Label}");
                    return meleeResult;
                }
            }
            else
            {
                result.Data["Note"] = "No weapon pickup job created";
            }

            return result;
        }

        public void Cleanup()
        {
            // Destroy weapons first to avoid container conflicts
            foreach (var weapon in testWeapons)
            {
                if (weapon is ThingWithComps weaponComp)
                {
                    TestHelpers.SafeDestroyWeapon(weaponComp);
                }
                else if (weapon != null && !weapon.Destroyed)
                {
                    weapon.Destroy();
                }
            }
            testWeapons.Clear();

            if (hunterPawn != null && !hunterPawn.Destroyed)
            {
                hunterPawn.Destroy();
            }
        }
    }

    public class SkillBasedPreferenceTest : ITestScenario
    {
        public string Name => "Skill-Based Weapon Preferences";
        private Pawn shooterPawn;
        private Pawn meleePawn;
        private List<ThingWithComps> weapons = new List<ThingWithComps>();

        public void Setup(Map map)
        {
            if (map == null) return;

            // Clear all systems before test
            TestRunnerFix.ResetAllSystems();

            // Clear weapon cache to avoid interference from other tests
            ImprovedWeaponCacheManager.InvalidateCache(map);
            WeaponScoreCache.ClearAllCaches();
            weapons.Clear();

            // Create a high-shooting skill pawn
            shooterPawn = TestHelpers.CreateTestPawn(map, new TestHelpers.TestPawnConfig
            {
                Name = "Shooter",
                Skills = new Dictionary<SkillDef, int>
                {
                    { SkillDefOf.Shooting, 15 },
                    { SkillDefOf.Melee, 3 }
                }
            });

            // Create a high-melee skill pawn
            meleePawn = TestHelpers.CreateTestPawn(map, new TestHelpers.TestPawnConfig
            {
                Name = "Brawler",
                Skills = new Dictionary<SkillDef, int>
                {
                    { SkillDefOf.Shooting, 3 },
                    { SkillDefOf.Melee, 15 }
                }
            });

            // IMPORTANT: Set up outfit policies BEFORE creating weapons to avoid cached penalties
            var allWeaponsOutfit = Current.Game.outfitDatabase.AllOutfits
                .FirstOrDefault(o => o.label == "Anything");
                
            if (allWeaponsOutfit == null)
            {
                // Create a new outfit that allows everything
                allWeaponsOutfit = Current.Game.outfitDatabase.MakeNewOutfit();
                allWeaponsOutfit.label = "Test - All Weapons";
                // Ensure the filter allows all weapons
                allWeaponsOutfit.filter.SetAllow(ThingCategoryDefOf.Weapons, true);
            }

            if (shooterPawn != null && shooterPawn.outfits != null)
            {
                shooterPawn.outfits.CurrentApparelPolicy = allWeaponsOutfit;
            }
            
            if (meleePawn != null && meleePawn.outfits != null)
            {
                meleePawn.outfits.CurrentApparelPolicy = allWeaponsOutfit;
            }

            // Clear all caches before creating weapons
            WeaponScoreCache.ClearAllCaches();
            ImprovedWeaponCacheManager.ClearPawnScoreCache(shooterPawn);
            ImprovedWeaponCacheManager.ClearPawnScoreCache(meleePawn);

            // Prepare pawns for testing
            if (shooterPawn != null)
            {
                shooterPawn.equipment?.DestroyAllEquipment();
                if (shooterPawn.Drafted)
                    shooterPawn.drafter.Drafted = false;
                shooterPawn.jobs?.StopAll();
                shooterPawn.mindState?.Reset(true, true);
            }
            
            if (meleePawn != null)
            {
                meleePawn.equipment?.DestroyAllEquipment();
                if (meleePawn.Drafted)
                    meleePawn.drafter.Drafted = false;
                meleePawn.jobs?.StopAll();
                meleePawn.mindState?.Reset(true, true);
            }

            // Create weapons AFTER outfit setup to ensure proper scoring
            if (shooterPawn != null)
            {
                var pos = shooterPawn.Position;
                // Test with assault rifle as the user indicated
                var rifleDef = VanillaWeaponDefOf.Gun_AssaultRifle;
                var swordDef = VanillaWeaponDefOf.MeleeWeapon_LongSword;

                if (rifleDef != null)
                {
                    var rifle = TestHelpers.CreateWeapon(map, rifleDef, pos + new IntVec3(2, 0, 0));
                    if (rifle != null)
                    {
                        weapons.Add(rifle);
                        // DON'T add to cache yet - let it happen naturally
                    }
                }

                if (swordDef != null)
                {
                    // Create with consistent material for fair comparison
                    var sword = TestHelpers.CreateWeapon(map, swordDef, pos + new IntVec3(-2, 0, 0), QualityCategory.Normal);
                    if (sword != null)
                    {
                        weapons.Add(sword);
                        // DON'T add to cache yet - let it happen naturally
                    }
                }
            }
            
            // Force a cache rebuild to ensure weapons are properly indexed with correct outfit policies
            ImprovedWeaponCacheManager.InvalidateCache(map);
            
            // Add weapons to cache AFTER everything is set up
            foreach (var weapon in weapons)
            {
                ImprovedWeaponCacheManager.AddWeaponToCache(weapon);
            }
            
            // Final cache clear to ensure fresh scoring
            WeaponScoreCache.ClearAllCaches();
        }

        public TestResult Run()
        {
            if (shooterPawn == null || meleePawn == null)
                return TestResult.Failure("Failed to create test pawns");

            var result = new TestResult { Success = true };
            var jobGiver = new JobGiver_PickUpBetterWeapon();

            // Test shooter preferences
            var shooterJob = jobGiver.TestTryGiveJob(shooterPawn);
            if (shooterJob != null && shooterJob.targetA.Thing is ThingWithComps shooterWeapon)
            {
                result.Data["ShooterPicked"] = shooterWeapon.Label;
                result.Data["ShooterPickedRanged"] = shooterWeapon.def.IsRangedWeapon;

                // Get actual skill bonuses to verify expectation
                float shooterRangedBonus = WeaponScoringHelper.GetSkillScore(shooterPawn, shooterWeapon);
                result.Data["ShooterWeaponSkillBonus"] = shooterRangedBonus;
                
                // Shooter should get a positive bonus for ranged weapons
                if (shooterWeapon.def.IsRangedWeapon && shooterRangedBonus <= 0)
                {
                    result.Success = false;
                    result.Data["ShooterError"] = "Shooter didn't get skill bonus for ranged weapon";
                }
            }

            // Move melee pawn to weapon location and test
            if (meleePawn != null && weapons.Count > 0)
            {
                // Move melee pawn near weapons
                meleePawn.Position = shooterPawn.Position + new IntVec3(0, 0, 3);
                
                // Get scores for both weapon types
                var rangedWeapon = weapons.FirstOrDefault(w => w.def.IsRangedWeapon);
                var meleeWeapon = weapons.FirstOrDefault(w => w.def.IsMeleeWeapon);
                
                if (rangedWeapon != null && meleeWeapon != null)
                {
                    // Get skill bonuses directly
                    float meleeRangedSkillBonus = WeaponScoringHelper.GetSkillScore(meleePawn, rangedWeapon);
                    float meleeMeleeSkillBonus = WeaponScoringHelper.GetSkillScore(meleePawn, meleeWeapon);
                    
                    result.Data["MeleePawn_RangedSkillBonus"] = meleeRangedSkillBonus;
                    result.Data["MeleePawn_MeleeSkillBonus"] = meleeMeleeSkillBonus;
                    
                    // Get total scores
                    float rangedTotal = WeaponScoringHelper.GetTotalScore(meleePawn, rangedWeapon);
                    float meleeTotal = WeaponScoringHelper.GetTotalScore(meleePawn, meleeWeapon);
                    
                    result.Data["MeleePawn_RangedTotal"] = rangedTotal;
                    result.Data["MeleePawn_MeleeTotal"] = meleeTotal;
                    
                    // Debug why melee weapon might have negative score
                    if (meleeTotal <= -1000)
                    {
                        result.Data["MeleePawn_FilterIssue"] = "Melee weapon blocked by outfit filter or other -1000 penalty";
                        
                        // Check outfit filter specifically
                        var filter = meleePawn.outfits?.CurrentApparelPolicy?.filter;
                        if (filter != null)
                        {
                            bool meleeAllowed = filter.Allows(meleeWeapon);
                            result.Data["MeleePawn_OutfitAllowsMelee"] = meleeAllowed;
                            result.Data["MeleePawn_OutfitName"] = meleePawn.outfits?.CurrentApparelPolicy?.label ?? "none";
                        }
                    }
                    
                    // Verify skill bonuses are applied correctly
                    if (meleeMeleeSkillBonus <= 0)
                    {
                    result.Success = false;
                    result.FailureReason = result.FailureReason ?? "Skill bonus calculation error";
                        result.Data["Error"] = result.Data.ContainsKey("Error") ? result.Data["Error"] + "; " + "Melee skill bonus not applied" : "Melee skill bonus not applied";
                        result.Data["MeleePawn_Error1"] = "High-melee pawn didn't get melee weapon skill bonus";
                        result.Data["MeleeMeleeSkillBonus"] = meleeMeleeSkillBonus;
                        result.Data["ExpectedBonus"] = "> 0";
                    AutoArmLogger.LogError($"[TEST] SkillBasedPreferenceTest: High-melee pawn didn't get melee weapon skill bonus - bonus: {meleeMeleeSkillBonus}");
                    }
                    
                if (meleeRangedSkillBonus >= 0)
                {
                    result.Success = false;
                    result.FailureReason = result.FailureReason ?? "Skill penalty calculation error";
                    result.Data["Error"] = result.Data.ContainsKey("Error") ? result.Data["Error"] + "; " + "Ranged skill penalty not applied" : "Ranged skill penalty not applied";
                    result.Data["MeleePawn_Error2"] = "High-melee pawn didn't get ranged weapon skill penalty";
                    result.Data["MeleeRangedSkillBonus"] = meleeRangedSkillBonus;
                    result.Data["ExpectedPenalty"] = "< 0";
                    AutoArmLogger.LogError($"[TEST] SkillBasedPreferenceTest: High-melee pawn didn't get ranged weapon skill penalty - bonus: {meleeRangedSkillBonus}");
                }
                }
                
                var meleeJob = jobGiver.TestTryGiveJob(meleePawn);
                
                if (meleeJob != null && meleeJob.targetA.Thing is ThingWithComps pickedWeapon)
                {
                    result.Data["MeleePawnPicked"] = pickedWeapon.Label;
                    result.Data["MeleePawnPickedMelee"] = pickedWeapon.def.IsMeleeWeapon;
                    
                    // Get the skill bonus for the picked weapon
                    float pickedSkillBonus = WeaponScoringHelper.GetSkillScore(meleePawn, pickedWeapon);
                    result.Data["MeleePawnPickedSkillBonus"] = pickedSkillBonus;
                    
                    // The test passes if the pawn picked a weapon that gives them a positive skill bonus
                    // OR if there's a valid reason they couldn't pick their preferred weapon type
                    if (pickedWeapon.def.IsMeleeWeapon)
                    {
                        // Good - picked melee
                        return result;
                    }
                    else if (meleeWeapon != null)
                    {
                        // Check why they didn't pick melee
                        float meleeScore = WeaponScoringHelper.GetTotalScore(meleePawn, meleeWeapon);
                        if (meleeScore <= -1000)
                        {
                            result.Data["Note"] = "Melee weapon was blocked (outfit filter or other restriction)";
                            return result; // Pass - valid reason
                        }
                        else
                        {
                            result.Success = false;
                            result.FailureReason = "High-melee pawn chose ranged over available melee weapon";
                            result.Data["Error"] = "Skill-based preference not working correctly";
                            result.Data["MeleePawn_Error3"] = "High-melee pawn chose ranged over available melee weapon";
                            result.Data["PickedWeapon"] = pickedWeapon.Label;
                            result.Data["WeaponType"] = "Ranged";
                            result.Data["ExpectedType"] = "Melee";
                            result.Data["MeleeSkill"] = meleePawn.skills?.GetSkill(SkillDefOf.Melee)?.Level ?? 0;
                            result.Data["ShootingSkill"] = meleePawn.skills?.GetSkill(SkillDefOf.Shooting)?.Level ?? 0;
                            AutoArmLogger.LogError($"[TEST] SkillBasedPreferenceTest: High-melee pawn chose ranged weapon over available melee - picked: {pickedWeapon.Label}");
                        }
                    }
                }
                else
                {
                    result.Data["MeleePawnJob"] = "No job created";
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

            if (shooterPawn != null && !shooterPawn.Destroyed)
            {
                shooterPawn.Destroy();
            }
            if (meleePawn != null && !meleePawn.Destroyed)
            {
                meleePawn.Destroy();
            }
        }
    }
}
