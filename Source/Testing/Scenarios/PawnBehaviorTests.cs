using AutoArm.Caching;
using AutoArm.Definitions;
using AutoArm.Jobs;
using AutoArm.Logging;
using AutoArm.Testing.Framework;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using static AutoArm.Testing.Helpers.TestValidationHelper;
using static AutoArm.Testing.TestHelpers;

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

            TestRunnerFix.ResetAllSystems();

            questLodger = TestHelpers.CreateTestPawn(map, new TestPawnConfig
            {
                Name = "QuestLodger"
            });

            if (questLodger != null)
            {
                if (questLodger.questTags == null)
                    questLodger.questTags = new List<string>();
                questLodger.questTags.Add("Lodger");

                questLodger.equipment?.DestroyAllEquipment();
            }

            borrowedPawn = TestHelpers.CreateTestPawn(map, new TestPawnConfig
            {
                Name = "BorrowedPawn"
            });

            if (borrowedPawn != null)
            {
                var otherFaction = Find.FactionManager.AllFactions
                    .FirstOrDefault(f => f != Faction.OfPlayer && !f.HostileTo(Faction.OfPlayer));

                if (otherFaction != null)
                {
                    if (borrowedPawn.Faction != Faction.OfPlayer)
                    {
                        borrowedPawn.SetFaction(Faction.OfPlayer);
                    }
                    if (borrowedPawn.guest == null)
                        borrowedPawn.guest = new Pawn_GuestTracker(borrowedPawn);
                    borrowedPawn.guest.SetGuestStatus(otherFaction, GuestStatus.Guest);
                }

                borrowedPawn.equipment?.DestroyAllEquipment();
            }

            if (questLodger != null)
            {
                var weaponDef = AutoArmDefOf.Gun_Autopistol;
                if (weaponDef != null)
                {
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
                        WeaponCacheManager.AddWeaponToCache(weapon1);
                        weapon1.SetForbidden(false, false);
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
                            WeaponCacheManager.AddWeaponToCache(weapon2);
                            weapon2.SetForbidden(false, false);
                        }
                    }
                }
            }
        }

        public TestResult Run()
        {
            var result = new TestResult { Success = true };
            var jobGiver = new JobGiver_PickUpBetterWeapon();

            bool originalSetting = AutoArmMod.settings?.allowTemporaryColonists ?? false;

            AutoArmMod.settings.allowTemporaryColonists = false;

            result.Data["Setting_AllowTemporary"] = false;

            if (questLodger != null)
            {
                string reason;
                bool isTemp = global::AutoArm.Jobs.Jobs.IsTemporary(questLodger);
                bool isValid = IsValidPawn(questLodger, out reason);

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

            if (borrowedPawn != null)
            {
                string reason;
                bool isTemp = global::AutoArm.Jobs.Jobs.IsTemporary(borrowedPawn);
                bool isValid = IsValidPawn(borrowedPawn, out reason);

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

            AutoArmMod.settings.allowTemporaryColonists = true;

            if (questLodger?.Map != null)
            {
                WeaponCacheManager.ClearAllCaches();
                PawnValidationCache.ClearCache();

                TestRunnerFix.ClearAllCooldownsForPawn(questLodger);
                if (borrowedPawn != null)
                    TestRunnerFix.ClearAllCooldownsForPawn(borrowedPawn);

                if (AutoArmMod.settings != null)
                {
                    AutoArmMod.settings.modEnabled = true;
                }
            }

            if (questLodger == null || questLodger.Destroyed || !questLodger.Spawned)
            {
                result.Success = false;
                result.Data["Error"] = "Quest lodger became invalid during test";
                AutoArmLogger.Error("[TEST] TemporaryColonistTest: Quest lodger became invalid");
                return result;
            }

            if (questLodger != null)
            {
                AutoArmLogger.Log($"[TEST] Quest lodger state before job: Spawned={questLodger.Spawned}, Map={questLodger.Map != null}, Position={questLodger.Position}");

                var availableWeapons = WeaponCacheManager.GetAllWeapons(questLodger.Map)?.ToList();
                AutoArmLogger.Log($"[TEST] Available weapons in cache: {availableWeapons?.Count ?? 0}");
                if (availableWeapons != null)
                {
                    foreach (var w in availableWeapons.Take(3))
                    {
                        AutoArmLogger.Log($"[TEST] - {w.Label} at {w.Position}, distance={questLodger.Position.DistanceTo(w.Position):F1}");
                    }
                }

                AutoArmLogger.Log($"[TEST] Test weapons spawned: {weapons.Count(w => w.Spawned)}");
                foreach (var w in weapons.Where(w => w.Spawned))
                {
                    AutoArmLogger.Log($"[TEST] - Test weapon: {w.Label} at {w.Position}, forbidden={w.IsForbidden(questLodger)}");
                }

                AutoArmLogger.Log($"[TEST] AutoArm enabled: {AutoArmMod.settings?.modEnabled}, allow temp colonists: {AutoArmMod.settings?.allowTemporaryColonists}");

                var job = jobGiver.TestTryGiveJob(questLodger);
                result.Data["QuestLodger_JobCreated_Allowed"] = job != null;

                string validationReason;
                bool canEquip = IsValidPawn(questLodger, out validationReason);

                if (!canEquip && validationReason.Contains("temporary"))
                {
                    result.Success = false;
                    result.Data["Error2"] = $"Quest lodger rejected for being temporary when temp colonists allowed";
                    AutoArmLogger.Error($"[TEST] TemporaryColonistTest: Quest lodger rejected as temporary when setting allows them");
                }
                else if (job == null)
                {
                    result.Data["QuestLodger_NoJob_Reason"] = validationReason ?? "Unknown";
                    result.Data["Note"] = "No job created but not due to temporary status";
                }
            }

            if (borrowedPawn != null)
            {
                var job = jobGiver.TestTryGiveJob(borrowedPawn);
                result.Data["BorrowedPawn_JobCreated_Allowed"] = job != null;
            }

            AutoArmMod.settings.allowTemporaryColonists = originalSetting;

            return result;
        }

        public void Cleanup()
        {
            foreach (var weapon in weapons)
            {
                if (weapon != null && !weapon.Destroyed)
                {
                    TestHelpers.SafeDestroyWeapon(weapon);
                }
            }
            weapons.Clear();

            if (questLodger != null && !questLodger.Destroyed)
            {
                TestHelpers.SafeDestroyPawn(questLodger);
                questLodger = null;
            }

            if (borrowedPawn != null && !borrowedPawn.Destroyed)
            {
                TestHelpers.SafeDestroyPawn(borrowedPawn);
                borrowedPawn = null;
            }
        }
    }

    public class ChildColonistTest : ITestScenario
    {
        public string Name => "Child Colonist Age Restrictions";
        private Pawn childPawn;
        private ThingWithComps nearbyWeapon;
        private int testAge = 10;
        private bool originalAllowChildren;
        private int originalMinAge;

        public void Setup(Map map)
        {
            if (map == null || !ModsConfig.BiotechActive) return;

            originalAllowChildren = AutoArmMod.settings?.allowChildrenToEquipWeapons ?? false;
            originalMinAge = AutoArmMod.settings?.childrenMinAge ?? 13;

            var config = new TestPawnConfig
            {
                BiologicalAge = testAge,
                EnsureViolenceCapable = true,
                Name = "TestChild"
            };
            childPawn = TestHelpers.CreateTestPawn(map, config);

            if (childPawn != null)
            {
                childPawn.equipment?.DestroyAllEquipment();

                var weaponDef = AutoArmDefOf.Gun_Autopistol ?? DefDatabase<ThingDef>.GetNamedSilentFail("Gun_Revolver");
                if (weaponDef != null)
                {
                    var pos = childPawn.Position + new IntVec3(1, 0, 0);
                    if (!pos.InBounds(map) || !pos.Standable(map))
                    {
                        foreach (var offset in new[] { new IntVec3(0, 0, 1), new IntVec3(-1, 0, 0), new IntVec3(0, 0, -1) })
                        {
                            var altPos = childPawn.Position + offset;
                            if (altPos.InBounds(map) && altPos.Standable(map))
                            {
                                pos = altPos;
                                break;
                            }
                        }
                    }

                    nearbyWeapon = TestHelpers.CreateWeapon(map, weaponDef, pos, QualityCategory.Normal);
                    if (nearbyWeapon != null)
                    {
                        nearbyWeapon.SetForbidden(false, false);
                        WeaponCacheManager.AddWeaponToCache(nearbyWeapon);
                    }
                }
            }
        }

        public TestResult Run()
        {
            if (!ModsConfig.BiotechActive)
                return TestResult.Pass();

            if (childPawn == null)
                return TestResult.Failure("Child pawn creation failed");

            var result = new TestResult { Success = true };

            JobGiver_PickUpBetterWeapon.EnableTestMode(true);

            var jobGiver = new JobGiver_PickUpBetterWeapon();


            AutoArmMod.settings.allowChildrenToEquipWeapons = false;
            AutoArmMod.settings.childrenMinAge = originalMinAge;

            AutoArm.Caching.PawnValidationCache.ClearCache();

            var jobWhenDisabled = jobGiver.TestTryGiveJob(childPawn);
            if (jobWhenDisabled == null)
            {
                AutoArmLogger.Error("[TEST] ChildColonistTest: Child (age 10) BLOCKED when setting disabled - should follow vanilla (allow 3+)");
                result.Success = false;
                result.Data["Phase1_Error"] = "Child blocked when vanilla should allow (age 10 >= 3)";
                return result;
            }
            result.Data["Phase1_Pass"] = "Child (age 10) correctly allowed when setting disabled (vanilla behavior)";

            AutoArmMod.settings.allowChildrenToEquipWeapons = true;
            AutoArmMod.settings.childrenMinAge = 8;

            AutoArm.Caching.PawnValidationCache.ClearCache();

            WeaponCacheManager.EnsureCacheExists(childPawn.Map);

            if (childPawn.Map != null && nearbyWeapon != null)
            {
                WeaponCacheManager.AddWeaponToCache(nearbyWeapon);

                var weaponsInCache = WeaponCacheManager.GetAllWeapons(childPawn.Map).ToList();
                if (!weaponsInCache.Contains(nearbyWeapon))
                {
                    AutoArmLogger.Error($"[TEST] ChildColonistTest: Weapon not in cache! Cache has {weaponsInCache.Count} weapons");

                    if (nearbyWeapon.Destroyed)
                        AutoArmLogger.Error("[TEST] ChildColonistTest: Weapon was destroyed!");
                    if (!nearbyWeapon.Spawned)
                        AutoArmLogger.Error("[TEST] ChildColonistTest: Weapon is not spawned!");
                    if (nearbyWeapon.Map != childPawn.Map)
                        AutoArmLogger.Error($"[TEST] ChildColonistTest: Weapon map mismatch! Weapon map: {nearbyWeapon.Map?.uniqueID}, Pawn map: {childPawn.Map?.uniqueID}");

                    return TestResult.Failure("Weapon not added to cache properly");
                }

            }

            if (nearbyWeapon == null)
            {
                return TestResult.Failure("No weapon available for test");
            }


            if (childPawn != null && nearbyWeapon != null)
            {
                bool canReach = childPawn.CanReach(nearbyWeapon, Verse.AI.PathEndMode.OnCell, Danger.Some);
            }

            var jobWhenEnabled = jobGiver.TestTryGiveJob(childPawn);
            if (jobWhenEnabled == null)
            {
                bool isViolenceCapable = !childPawn.WorkTagIsDisabled(WorkTags.Violent);
                var weaponsInCache = WeaponCacheManager.GetAllWeapons(childPawn.Map)?.ToList();

                result.Success = false;
                result.Data["Phase2_Error"] = "No job created when children allowed";
                result.Data["Child_Age"] = childPawn.ageTracker.AgeBiologicalYears;
                result.Data["Child_ViolenceCapable"] = isViolenceCapable;
                result.Data["Setting_AllowChildren"] = AutoArmMod.settings.allowChildrenToEquipWeapons;
                result.Data["Setting_MinAge"] = AutoArmMod.settings.childrenMinAge;
                result.Data["Weapon_Available"] = nearbyWeapon != null;
                result.Data["Weapon_InCache"] = weaponsInCache?.Contains(nearbyWeapon) ?? false;
                result.Data["Cache_WeaponCount"] = weaponsInCache?.Count ?? 0;

                AutoArmLogger.Error($"[TEST] ChildColonistTest FAILED: No job created");
                AutoArmLogger.Error($"[TEST] Details: Age={childPawn.ageTracker.AgeBiologicalYears}, ViolenceCapable={isViolenceCapable}");
                AutoArmLogger.Error($"[TEST] Settings: allowChildren={AutoArmMod.settings.allowChildrenToEquipWeapons}, minAge={AutoArmMod.settings.childrenMinAge}");
                AutoArmLogger.Error($"[TEST] Weapon: available={nearbyWeapon != null}, inCache={weaponsInCache?.Contains(nearbyWeapon) ?? false}, cacheCount={weaponsInCache?.Count ?? 0}");

                return result;
            }

            result.Data["Phase2_Pass"] = "Job created when children allowed";
            result.Data["Job_Created"] = jobWhenEnabled.def.defName;
            return result;
        }

        public void Cleanup()
        {
            JobGiver_PickUpBetterWeapon.EnableTestMode(false);

            if (AutoArmMod.settings != null)
            {
                AutoArmMod.settings.allowChildrenToEquipWeapons = originalAllowChildren;
                AutoArmMod.settings.childrenMinAge = originalMinAge;
            }

            TestCleanupHelper.CleanupTest(childPawn, nearbyWeapon);
            childPawn = null;
            nearbyWeapon = null;
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
                TestHelpers.SafeDestroyPawn(noblePawn);
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

            TestRunnerFix.ResetAllSystems();

            if (ModsConfig.IdeologyActive)
            {
                slave = TestHelpers.CreateTestPawn(map, new TestPawnConfig
                {
                    Name = "TestSlave"
                });

                if (slave != null)
                {
                    if (slave.Faction != Faction.OfPlayer)
                    {
                        slave.SetFaction(Faction.OfPlayer);
                    }

                    if (slave.guest == null)
                    {
                        slave.guest = new Pawn_GuestTracker(slave);
                    }
                    slave.guest.SetGuestStatus(Faction.OfPlayer, GuestStatus.Slave);

                    TestRunnerFix.PreparePawnForTest(slave);

                    slave.equipment?.DestroyAllEquipment();
                }
            }

            var weaponDef = AutoArmDefOf.Gun_Autopistol;
            if (weaponDef != null && slave != null)
            {
                var weapon = TestHelpers.CreateWeapon(map, weaponDef,
                    slave.Position + new IntVec3(2, 0, 0));
                if (weapon != null)
                {
                    weapons.Add(weapon);
                    WeaponCacheManager.AddWeaponToCache(weapon);
                }
            }
        }

        public TestResult Run()
        {

            var result = new TestResult { Success = true };
            var jobGiver = new JobGiver_PickUpBetterWeapon();

            if (slave != null && ModsConfig.IdeologyActive)
            {
                result.Data["SlaveStatus"] = slave.IsSlaveOfColony;

                string reason;
                bool isValid = IsValidPawn(slave, out reason);
                result.Data["SlaveIsValid"] = isValid;
                result.Data["SlaveReason"] = reason ?? "None";

                var job = jobGiver.TestTryGiveJob(slave);
                result.Data["SlaveJobCreated"] = job != null;

                if (!isValid && !reason.Contains("colonist") && !reason.Contains("slave"))
                {
                    result.Data["SlaveFailureReason"] = reason;
                }
                else if (!isValid && (reason.Contains("colonist") || reason.Contains("slave")))
                {
                    result.Success = false;
                    result.Data["Error"] = "Slave rejected for not being colonist/slave when they should be allowed";
                    AutoArmLogger.Error($"[TEST] SlaveTest: Slave incorrectly rejected - reason: {reason}");
                }
                else if (isValid)
                {
                    result.Data["SlaveValidationPassed"] = true;

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
                    TestHelpers.SafeDestroyWeapon(weapon);
                }
            }
            weapons.Clear();

            if (slave != null && !slave.Destroyed)
            {
                TestHelpers.SafeDestroyPawn(slave);
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
                brawlerPawn.equipment?.DestroyAllEquipment();

                var pos = brawlerPawn.Position;

                var swordDef = AutoArmDefOf.MeleeWeapon_LongSword;
                var rifleDef = AutoArmDefOf.Gun_AssaultRifle;

                if (swordDef != null)
                {
                    var sword = TestHelpers.CreateWeapon(map, swordDef, pos + new IntVec3(2, 0, 0));
                    if (sword != null)
                    {
                        weapons.Add(sword);
                        WeaponCacheManager.AddWeaponToCache(sword);
                    }
                }
                if (rifleDef != null)
                {
                    var rifle = TestHelpers.CreateWeapon(map, rifleDef, pos + new IntVec3(-2, 0, 0));
                    if (rifle != null)
                    {
                        weapons.Add(rifle);
                        WeaponCacheManager.AddWeaponToCache(rifle);
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
                var meleeWeapon = weapons.FirstOrDefault(w => w.def.IsMeleeWeapon);
                var rangedWeapon = weapons.FirstOrDefault(w => w.def.IsRangedWeapon);

                if (meleeWeapon != null && rangedWeapon != null)
                {
                    float meleeScore = WeaponCacheManager.GetCachedScore(brawlerPawn, meleeWeapon);
                    float rangedScore = WeaponCacheManager.GetCachedScore(brawlerPawn, rangedWeapon);

                    result.Data["MeleeScore"] = meleeScore;
                    result.Data["RangedScore"] = rangedScore;
                    result.Data["PickedWeapon"] = weapon.Label;

                    if (weapon.def.IsMeleeWeapon)
                    {
                        return result;
                    }
                    else if (weapon.def.IsRangedWeapon)
                    {
                        result.Data["Note"] = "Brawler picked ranged weapon (acceptable for unarmed brawler)";
                        result.Data["Explanation"] = "Brawlers get penalty for ranged but can still use them when unarmed";
                        return result;
                    }
                }
            }

            result.Data["Note"] = "No weapon pickup job created";
            return result;
        }

        public void Cleanup()
        {
            foreach (var weapon in weapons)
            {
                if (weapon != null && !weapon.Destroyed)
                {
                    TestHelpers.SafeDestroyWeapon(weapon);
                }
            }
            weapons.Clear();

            if (brawlerPawn != null && !brawlerPawn.Destroyed)
            {
                TestHelpers.SafeDestroyPawn(brawlerPawn);
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

            shooterPawn = TestHelpers.CreateTestPawn(map, new TestHelpers.TestPawnConfig
            {
                Name = "Shooter",
                Skills = new Dictionary<SkillDef, int>
                {
                    { SkillDefOf.Shooting, 15 },
                    { SkillDefOf.Melee, 3 }
                }
            });

            meleePawn = TestHelpers.CreateTestPawn(map, new TestHelpers.TestPawnConfig
            {
                Name = "Brawler",
                Skills = new Dictionary<SkillDef, int>
                {
                    { SkillDefOf.Shooting, 3 },
                    { SkillDefOf.Melee, 15 }
                }
            });

            if (shooterPawn != null)
            {
                shooterPawn.equipment?.DestroyAllEquipment();

                var pos = shooterPawn.Position;
                var rifleDef = AutoArmDefOf.Gun_AssaultRifle;
                var swordDef = AutoArmDefOf.MeleeWeapon_LongSword;

                if (rifleDef != null)
                {
                    var rifle = TestHelpers.CreateWeapon(map, rifleDef, pos + new IntVec3(2, 0, 0), QualityCategory.Good);
                    if (rifle != null)
                    {
                        weapons.Add(rifle);
                        WeaponCacheManager.AddWeaponToCache(rifle);
                    }
                }

            }

            if (meleePawn != null)
            {
                meleePawn.equipment?.DestroyAllEquipment();

                var junkDef = AutoArmDefOf.MeleeWeapon_Knife;
                if (junkDef != null)
                {
                    ThingWithComps junk = null;
                    try
                    {
                        var stuff = junkDef.MadeFromStuff ? GenStuff.DefaultStuffFor(junkDef) : null;
                        junk = ThingMaker.MakeThing(junkDef, stuff) as ThingWithComps;
                        if (junk != null)
                        {
                            var compQuality = junk.TryGetComp<CompQuality>();
                            if (compQuality != null)
                            {
                                compQuality.SetQuality(QualityCategory.Poor, ArtGenerationContext.Colony);
                            }
                            meleePawn.equipment?.AddEquipment(junk);
                        }
                    }
                    catch (System.Exception e)
                    {
                        AutoArmLogger.Error("[TEST] Failed to equip junk melee for SkillBasedPreferenceTest", e);
                    }
                }
            }
        }

        public TestResult Run()
        {
            if (shooterPawn == null || meleePawn == null)
                return TestResult.Failure("Failed to create test pawns");

            var result = new TestResult { Success = true };
            var jobGiver = new JobGiver_PickUpBetterWeapon();

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

            if (meleePawn != null && weapons.Count > 0)
            {
                var meleePos = meleePawn.Position + new IntVec3(2, 0, 0);
                var strongMeleeDef = AutoArmDefOf.MeleeWeapon_LongSword;
                if (strongMeleeDef != null)
                {
                    var strongMelee = TestHelpers.CreateWeapon(meleePawn.Map, strongMeleeDef, meleePos, QualityCategory.Excellent);
                    if (strongMelee != null)
                    {
                        weapons.Add(strongMelee);
                        WeaponCacheManager.AddWeaponToCache(strongMelee);
                    }
                }
                meleePawn.Position = meleePos;

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

            var rangedWeapon = weapons.FirstOrDefault(w => w.def.IsRangedWeapon);
            var meleeWeapon2 = weapons.FirstOrDefault(w => w.def.IsMeleeWeapon);

            if (rangedWeapon != null && meleeWeapon2 != null)
            {
                float shooterRangedScore = WeaponCacheManager.GetCachedScore(shooterPawn, rangedWeapon);
                float shooterMeleeScore = WeaponCacheManager.GetCachedScore(shooterPawn, meleeWeapon2);
                float meleeRangedScore = WeaponCacheManager.GetCachedScore(meleePawn, rangedWeapon);
                float meleeMeleeScore = WeaponCacheManager.GetCachedScore(meleePawn, meleeWeapon2);

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
                    TestHelpers.SafeDestroyWeapon(weapon);
                }
            }
            weapons.Clear();

            if (shooterPawn != null && !shooterPawn.Destroyed)
            {
                TestHelpers.SafeDestroyPawn(shooterPawn);
            }
            if (meleePawn != null && !meleePawn.Destroyed)
            {
                TestHelpers.SafeDestroyPawn(meleePawn);
            }
        }
    }
}
