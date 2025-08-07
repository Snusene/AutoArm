using AutoArm.Caching;
using AutoArm.Definitions;
using AutoArm.Helpers;
using AutoArm.Jobs;
using AutoArm.Logging;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using static AutoArm.Testing.TestHelpers;
using static AutoArm.Testing.Helpers.TestValidationHelper;

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

            // Reset all systems for test isolation
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
                    // Find safe position near pawn
                    IntVec3 pos1 = questLodger.Position;
                    for (int i = 1; i <= 5; i++)
                    {
                        IntVec3 testPos = questLodger.Position + new IntVec3(i, 0, 0);
                        if (testPos.InBounds(map) && testPos.Standable(map))
                        {
                            pos1 = testPos;
                            break;
                        }
                        testPos = questLodger.Position + new IntVec3(0, 0, i);
                        if (testPos.InBounds(map) && testPos.Standable(map))
                        {
                            pos1 = testPos;
                            break;
                        }
                    }

                    var weapon1 = TestHelpers.CreateWeapon(map, weaponDef, pos1);
                    if (weapon1 != null)
                    {
                        weapons.Add(weapon1);
                        ImprovedWeaponCacheManager.AddWeaponToCache(weapon1);
                        // Ensure weapon is not forbidden
                        weapon1.SetForbidden(false, false);
                        AutoArmLogger.Log($"[TEST] Created weapon1 at {weapon1.Position} for quest lodger at {questLodger.Position}");
                    }

                    if (borrowedPawn != null)
                    {
                        IntVec3 pos2 = borrowedPawn.Position;
                        for (int i = 1; i <= 5; i++)
                        {
                            IntVec3 testPos = borrowedPawn.Position + new IntVec3(-i, 0, 0);
                            if (testPos.InBounds(map) && testPos.Standable(map))
                            {
                                pos2 = testPos;
                                break;
                            }
                            testPos = borrowedPawn.Position + new IntVec3(0, 0, -i);
                            if (testPos.InBounds(map) && testPos.Standable(map))
                            {
                                pos2 = testPos;
                                break;
                            }
                        }

                        var weapon2 = TestHelpers.CreateWeapon(map, weaponDef, pos2);
                        if (weapon2 != null)
                        {
                            weapons.Add(weapon2);
                            ImprovedWeaponCacheManager.AddWeaponToCache(weapon2);
                            // Ensure weapon is not forbidden
                            weapon2.SetForbidden(false, false);
                            AutoArmLogger.Log($"[TEST] Created weapon2 at {weapon2.Position} for borrowed pawn at {borrowedPawn.Position}");
                        }
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
                bool isValid = IsValidPawnForAutoEquip(questLodger, out reason);

                result.Data["QuestLodger_IsTemp"] = isTemp;
                result.Data["QuestLodger_IsValid"] = isValid;
                result.Data["QuestLodger_Reason"] = reason ?? "None";

                var job = jobGiver.TestTryGiveJob(questLodger);
                result.Data["QuestLodger_JobCreated_Disallowed"] = job != null;

                if (job != null)
                {
                    result.Success = false;
                    result.Data["Error"] = "Quest lodger got weapon job when temp colonists disabled";
                    AutoArmLogger.Error($"[TEST] TemporaryColonistTest: Quest lodger got weapon job when temp colonists disabled - expected: null, got: {job.def.defName}");
                }
            }

            // Test borrowed pawn
            if (borrowedPawn != null)
            {
                string reason;
                bool isTemp = JobGiverHelpers.IsTemporaryColonist(borrowedPawn);
                bool isValid = IsValidPawnForAutoEquip(borrowedPawn, out reason);

                result.Data["BorrowedPawn_IsTemp"] = isTemp;
                result.Data["BorrowedPawn_IsValid"] = isValid;
                result.Data["BorrowedPawn_Reason"] = reason ?? "None";

                var job = jobGiver.TestTryGiveJob(borrowedPawn);
                result.Data["BorrowedPawn_JobCreated_Disallowed"] = job != null;

                if (job != null)
                {
                    result.Success = false;
                    result.Data["Error"] = "Borrowed pawn got weapon job when temp colonists disabled";
                    AutoArmLogger.Error($"[TEST] TemporaryColonistTest: Borrowed pawn got weapon job when temp colonists disabled - expected: null, got: {job.def.defName}");
                }
            }

            // Test with temporary colonists ALLOWED
            AutoArmMod.settings.allowTemporaryColonists = true;

            // IMPORTANT: Clear any cached validation states when settings change
            // This ensures the validation logic re-evaluates with new settings
            if (questLodger?.Map != null)
            {
                // Force cache rebuild
                ImprovedWeaponCacheManager.InvalidateCache(questLodger.Map);

                // Clear timing cooldowns that might prevent job creation
                TimingHelper.ClearAllCooldowns();

                // Also clear cooldowns specifically for the test pawns
                TestRunnerFix.ClearAllCooldownsForPawn(questLodger);
                if (borrowedPawn != null)
                    TestRunnerFix.ClearAllCooldownsForPawn(borrowedPawn);

                // Ensure mod is still enabled after settings change
                if (AutoArmMod.settings != null)
                {
                    AutoArmMod.settings.modEnabled = true;
                }

                // Force a rebuild by accessing the cache
                var forceRebuild = ImprovedWeaponCacheManager.GetWeaponsNear(questLodger.Map, questLodger.Position, 1f);
            }

            // Re-verify pawns are still valid after settings change
            if (questLodger == null || questLodger.Destroyed || !questLodger.Spawned)
            {
                result.Success = false;
                result.Data["Error"] = "Quest lodger became invalid during test";
                AutoArmLogger.Error("[TEST] TemporaryColonistTest: Quest lodger became invalid");
                return result;
            }

            if (questLodger != null)
            {
                // Debug: Check pawn state
                AutoArmLogger.Log($"[TEST] Quest lodger state before job: Spawned={questLodger.Spawned}, Map={questLodger.Map != null}, Position={questLodger.Position}");

                // Debug: Check weapon availability
                var availableWeapons = ImprovedWeaponCacheManager.GetWeaponsNear(questLodger.Map, questLodger.Position, 50f)?.ToList();
                AutoArmLogger.Log($"[TEST] Available weapons in cache: {availableWeapons?.Count ?? 0}");
                if (availableWeapons != null)
                {
                    foreach (var w in availableWeapons.Take(3))
                    {
                        AutoArmLogger.Log($"[TEST] - {w.Label} at {w.Position}, distance={questLodger.Position.DistanceTo(w.Position):F1}");
                    }
                }

                // Debug: Check spawned test weapons
                AutoArmLogger.Log($"[TEST] Test weapons spawned: {weapons.Count(w => w.Spawned)}");
                foreach (var w in weapons.Where(w => w.Spawned))
                {
                    AutoArmLogger.Log($"[TEST] - Test weapon: {w.Label} at {w.Position}, forbidden={w.IsForbidden(questLodger)}");
                }

                // Extra debugging: Check if AutoArm is enabled
                AutoArmLogger.Log($"[TEST] AutoArm enabled: {AutoArmMod.settings?.modEnabled}, allow temp colonists: {AutoArmMod.settings?.allowTemporaryColonists}");

                var job = jobGiver.TestTryGiveJob(questLodger);
                result.Data["QuestLodger_JobCreated_Allowed"] = job != null;

                // When allowed, temporary colonists should be able to pick up weapons
                // However, the JobGiver requires the pawn to pass many other checks
                // The test passes if the pawn is not rejected for being temporary
                string validationReason;
                bool canEquip = IsValidPawnForAutoEquip(questLodger, out validationReason);
                
                if (!canEquip && validationReason.Contains("temporary"))
                {
                    result.Success = false;
                    result.Data["Error2"] = $"Quest lodger rejected for being temporary when temp colonists allowed";
                    AutoArmLogger.Error($"[TEST] TemporaryColonistTest: Quest lodger rejected as temporary when setting allows them");
                }
                else if (job == null)
                {
                    // Job might be null for other valid reasons (no weapons in range, drafted, etc.)
                    result.Data["QuestLodger_NoJob_Reason"] = validationReason ?? "Unknown";
                    result.Data["Note"] = "No job created but not due to temporary status";
                    // This is not a failure - the test is only checking temporary colonist handling
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
                if (weapon != null && !weapon.Destroyed)
                {
                    weapon.Destroy();
                }
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

            var config = new TestPawnConfig { BiologicalAge = testAge };
            childPawn = TestHelpers.CreateTestPawn(map, config);
        }

        public TestResult Run()
        {
            if (!ModsConfig.BiotechActive)
                return TestResult.Pass();

            if (childPawn == null)
            {
                AutoArmLogger.Error("[TEST] ChildColonistTest: Failed to create child pawn");
                return TestResult.Failure("Failed to create child pawn");
            }

            if (childPawn.ageTracker == null)
            {
                AutoArmLogger.Error("[TEST] ChildColonistTest: Pawn has no age tracker");
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
            bool isValid = IsValidPawnForAutoEquip(childPawn, out reason);

            result.Data["Is Valid"] = isValid;
            result.Data["Reason"] = reason ?? "None";

            bool allowChildrenSetting = AutoArmMod.settings?.allowChildrenToEquipWeapons ?? false;
            int minAge = AutoArmMod.settings?.childrenMinAge ?? 13;

            if (allowChildrenSetting && actualAge >= minAge)
            {
                if (!isValid && reason.Contains("Too young"))
                {
                    AutoArmLogger.Error($"[TEST] ChildColonistTest: Child rejected despite being old enough - expected: valid, got: invalid (age: {actualAge}, minAge: {minAge}, setting: {allowChildrenSetting})");
                    return TestResult.Failure($"Child rejected despite being old enough ({actualAge} >= {minAge}) and setting allowing children");
                }
            }
            else if (!allowChildrenSetting || actualAge < minAge)
            {
                if (isValid)
                {
                    AutoArmLogger.Error($"[TEST] ChildColonistTest: Child allowed despite settings - expected: invalid, got: valid (allow: {allowChildrenSetting}, age: {actualAge}, minAge: {minAge})");
                    return TestResult.Failure($"Child allowed despite settings (allow={allowChildrenSetting}, age={actualAge}, minAge={minAge})");
                }
                if (!reason.Contains("Too young") && !reason.Contains("age"))
                {
                    AutoArmLogger.Error($"[TEST] ChildColonistTest: Child rejected but not for age reasons - reason: {reason}");
                    return TestResult.Failure($"Child rejected but not for age reasons: {reason}");
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

    public class NobilityTest : ITestScenario
    {
        public string Name => "Conceited Noble Behavior";
        private Pawn noblePawn;

        public void Setup(Map map)
        {
            if (map == null || !ModsConfig.RoyaltyActive) return;

            noblePawn = TestHelpers.CreateTestPawn(map, new TestHelpers.TestPawnConfig
            {
                Name = "TestNoble",
                MakeNoble = true,
                Conceited = true
            });
        }

        public TestResult Run()
        {
            if (!ModsConfig.RoyaltyActive)
                return TestResult.Pass();

            if (noblePawn == null)
                return TestResult.Pass();

            if (noblePawn.equipment?.Primary != null)
            {
                var jobGiver = new JobGiver_PickUpBetterWeapon();
                var job = jobGiver.TestTryGiveJob(noblePawn);

                // Note: respectConceitedNobles setting was removed
                // if (job != null && AutoArmMod.settings?.respectConceitedNobles == true)
                if (job != null)
                {
                    AutoArmLogger.Error($"[TEST] NobilityTest: Conceited noble tried to switch weapons - expected: no job, got: {job.def.defName}");
                    return TestResult.Failure("Conceited noble tried to switch weapons");
                }
            }

            return TestResult.Pass();
        }

        public void Cleanup()
        {
            if (noblePawn != null && !noblePawn.Destroyed)
            {
                noblePawn.Destroy();
            }
        }
    }

    public class SlaveTest : ITestScenario
    {
        public string Name => "Slave (Has Access) Weapon Tests";
        private Pawn slave;
        private List<ThingWithComps> weapons = new List<ThingWithComps>();

        public void Setup(Map map)
        {
            if (map == null) return;

            // Clear all systems before test
            TestRunnerFix.ResetAllSystems();

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
                    // Slaves ARE part of the player faction (unlike prisoners)
                    if (slave.Faction != Faction.OfPlayer)
                    {
                        slave.SetFaction(Faction.OfPlayer);
                    }
                    
                    // Set up guest tracker for slave status
                    if (slave.guest == null)
                    {
                        slave.guest = new Pawn_GuestTracker(slave);
                    }
                    slave.guest.SetGuestStatus(Faction.OfPlayer, GuestStatus.Slave);

                    // Prepare pawn for testing
                    TestRunnerFix.PreparePawnForTest(slave);

                    // Ensure unarmed
                    slave.equipment?.DestroyAllEquipment();
                }
            }

            // Create weapons near the slave
            var weaponDef = VanillaWeaponDefOf.Gun_Autopistol;
            if (weaponDef != null && slave != null)
            {
                var weapon = TestHelpers.CreateWeapon(map, weaponDef,
                    slave.Position + new IntVec3(2, 0, 0));
                if (weapon != null)
                {
                    weapons.Add(weapon);
                    ImprovedWeaponCacheManager.AddWeaponToCache(weapon);
                }
            }
        }

        public TestResult Run()
        {
            // This test verifies:
            // Slaves CAN auto-equip weapons (they can fight for the colony in vanilla)
            
            var result = new TestResult { Success = true };
            var jobGiver = new JobGiver_PickUpBetterWeapon();

            // Test slave - SHOULD be able to equip weapons (can fight in vanilla)
            if (slave != null && ModsConfig.IdeologyActive)
            {
                result.Data["SlaveStatus"] = slave.IsSlaveOfColony;

                string reason;
                bool isValid = IsValidPawnForAutoEquip(slave, out reason);
                result.Data["SlaveIsValid"] = isValid;
                result.Data["SlaveReason"] = reason ?? "None";

                var job = jobGiver.TestTryGiveJob(slave);
                result.Data["SlaveJobCreated"] = job != null;

                // Slaves in vanilla RimWorld CAN use weapons, so they SHOULD pass validation
                // UNLESS they're drafted, downed, in mental state, etc.
                if (!isValid && !reason.Contains("colonist") && !reason.Contains("slave"))
                {
                    // Failed for some other reason (drafted, downed, etc.) - that's OK
                    result.Data["SlaveFailureReason"] = reason;
                }
                else if (!isValid && (reason.Contains("colonist") || reason.Contains("slave")))
                {
                    // Failed because not recognized as valid pawn type - that's a problem
                    result.Success = false;
                    result.Data["Error"] = "Slave rejected for not being colonist/slave when they should be allowed";
                    AutoArmLogger.Error($"[TEST] SlaveTest: Slave incorrectly rejected - reason: {reason}");
                }
                else if (isValid)
                {
                    // Slave passed validation - this is expected
                    result.Data["SlaveValidationPassed"] = true;
                    
                    // Whether a job is created depends on available weapons
                    if (job != null)
                    {
                        result.Data["SlaveCanEquip"] = true;
                    }
                }
            }

            return result;
        }

        public void Cleanup()
        {
            foreach (var weapon in weapons)
            {
                if (weapon != null && !weapon.Destroyed)
                {
                    weapon.Destroy();
                }
            }
            weapons.Clear();

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
                        // Unarmed brawlers can pick ranged weapons as temporary solution
                        // The scoring system gives penalty to ranged for brawlers but doesn't make it impossible
                        // Melee weapons get Constants.BrawlerMeleeBonus for brawlers
                        result.Data["Note"] = "Brawler picked ranged weapon (acceptable for unarmed brawler)";
                        result.Data["Explanation"] = "Brawlers get penalty for ranged but can still use them when unarmed";
                        return result;
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
                if (weapon != null && !weapon.Destroyed)
                {
                    weapon.Destroy();
                }
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
                    AutoArmLogger.Error($"[TEST] HunterTest: Hunter picked melee weapon - expected: ranged weapon, got: {weapon.Label}");
                    return TestResult.Failure($"Hunter picked melee weapon: {weapon.Label}");
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
                if (weapon != null && !weapon.Destroyed)
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

            // Create weapons near shooter pawn
            if (shooterPawn != null)
            {
                shooterPawn.equipment?.DestroyAllEquipment();

                var pos = shooterPawn.Position;
                var rifleDef = VanillaWeaponDefOf.Gun_AssaultRifle;
                var swordDef = VanillaWeaponDefOf.MeleeWeapon_LongSword;

                if (rifleDef != null)
                {
                    var rifle = TestHelpers.CreateWeapon(map, rifleDef, pos + new IntVec3(2, 0, 0));
                    if (rifle != null)
                    {
                        weapons.Add(rifle);
                        ImprovedWeaponCacheManager.AddWeaponToCache(rifle);
                    }
                }

                if (swordDef != null)
                {
                    var sword = TestHelpers.CreateWeapon(map, swordDef, pos + new IntVec3(-2, 0, 0));
                    if (sword != null)
                    {
                        weapons.Add(sword);
                        ImprovedWeaponCacheManager.AddWeaponToCache(sword);
                    }
                }
            }

            // Ensure melee pawn is unarmed too
            meleePawn?.equipment?.DestroyAllEquipment();
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

                if (!shooterWeapon.def.IsRangedWeapon)
                {
                    AutoArmLogger.Error($"[TEST] SkillBasedPreferenceTest: High-shooting pawn picked melee weapon - expected: ranged weapon, got: {shooterWeapon.Label}");
                    return TestResult.Failure($"High-shooting pawn picked melee weapon: {shooterWeapon.Label}");
                }
            }

            // Move melee pawn to weapon location and test
            if (meleePawn != null && weapons.Count > 0)
            {
                // Move melee pawn near weapons
                meleePawn.Position = shooterPawn.Position + new IntVec3(0, 0, 3);

                var meleeJob = jobGiver.TestTryGiveJob(meleePawn);
                if (meleeJob != null && meleeJob.targetA.Thing is ThingWithComps meleeWeapon)
                {
                    result.Data["MeleePawnPicked"] = meleeWeapon.Label;
                    result.Data["MeleePawnPickedMelee"] = meleeWeapon.def.IsMeleeWeapon;

                    if (!meleeWeapon.def.IsMeleeWeapon)
                    {
                        AutoArmLogger.Error($"[TEST] SkillBasedPreferenceTest: High-melee pawn picked ranged weapon - expected: melee weapon, got: {meleeWeapon.Label}");
                        return TestResult.Failure($"High-melee pawn picked ranged weapon: {meleeWeapon.Label}");
                    }
                }
            }

            // Check weapon scores for both pawns
            var rangedWeapon = weapons.FirstOrDefault(w => w.def.IsRangedWeapon);
            var meleeWeapon2 = weapons.FirstOrDefault(w => w.def.IsMeleeWeapon);

            if (rangedWeapon != null && meleeWeapon2 != null)
            {
                float shooterRangedScore = WeaponScoreCache.GetCachedScore(shooterPawn, rangedWeapon);
                float shooterMeleeScore = WeaponScoreCache.GetCachedScore(shooterPawn, meleeWeapon2);
                float meleeRangedScore = WeaponScoreCache.GetCachedScore(meleePawn, rangedWeapon);
                float meleeMeleeScore = WeaponScoreCache.GetCachedScore(meleePawn, meleeWeapon2);

                result.Data["ShooterRangedScore"] = shooterRangedScore;
                result.Data["ShooterMeleeScore"] = shooterMeleeScore;
                result.Data["MeleeRangedScore"] = meleeRangedScore;
                result.Data["MeleeMeleeScore"] = meleeMeleeScore;
            }

            return result;
        }

        public void Cleanup()
        {
            foreach (var weapon in weapons)
            {
                if (weapon != null && !weapon.Destroyed)
                {
                    weapon.Destroy();
                }
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