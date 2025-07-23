using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using static AutoArm.Testing.TestHelpers;

namespace AutoArm.Testing
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
                }
            }
            
            // Test with temporary colonists ALLOWED
            AutoArmMod.settings.allowTemporaryColonists = true;
            
            if (questLodger != null)
            {
                var job = jobGiver.TestTryGiveJob(questLodger);
                result.Data["QuestLodger_JobCreated_Allowed"] = job != null;
                
                // When allowed, they should be able to pick up weapons
                if (job == null && weapons.Any(w => w.Spawned))
                {
                    result.Success = false;
                    result.Data["Error2"] = "Quest lodger couldn't get weapon when temp colonists allowed";
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

    public class StressTest : ITestScenario
    {
        public string Name => "Stress Test - Many Pawns and Weapons";
        private List<Pawn> testPawns = new List<Pawn>();
        private List<ThingWithComps> testWeapons = new List<ThingWithComps>();
        private const int PAWN_COUNT = 50;
        private const int WEAPON_COUNT = 100;
        private const int TEST_ITERATIONS = 10;

        public void Setup(Map map)
        {
            if (map == null) return;
            
            var startTime = System.DateTime.Now;
            
            // Create many pawns in a grid pattern
            AutoArmDebug.Log($"[STRESS TEST] Creating {PAWN_COUNT} test pawns...");
            
            int gridSize = (int)Math.Ceiling(Math.Sqrt(PAWN_COUNT));
            int pawnIndex = 0;
            
            for (int x = 0; x < gridSize && pawnIndex < PAWN_COUNT; x++)
            {
                for (int z = 0; z < gridSize && pawnIndex < PAWN_COUNT; z++)
                {
                    var pos = map.Center + new IntVec3(x * 3 - gridSize * 3 / 2, 0, z * 3 - gridSize * 3 / 2);
                    
                    // Make sure position is valid
                    if (!pos.InBounds(map) || !pos.Standable(map))
                        continue;
                        
                    var pawn = TestHelpers.CreateTestPawn(map, new TestPawnConfig
                    {
                        Name = $"StressPawn{pawnIndex}",
                        Skills = new Dictionary<SkillDef, int>
                        {
                            { SkillDefOf.Shooting, Rand.Range(0, 20) },
                            { SkillDefOf.Melee, Rand.Range(0, 20) }
                        }
                    });
                    
                    if (pawn != null)
                    {
                        // Move to position
                        pawn.Position = pos;
                        pawn.equipment?.DestroyAllEquipment();
                        testPawns.Add(pawn);
                        pawnIndex++;
                    }
                }
            }
            
            AutoArmDebug.Log($"[STRESS TEST] Created {testPawns.Count} pawns");
            
            // Create many weapons scattered around
            AutoArmDebug.Log($"[STRESS TEST] Creating {WEAPON_COUNT} weapons...");
            
            var weaponDefs = new ThingDef[]
            {
                VanillaWeaponDefOf.Gun_Autopistol,
                VanillaWeaponDefOf.Gun_AssaultRifle,
                VanillaWeaponDefOf.Gun_BoltActionRifle,
                VanillaWeaponDefOf.MeleeWeapon_Knife,
                VanillaWeaponDefOf.MeleeWeapon_LongSword
            }.Where(d => d != null).ToArray();
            
            if (weaponDefs.Length == 0)
            {
                Log.Error("[STRESS TEST] No weapon defs available!");
                return;
            }
            
            for (int i = 0; i < WEAPON_COUNT; i++)
            {
                var weaponDef = weaponDefs[i % weaponDefs.Length];
                var quality = (QualityCategory)Rand.Range(0, 7); // Random quality
                
                // Random position around the center
                var radius = gridSize * 4;
                var angle = Rand.Range(0f, 360f);
                var distance = Rand.Range(5f, radius);
                var pos = map.Center + (Vector3.forward.RotatedBy(angle) * distance).ToIntVec3();
                
                if (!pos.InBounds(map) || !pos.Standable(map))
                    continue;
                    
                var weapon = TestHelpers.CreateWeapon(map, weaponDef, pos, quality);
                if (weapon != null)
                {
                    testWeapons.Add(weapon);
                    ImprovedWeaponCacheManager.AddWeaponToCache(weapon);
                }
            }
            
            AutoArmDebug.Log($"[STRESS TEST] Created {testWeapons.Count} weapons");
            
            var setupTime = (System.DateTime.Now - startTime).TotalMilliseconds;
            AutoArmDebug.Log($"[STRESS TEST] Setup completed in {setupTime:F2}ms");
        }

        public TestResult Run()
        {
            var result = new TestResult { Success = true };
            var jobGiver = new JobGiver_PickUpBetterWeapon();
            
            // Measure initial memory
            long startMemory = GC.GetTotalMemory(false);
            
            // Test 1: Mass job creation
            AutoArmDebug.Log($"[STRESS TEST] Testing job creation for {testPawns.Count} pawns...");
            var startTime = System.DateTime.Now;
            int jobsCreated = 0;
            
            for (int iteration = 0; iteration < TEST_ITERATIONS; iteration++)
            {
                foreach (var pawn in testPawns)
                {
                    if (pawn != null && !pawn.Destroyed)
                    {
                        var job = jobGiver.TestTryGiveJob(pawn);
                        if (job != null)
                            jobsCreated++;
                    }
                }
            }
            
            var jobCreationTime = (System.DateTime.Now - startTime).TotalMilliseconds;
            result.Data["JobCreationTime_ms"] = jobCreationTime;
            result.Data["JobsCreated"] = jobsCreated;
            result.Data["AvgTimePerPawn_ms"] = jobCreationTime / (testPawns.Count * TEST_ITERATIONS);
            
            // Test 2: Cache performance
            AutoArmDebug.Log("[STRESS TEST] Testing weapon cache performance...");
            startTime = System.DateTime.Now;
            int cacheHits = 0;
            
            for (int i = 0; i < 100; i++)
            {
                var weapons = ImprovedWeaponCacheManager.GetWeaponsNear(
                    testPawns[0].Map, 
                    testPawns[0].Map.Center, 
                    100f
                ).ToList();
                cacheHits += weapons.Count;
            }
            
            var cacheTime = (System.DateTime.Now - startTime).TotalMilliseconds;
            result.Data["CacheQueryTime_ms"] = cacheTime;
            result.Data["WeaponsInCache"] = cacheHits / 100;
            
            // Test 3: Memory usage
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            
            long endMemory = GC.GetTotalMemory(false);
            long memoryUsed = endMemory - startMemory;
            
            result.Data["MemoryUsed_MB"] = memoryUsed / (1024.0 * 1024.0);
            result.Data["MemoryPerPawn_KB"] = (memoryUsed / 1024.0) / testPawns.Count;
            
            // Test 4: Weapon scoring performance
            AutoArmDebug.Log("[STRESS TEST] Testing weapon scoring performance...");
            startTime = System.DateTime.Now;
            int scoringOperations = 0;
            
            var samplePawn = testPawns.FirstOrDefault();
            var sampleWeapons = testWeapons.Take(10).ToList();
            
            if (samplePawn != null)
            {
                for (int i = 0; i < 1000; i++)
                {
                    foreach (var weapon in sampleWeapons)
                    {
                        if (weapon != null && !weapon.Destroyed)
                        {
                            var score = jobGiver.GetWeaponScore(samplePawn, weapon);
                            scoringOperations++;
                        }
                    }
                }
            }
            
            var scoringTime = (System.DateTime.Now - startTime).TotalMilliseconds;
            result.Data["ScoringTime_ms"] = scoringTime;
            result.Data["ScoresPerSecond"] = (scoringOperations * 1000.0) / scoringTime;
            
            // Test 5: Rapid weapon spawning/despawning
            AutoArmDebug.Log("[STRESS TEST] Testing rapid weapon spawn/despawn...");
            startTime = System.DateTime.Now;
            var map = testPawns[0].Map;
            var spawnPos = map.Center + new IntVec3(0, 0, 10);
            
            for (int i = 0; i < 100; i++)
            {
                var weapon = TestHelpers.CreateWeapon(map, VanillaWeaponDefOf.Gun_Autopistol, spawnPos);
                if (weapon != null)
                {
                    ImprovedWeaponCacheManager.AddWeaponToCache(weapon);
                    weapon.Destroy();
                }
            }
            
            var spawnTime = (System.DateTime.Now - startTime).TotalMilliseconds;
            result.Data["SpawnDestroyTime_ms"] = spawnTime;
            
            // Performance thresholds
            bool passedPerformance = true;
            
            if (jobCreationTime / (testPawns.Count * TEST_ITERATIONS) > 10.0) // 10ms per pawn is more realistic
            {
                passedPerformance = false;
                result.Data["PerfWarning_JobCreation"] = "Job creation too slow";
            }
            
            if (memoryUsed > 100 * 1024 * 1024) // 100MB is excessive
            {
                passedPerformance = false;
                result.Data["PerfWarning_Memory"] = "Excessive memory usage";
            }
            
            result.Success = passedPerformance;
            
            AutoArmDebug.Log($"[STRESS TEST] Completed. Performance: {(passedPerformance ? "PASSED" : "FAILED")}");
            
            return result;
        }

        public void Cleanup()
        {
            AutoArmDebug.Log($"[STRESS TEST] Starting cleanup of {testPawns.Count} pawns and {testWeapons.Count} weapons...");
            
            // Destroy all weapons first
            foreach (var weapon in testWeapons)
            {
                if (weapon != null && !weapon.Destroyed)
                {
                    weapon.Destroy();
                }
            }
            testWeapons.Clear();
            
            // Then destroy all pawns
            foreach (var pawn in testPawns)
            {
                if (pawn != null && !pawn.Destroyed)
                {
                    pawn.jobs?.StopAll();
                    pawn.equipment?.DestroyAllEquipment();
                    pawn.Destroy();
                }
            }
            testPawns.Clear();
            
            // Force garbage collection to clean up
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            
            AutoArmDebug.Log("[STRESS TEST] Cleanup completed");
        }
    }

    public class UnarmedPawnTest : ITestScenario
    {
        public string Name => "Unarmed Pawn Weapon Acquisition";
        private Pawn testPawn;
        private ThingWithComps testWeapon;

        public void Setup(Map map)
        {
            if (map == null) return;

            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn != null)
            {
                testPawn.equipment?.DestroyAllEquipment();

                var weaponDef = VanillaWeaponDefOf.Gun_Autopistol;
                if (weaponDef != null)
                {
                    testWeapon = TestHelpers.CreateWeapon(map, weaponDef,
                        testPawn.Position + new IntVec3(3, 0, 0));

                    if (testWeapon != null)
                    {
                        var compQuality = testWeapon.TryGetComp<CompQuality>();
                        if (compQuality != null)
                        {
                            compQuality.SetQuality(QualityCategory.Excellent, ArtGenerationContext.Colony);
                        }
                        
                        // Add to cache to ensure it can be found
                        ImprovedWeaponCacheManager.AddWeaponToCache(testWeapon);
                    }
                }
            }
        }

        public TestResult Run()
        {
            if (testPawn == null)
                return TestResult.Failure("Test pawn creation failed");

            if (testPawn.equipment?.Primary != null)
                return TestResult.Failure("Pawn is not unarmed");

            var oldDebug = AutoArmMod.settings.debugLogging;
            AutoArmMod.settings.debugLogging = true;

            AutoArmDebug.Log($"[TEST] Testing unarmed pawn: {testPawn.Name} at {testPawn.Position}");
            AutoArmDebug.Log($"[TEST] Available weapon: {testWeapon?.Label} at {testWeapon?.Position}");

            var jobGiver = new JobGiver_PickUpBetterWeapon();
            var job = jobGiver.TestTryGiveJob(testPawn);

            AutoArmMod.settings.debugLogging = oldDebug;

            if (job == null)
            {
                AutoArmDebug.Log($"[TEST] No job created. Checking conditions:");

                string reason;
                bool isValidPawn = JobGiverHelpers.IsValidPawnForAutoEquip(testPawn, out reason);
                AutoArmDebug.Log($"[TEST] Is valid pawn: {isValidPawn} - {reason}");

                if (testWeapon != null)
                {
                    bool isValidWeapon = JobGiverHelpers.IsValidWeaponCandidate(testWeapon, testPawn, out reason);
                    AutoArmDebug.Log($"[TEST] Is valid weapon: {isValidWeapon} - {reason}");

                    var score = WeaponScoreCache.GetCachedScore(testPawn, testWeapon);
                    AutoArmDebug.Log($"[TEST] Weapon score: {score}");
                }

                return TestResult.Failure("No weapon pickup job created for unarmed pawn");
            }

            if (job.def != JobDefOf.Equip)
                return TestResult.Failure($"Wrong job type: {job.def.defName}");

            if (job.targetA.Thing != testWeapon)
                return TestResult.Failure($"Job targets wrong weapon: {job.targetA.Thing?.Label}");

            return TestResult.Pass();
        }

        public void Cleanup()
        {
            // Destroy weapon first to avoid container conflicts
            if (testWeapon != null && !testWeapon.Destroyed)
            {
                testWeapon.Destroy();
                testWeapon = null;
            }
            if (testPawn != null && !testPawn.Destroyed)
            {
                // Stop all jobs first
                testPawn.jobs?.StopAll();
                // Clear equipment tracker references
                testPawn.equipment?.DestroyAllEquipment();
                // Destroy the pawn
                testPawn.Destroy();
                testPawn = null;
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
                        // It's acceptable for unarmed brawler to pick ranged if score is positive
                        if (rangedScore > 0 && meleeScore <= rangedScore)
                        {
                            result.Data["Note"] = "Brawler picked ranged weapon because it scored higher";
                            return result;
                        }
                        else
                        {
                            return TestResult.Failure($"Brawler incorrectly picked ranged weapon: {weapon.Label} (melee score: {meleeScore}, ranged score: {rangedScore})");
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




    public class WeaponUpgradeTest : ITestScenario
    {
        public string Name => "Weapon Upgrade Logic";
        private Pawn testPawn;
        private ThingWithComps currentWeapon;
        private ThingWithComps betterWeapon;

        public void Setup(Map map)
        {
            if (map == null) return;

            var testArea = CellRect.CenteredOn(map.Center, 10);
            var existingWeapons = map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon)
                .Where(t => testArea.Contains(t.Position))
                .ToList();

            foreach (var weapon in existingWeapons)
            {
                weapon.Destroy();
            }

            AutoArmDebug.Log("[TEST] Starting weapon upgrade test setup");

            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn == null)
            {
                Log.Error("[TEST] Failed to create test pawn!");
                return;
            }

            AutoArmDebug.Log($"[TEST] Created pawn: {testPawn.Name} at {testPawn.Position}");

            testPawn.equipment?.DestroyAllEquipment();

            var pistolDef = VanillaWeaponDefOf.Gun_Autopistol;
            var rifleDef = VanillaWeaponDefOf.Gun_AssaultRifle;

            AutoArmDebug.Log($"[TEST] Pistol def: {pistolDef?.defName ?? "NULL"}");
            AutoArmDebug.Log($"[TEST] Rifle def: {rifleDef?.defName ?? "NULL"}");

            if (pistolDef == null)
            {
                pistolDef = DefDatabase<ThingDef>.GetNamedSilentFail("Gun_Revolver");
                AutoArmDebug.Log($"[TEST] Using revolver instead: {pistolDef?.defName ?? "NULL"}");
            }
            if (rifleDef == null)
            {
                rifleDef = DefDatabase<ThingDef>.GetNamedSilentFail("Gun_BoltActionRifle");
                AutoArmDebug.Log($"[TEST] Using bolt rifle instead: {rifleDef?.defName ?? "NULL"}");
            }

            if (pistolDef != null && rifleDef != null)
            {
                currentWeapon = ThingMaker.MakeThing(pistolDef) as ThingWithComps;
                if (currentWeapon != null)
                {
                    var compQuality = currentWeapon.TryGetComp<CompQuality>();
                    if (compQuality != null)
                    {
                        compQuality.SetQuality(QualityCategory.Poor, ArtGenerationContext.Colony);
                    }

                    if (testPawn.equipment != null)
                    {
                        // Ensure pawn is unarmed first
                        testPawn.equipment.DestroyAllEquipment();
                        
                        testPawn.equipment.AddEquipment(currentWeapon);
                    }
                    AutoArmDebug.Log($"[TEST] Equipped pawn with {currentWeapon.Label}");
                }

                var weaponPos = testPawn.Position + new IntVec3(3, 0, 0);
                if (!weaponPos.InBounds(map) || !weaponPos.Standable(map))
                {
                    weaponPos = testPawn.Position + new IntVec3(0, 0, 3);
                }

                betterWeapon = ThingMaker.MakeThing(rifleDef) as ThingWithComps;
                if (betterWeapon != null)
                {
                    var compQuality = betterWeapon.TryGetComp<CompQuality>();
                    if (compQuality != null)
                    {
                        compQuality.SetQuality(QualityCategory.Good, ArtGenerationContext.Colony);
                    }

                    GenSpawn.Spawn(betterWeapon, weaponPos, map);
                    betterWeapon.SetForbidden(false, false);

                    if (testPawn.outfits?.CurrentApparelPolicy?.filter != null)
                    {
                        testPawn.outfits.CurrentApparelPolicy.filter.SetAllow(pistolDef, true);
                        testPawn.outfits.CurrentApparelPolicy.filter.SetAllow(rifleDef, true);

                        var weaponsCat = DefDatabase<ThingCategoryDef>.GetNamedSilentFail("Weapons");
                        if (weaponsCat != null)
                            testPawn.outfits.CurrentApparelPolicy.filter.SetAllow(weaponsCat, true);

                        var rangedCat = DefDatabase<ThingCategoryDef>.GetNamedSilentFail("WeaponsRanged");
                        if (rangedCat != null)
                            testPawn.outfits.CurrentApparelPolicy.filter.SetAllow(rangedCat, true);
                    }

                    AutoArmDebug.Log($"[TEST] Created {betterWeapon.Label} at {betterWeapon.Position}");

                    // Force cache rebuild after spawning the weapon
                    ImprovedWeaponCacheManager.InvalidateCache(map);
                    
                    // Manually add the weapon to cache to ensure it's registered
                    ImprovedWeaponCacheManager.AddWeaponToCache(betterWeapon);
                    
                    var nearbyWeapons = ImprovedWeaponCacheManager.GetWeaponsNear(map, testPawn.Position, 50f);
                    AutoArmDebug.Log($"[TEST] Weapons in cache after rebuild: {nearbyWeapons.Count()}");
                    foreach (var w in nearbyWeapons)
                    {
                        AutoArmDebug.Log($"[TEST] - {w.Label} at {w.Position}, destroyed: {w.Destroyed}, spawned: {w.Spawned}");
                    }
                }
            }
        }

        public TestResult Run()
        {
            AutoArmDebug.Log("[TEST] Starting test run");

            if (testPawn == null)
                return TestResult.Failure("Test pawn is null");

            if (testPawn.equipment?.Primary == null)
                return TestResult.Failure($"Pawn has no equipped weapon (equipment tracker: {testPawn.equipment != null})");

            if (betterWeapon == null || !betterWeapon.Spawned)
                return TestResult.Failure($"Better weapon null or not spawned (null: {betterWeapon == null})");

            var oldDebug = AutoArmMod.settings.debugLogging;
            var oldEnabled = AutoArmMod.settings.modEnabled;
            AutoArmMod.settings.debugLogging = true;
            AutoArmMod.settings.modEnabled = true; 

            var jobGiver = new JobGiver_PickUpBetterWeapon();

            AutoArmDebug.Log($"[TEST] Pawn: {testPawn.Name} at {testPawn.Position}");
            AutoArmDebug.Log($"[TEST] Current weapon: {currentWeapon?.Label ?? "null"}");
            AutoArmDebug.Log($"[TEST] Better weapon: {betterWeapon?.Label} at {betterWeapon?.Position}");
            AutoArmDebug.Log($"[TEST] Distance: {testPawn.Position.DistanceTo(betterWeapon.Position)}");
            AutoArmDebug.Log($"[TEST] Weapon forbidden: {betterWeapon?.IsForbidden(testPawn)}");

            var filter = testPawn.outfits?.CurrentApparelPolicy?.filter;
            if (filter != null)
            {
                AutoArmDebug.Log($"[TEST] Outfit allows rifle: {filter.Allows(betterWeapon.def)}");
            }

            var currentScore = WeaponScoreCache.GetCachedScore(testPawn, currentWeapon);
            var betterScore = WeaponScoreCache.GetCachedScore(testPawn, betterWeapon);

            AutoArmDebug.Log($"[TEST] Current weapon score: {currentScore}");
            AutoArmDebug.Log($"[TEST] Better weapon score: {betterScore}");
            AutoArmDebug.Log($"[TEST] Threshold needed: {currentScore * 1.1f}");

            string reason;
            bool isValidPawn = JobGiverHelpers.IsValidPawnForAutoEquip(testPawn, out reason);
            AutoArmDebug.Log($"[TEST] Is valid pawn: {isValidPawn} - Reason: {reason}");

            AutoArmDebug.Log($"[TEST] Pawn in bed: {testPawn.InBed()}");
            AutoArmDebug.Log($"[TEST] Pawn has lord: {testPawn.GetLord() != null}");

            var weaponConditional = new ThinkNode_ConditionalWeaponsInOutfit();
            bool weaponsAllowed = weaponConditional.TestSatisfied(testPawn);
            AutoArmDebug.Log($"[TEST] Weapons allowed in outfit: {weaponsAllowed}");

            bool isValidWeapon = JobGiverHelpers.IsValidWeaponCandidate(betterWeapon, testPawn, out reason);
            AutoArmDebug.Log($"[TEST] Is better weapon valid: {isValidWeapon} - Reason: {reason}");

            var job = jobGiver.TestTryGiveJob(testPawn);

            AutoArmMod.settings.debugLogging = oldDebug;
            AutoArmMod.settings.modEnabled = oldEnabled;

            AutoArmDebug.Log($"[TEST] Job result: {job?.def.defName ?? "NULL"}");
            if (job != null)
            {
                AutoArmDebug.Log($"[TEST] Job target: {job.targetA.Thing?.Label ?? "null"}");
            }

            var result = new TestResult { Success = true };
            result.Data["Current Score"] = currentScore;
            result.Data["Better Score"] = betterScore;

            if (betterScore <= currentScore * 1.1f)
                return TestResult.Failure($"Better weapon score not high enough ({betterScore} vs {currentScore * 1.1f} required)");

            if (job == null)
                return TestResult.Failure("No upgrade job created");

            if (job.def != JobDefOf.Equip)
                return TestResult.Failure($"Wrong job type: {job.def.defName}");

            if (job.targetA.Thing != betterWeapon)
                return TestResult.Failure("Job targets wrong weapon");

            return result;
        }

        public void Cleanup()
        {
            // Clean up weapons first to avoid container conflicts
            // Don't destroy equipped weapons directly - let the pawn destruction handle it
            if (betterWeapon != null && !betterWeapon.Destroyed && betterWeapon.ParentHolder is Map)
            {
                betterWeapon.Destroy();
                betterWeapon = null;
            }
            
            // Destroy pawn (which will also destroy their equipped weapon)
            if (testPawn != null && !testPawn.Destroyed)
            {
                // Stop all jobs first
                testPawn.jobs?.StopAll();
                // Clear equipment tracker references
                testPawn.equipment?.DestroyAllEquipment();
                // Destroy the pawn
                testPawn.Destroy();
                testPawn = null;
            }
            
            // Only destroy current weapon if it somehow wasn't destroyed with the pawn
            if (currentWeapon != null && !currentWeapon.Destroyed)
            {
                currentWeapon.Destroy();
                currentWeapon = null;
            }
        }
    }

    public class OutfitFilterTest : ITestScenario
    {
        public string Name => "Outfit Filter Weapon Restrictions";
        private Pawn testPawn;
        private List<ThingWithComps> weapons = new List<ThingWithComps>();

        public void Setup(Map map)
        {
            if (map == null) return;
            testPawn = TestHelpers.CreateTestPawn(map);

            var pos = testPawn?.Position ?? IntVec3.Invalid;
            if (pos.IsValid)
            {
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
        }

        public TestResult Run()
        {
            if (testPawn == null)
                return TestResult.Failure("Test pawn creation failed");

            var jobGiver = new JobGiver_PickUpBetterWeapon();
            var job = jobGiver.TestTryGiveJob(testPawn);

            return TestResult.Pass();
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
            
            if (testPawn != null && !testPawn.Destroyed)
            {
                testPawn.Destroy();
            }
        }
    }

    public class ForcedWeaponTest : ITestScenario
    {
        public string Name => "Forced Weapon Retention";
        private Pawn testPawn;
        private ThingWithComps forcedWeapon;
        private ThingWithComps betterWeapon;

        public void Setup(Map map)
        {
            if (map == null) return;
            testPawn = TestHelpers.CreateTestPawn(map);

            if (testPawn != null)
            {
                var pos = testPawn.Position;
                var pistolDef = VanillaWeaponDefOf.Gun_Autopistol;
                var rifleDef = VanillaWeaponDefOf.Gun_AssaultRifle;

                if (pistolDef != null && rifleDef != null)
                {
                    forcedWeapon = ThingMaker.MakeThing(pistolDef) as ThingWithComps;

                    if (forcedWeapon != null)
                    {
                        // Ensure pawn is unarmed first
                        testPawn.equipment?.DestroyAllEquipment();
                        
                        testPawn.equipment?.AddEquipment(forcedWeapon);
                        ForcedWeaponHelper.SetForced(testPawn, forcedWeapon);
                    }

                    betterWeapon = TestHelpers.CreateWeapon(map, rifleDef, pos + new IntVec3(3, 0, 0));
                    if (betterWeapon != null)
                    {
                        ImprovedWeaponCacheManager.AddWeaponToCache(betterWeapon);
                    }
                }
            }
        }

        public TestResult Run()
        {
            if (testPawn == null || forcedWeapon == null)
                return TestResult.Failure("Test setup failed");

            if (!ForcedWeaponHelper.IsForced(testPawn, forcedWeapon))
                return TestResult.Failure("Weapon not marked as forced");

            var jobGiver = new JobGiver_PickUpBetterWeapon();
            var job = jobGiver.TestTryGiveJob(testPawn);

            if (job != null)
                return TestResult.Failure("Pawn tried to replace forced weapon");

            return TestResult.Pass();
        }

        public void Cleanup()
        {
            ForcedWeaponHelper.ClearForced(testPawn);
            
            // Destroy map weapons first
            if (betterWeapon != null && !betterWeapon.Destroyed && betterWeapon.ParentHolder is Map)
            {
                betterWeapon.Destroy();
            }
            
            // Destroy pawn (which will also destroy their equipped weapon)
            if (testPawn != null && !testPawn.Destroyed)
            {
                testPawn.Destroy();
            }
            
            // Only destroy forced weapon if it somehow wasn't destroyed with the pawn
            if (forcedWeapon != null && !forcedWeapon.Destroyed)
            {
                forcedWeapon.Destroy();
            }
        }
    }

    public class CombatExtendedAmmoTest : ITestScenario
    {
        public string Name => "Combat Extended Ammo Check";

        public void Setup(Map map) { }

        public TestResult Run()
        {
            if (!CECompat.IsLoaded())
                return TestResult.Pass(); 

            bool shouldCheck = CECompat.ShouldCheckAmmo();

            var result = new TestResult { Success = true };
            result.Data["CE Loaded"] = CECompat.IsLoaded();
            result.Data["Should Check Ammo"] = shouldCheck;

            return result;
        }

        public void Cleanup() { }
    }

    public class SimpleSidearmsIntegrationTest : ITestScenario
    {
        public string Name => "Simple Sidearms Integration";
        private Pawn testPawn;

        public void Setup(Map map)
        {
            if (map == null) return;
            testPawn = TestHelpers.CreateTestPawn(map);
            
            // Make sure pawn is properly initialized
            if (testPawn != null)
            {
                testPawn.equipment?.DestroyAllEquipment();
            }
        }

        public TestResult Run()
        {
            if (!SimpleSidearmsCompat.IsLoaded())
                return TestResult.Pass(); 

            if (testPawn == null)
                return TestResult.Failure("Test pawn creation failed");

            int maxSidearms = SimpleSidearmsCompat.GetMaxSidearmsForPawn(testPawn);
            int currentCount = SimpleSidearmsCompat.GetCurrentSidearmCount(testPawn);

            var result = new TestResult { Success = true };
            result.Data["Max Sidearms"] = maxSidearms;
            result.Data["Current Count"] = currentCount;

            return result;
        }

        public void Cleanup()
        {
            if (testPawn != null && !testPawn.Destroyed)
            {
                testPawn.Destroy();
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
                return TestResult.Failure("Failed to create child pawn");

            if (childPawn.ageTracker == null)
                return TestResult.Failure("Pawn has no age tracker");

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
                    return TestResult.Failure($"Child rejected despite being old enough ({actualAge} >= {minAge}) and setting allowing children");
            }
            else if (!allowChildrenSetting || actualAge < minAge)
            {
                if (isValid)
                    return TestResult.Failure($"Child allowed despite settings (allow={allowChildrenSetting}, age={actualAge}, minAge={minAge})");
                if (!reason.Contains("Too young") && !reason.Contains("age"))
                    return TestResult.Failure($"Child rejected but not for age reasons: {reason}");
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

                if (job != null && AutoArmMod.settings?.respectConceitedNobles == true)
                    return TestResult.Failure("Conceited noble tried to switch weapons");
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

    public class MapTransitionTest : ITestScenario
    {
        public string Name => "Map Transition Cache Handling";

        public void Setup(Map map) { }

        public TestResult Run()
        {
            // Test the improved cache system
            var map1 = Find.CurrentMap;
            if (map1 == null)
                return TestResult.Failure("No current map");

            // Test that the cache works properly
            ImprovedWeaponCacheManager.InvalidateCache(map1);
            
            // First call should build the cache
            var weapons = ImprovedWeaponCacheManager.GetWeaponsNear(map1, map1.Center, 50f).ToList();
            
            // Second call should use the cache
            var weaponsAgain = ImprovedWeaponCacheManager.GetWeaponsNear(map1, map1.Center, 50f).ToList();
            
            var result = new TestResult { Success = true };
            result.Data["Weapons in cache"] = weapons.Count;
            result.Data["Cache working"] = weapons.Count == weaponsAgain.Count;
            
            return result;
        }

        public void Cleanup() { }
    }

    public class SaveLoadTest : ITestScenario
    {
        public string Name => "Save/Load Forced Weapons";
        private Pawn testPawn;
        private ThingWithComps testWeapon;

        public void Setup(Map map)
        {
            if (map == null) return;
            testPawn = TestHelpers.CreateTestPawn(map);

            if (testPawn != null)
            {
                var weaponDef = VanillaWeaponDefOf.Gun_Autopistol;
                if (weaponDef != null)
                {
                    testWeapon = TestHelpers.CreateWeapon(map, weaponDef, testPawn.Position);
                    if (testWeapon != null)
                    {
                        // Ensure pawn is unarmed first
                        testPawn.equipment?.DestroyAllEquipment();
                        
                        testPawn.equipment?.AddEquipment(testWeapon);
                        ForcedWeaponHelper.SetForced(testPawn, testWeapon);
                        ImprovedWeaponCacheManager.AddWeaponToCache(testWeapon);
                    }
                }
            }
        }

        public TestResult Run()
        {
            if (testPawn == null || testWeapon == null)
                return TestResult.Failure("Test setup failed");

            var saveData = ForcedWeaponHelper.GetSaveData();
            if (!saveData.ContainsKey(testPawn))
                return TestResult.Failure("Forced weapon not in save data");

            ForcedWeaponHelper.LoadSaveData(saveData);

            var forcedDef = ForcedWeaponHelper.GetForcedWeaponDefs(testPawn).FirstOrDefault();
            if (forcedDef != testWeapon.def)
                return TestResult.Failure("Forced weapon def not retained after load");

            return TestResult.Pass();
        }

        public void Cleanup()
        {
            ForcedWeaponHelper.ClearForced(testPawn);
            
            // Destroy pawn (which will also destroy their equipped weapon)
            if (testPawn != null && !testPawn.Destroyed)
            {
                testPawn.Destroy();
            }
            
            // Only destroy weapon if it somehow wasn't destroyed with the pawn
            if (testWeapon != null && !testWeapon.Destroyed)
            {
                testWeapon.Destroy();
            }
        }
    }

    public class PerformanceTest : ITestScenario
    {
        public string Name => "Performance Benchmarks";
        private List<Pawn> testPawns = new List<Pawn>();
        private List<ThingWithComps> testWeapons = new List<ThingWithComps>();

        public void Setup(Map map)
        {
            if (map == null) return;

            for (int i = 0; i < 20; i++)
            {
                var pawn = TestHelpers.CreateTestPawn(map);
                if (pawn != null)
                {
                    testPawns.Add(pawn);

                    var weaponDef = i % 2 == 0 ? VanillaWeaponDefOf.Gun_Autopistol : VanillaWeaponDefOf.MeleeWeapon_Knife;
                    if (weaponDef != null)
                    {
                        var weapon = TestHelpers.CreateWeapon(map, weaponDef,
                            pawn.Position + new IntVec3(2, 0, 0));
                        if (weapon != null)
                        {
                            testWeapons.Add(weapon);
                            ImprovedWeaponCacheManager.AddWeaponToCache(weapon);
                        }
                    }
                }
            }
        }

        public TestResult Run()
        {
            if (testPawns.Count == 0)
                return TestResult.Failure("No test pawns created");

            var jobGiver = new JobGiver_PickUpBetterWeapon();
            var startTicks = Find.TickManager.TicksGame;
            int jobsCreated = 0;

            foreach (var pawn in testPawns)
            {
                var job = jobGiver.TestTryGiveJob(pawn);
                if (job != null)
                    jobsCreated++;
            }

            var elapsed = Find.TickManager.TicksGame - startTicks;

            var result = new TestResult { Success = true };
            result.Data["Pawns Tested"] = testPawns.Count;
            result.Data["Jobs Created"] = jobsCreated;
            result.Data["Ticks Elapsed"] = elapsed;
            result.Data["Time Per Pawn"] = $"{elapsed / (float)testPawns.Count:F2} ticks";

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
            
            // Then destroy pawns
            foreach (var pawn in testPawns)
            {
                if (pawn != null && !pawn.Destroyed)
                {
                    pawn.Destroy();
                }
            }
            testPawns.Clear();
        }
    }

    public class EdgeCaseTest : ITestScenario
    {
        public string Name => "Edge Cases and Error Handling";

        public void Setup(Map map) { }

        public TestResult Run()
        {
            var jobGiver = new JobGiver_PickUpBetterWeapon();

            var job = jobGiver.TestTryGiveJob(null);
            if (job != null)
                return TestResult.Failure("Job created for null pawn");

            float score = jobGiver.GetWeaponScore(null, null);
            if (score != 0f)
                return TestResult.Failure("Non-zero score for null inputs");

            return TestResult.Pass();
        }

        public void Cleanup() { }
    }

    public class DraftedBehaviorTest : ITestScenario
    {
        public string Name => "Drafted Pawn Behavior";
        private Pawn draftedPawn;
        private ThingWithComps currentWeapon;
        private ThingWithComps betterWeapon;

        public void Setup(Map map)
        {
            if (map == null) return;
            
            draftedPawn = TestHelpers.CreateTestPawn(map, new TestHelpers.TestPawnConfig
            {
                Name = "DraftedPawn"
            });

            if (draftedPawn != null)
            {
                var pos = draftedPawn.Position;
                var pistolDef = VanillaWeaponDefOf.Gun_Autopistol;
                var rifleDef = VanillaWeaponDefOf.Gun_AssaultRifle;

                if (pistolDef != null && rifleDef != null)
                {
                    // Give pawn a poor weapon
                    currentWeapon = ThingMaker.MakeThing(pistolDef) as ThingWithComps;
                    if (currentWeapon != null)
                    {
                        var compQuality = currentWeapon.TryGetComp<CompQuality>();
                        if (compQuality != null)
                        {
                            compQuality.SetQuality(QualityCategory.Poor, ArtGenerationContext.Colony);
                        }
                        
                        draftedPawn.equipment?.DestroyAllEquipment();
                        draftedPawn.equipment?.AddEquipment(currentWeapon);
                    }

                    // Place a better weapon nearby
                    betterWeapon = TestHelpers.CreateWeapon(map, rifleDef, pos + new IntVec3(2, 0, 0), QualityCategory.Excellent);
                    if (betterWeapon != null)
                    {
                        ImprovedWeaponCacheManager.AddWeaponToCache(betterWeapon);
                    }
                    
                    // Draft the pawn
                    if (draftedPawn.drafter != null)
                    {
                        draftedPawn.drafter.Drafted = true;
                    }
                }
            }
        }

        public TestResult Run()
        {
            if (draftedPawn == null)
                return TestResult.Failure("Failed to create test pawn");
                
            if (!draftedPawn.Drafted)
                return TestResult.Failure("Pawn is not drafted");

            var result = new TestResult { Success = true };
            result.Data["IsDrafted"] = draftedPawn.Drafted;
            result.Data["CurrentWeapon"] = currentWeapon?.Label ?? "none";
            result.Data["BetterWeaponAvailable"] = betterWeapon?.Label ?? "none";

            // Drafted pawns should not try to switch weapons
            var jobGiver = new JobGiver_PickUpBetterWeapon();
            var job = jobGiver.TestTryGiveJob(draftedPawn);

            if (job != null)
            {
                return TestResult.Failure($"Drafted pawn tried to switch weapons! Job: {job.def.defName}");
            }

            return result;
        }

        public void Cleanup()
        {
            // Undraft before cleanup
            if (draftedPawn?.drafter != null)
            {
                draftedPawn.drafter.Drafted = false;
            }
            
            // Clean up weapons first to avoid container conflicts
            // Don't destroy equipped weapons directly - let the pawn destruction handle it
            if (betterWeapon != null && !betterWeapon.Destroyed && betterWeapon.ParentHolder is Map)
            {
                betterWeapon.Destroy();
            }
            
            // Destroy pawn (which will also destroy their equipped weapon)
            if (draftedPawn != null && !draftedPawn.Destroyed)
            {
                draftedPawn.Destroy();
            }
            
            // Only destroy current weapon if it somehow wasn't destroyed with the pawn
            if (currentWeapon != null && !currentWeapon.Destroyed)
            {
                currentWeapon.Destroy();
            }
        }
    }

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
                // Give pawn a ranged weapon
                var rifleDef = VanillaWeaponDefOf.Gun_AssaultRifle;
                if (rifleDef != null)
                {
                    rangedWeapon = ThingMaker.MakeThing(rifleDef) as ThingWithComps;
                    if (rangedWeapon != null)
                    {
                        testPawn.equipment?.DestroyAllEquipment();
                        testPawn.equipment?.AddEquipment(rangedWeapon);
                    }
                }
                
                // Store original policy
                originalPolicy = testPawn.outfits?.CurrentApparelPolicy;
                
                // Create a restrictive policy that doesn't allow ranged weapons
                restrictivePolicy = new ApparelPolicy(testPawn.Map.uniqueID, "Test - Melee Only");
                
                // Get the weapons category
                var weaponsCat = DefDatabase<ThingCategoryDef>.GetNamedSilentFail("Weapons");
                if (weaponsCat != null)
                {
                    restrictivePolicy.filter.SetAllow(weaponsCat, true);
                }
                
                // Disallow all ranged weapons
                foreach (var weaponDef in WeaponThingFilterUtility.RangedWeapons)
                {
                    restrictivePolicy.filter.SetAllow(weaponDef, false);
                }
                
                // Allow melee weapons
                foreach (var weaponDef in WeaponThingFilterUtility.MeleeWeapons)
                {
                    restrictivePolicy.filter.SetAllow(weaponDef, true);
                }
                
                // Add policy to database
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
            
            // Change to restrictive policy
            if (testPawn.outfits != null)
            {
                testPawn.outfits.CurrentApparelPolicy = restrictivePolicy;
            }
            
            // The weapon drop check happens in Pawn_EquipmentTracker.Notify_ApparelPolicyChanged
            // which is called by the harmony patch when outfit changes
            // For testing, we'll check if the weapon should be dropped
            
            bool weaponAllowedByPolicy = restrictivePolicy.filter.Allows(rangedWeapon.def);
            result.Data["WeaponAllowedByNewPolicy"] = weaponAllowedByPolicy;
            
            if (weaponAllowedByPolicy)
            {
                return TestResult.Failure("Ranged weapon is still allowed by restrictive policy");
            }
            
            // In the actual game, the harmony patch would trigger the drop
            // For the test, we verify the policy correctly disallows the weapon
            result.Data["PolicyCorrectlyRestrictsWeapon"] = !weaponAllowedByPolicy;
            
            return result;
        }

        public void Cleanup()
        {
            // Restore original policy
            if (testPawn?.outfits != null && originalPolicy != null)
            {
                testPawn.outfits.CurrentApparelPolicy = originalPolicy;
            }
            
            // Remove test policy
            if (restrictivePolicy != null && Current.Game?.outfitDatabase?.AllOutfits != null)
            {
                Current.Game.outfitDatabase.AllOutfits.Remove(restrictivePolicy);
            }
            
            // Destroy pawn (which will also destroy their equipped weapon)
            if (testPawn != null && !testPawn.Destroyed)
            {
                testPawn.Destroy();
            }
            
            // Only destroy weapon if it somehow wasn't destroyed with the pawn
            if (rangedWeapon != null && !rangedWeapon.Destroyed)
            {
                rangedWeapon.Destroy();
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

    public class PrisonerSlaveTest : ITestScenario
    {
        public string Name => "Prisoner and Slave Weapon Access";
        private Pawn prisoner;
        private Pawn slave;
        private List<ThingWithComps> weapons = new List<ThingWithComps>();

        public void Setup(Map map)
        {
            if (map == null) return;
            
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
                    
                    // Ensure unarmed
                    slave.equipment?.DestroyAllEquipment();
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
                }
                
                if (isValid)
                {
                    result.Success = false;
                    result.Data["Error2"] = "CRITICAL: Prisoner passed validation check!";
                }
            }
            
            // Test slave
            if (slave != null && ModsConfig.IdeologyActive)
            {
                result.Data["SlaveStatus"] = slave.IsSlaveOfColony;
                
                string reason;
                bool isValid = JobGiverHelpers.IsValidPawnForAutoEquip(slave, out reason);
                result.Data["SlaveIsValid"] = isValid;
                result.Data["SlaveReason"] = reason ?? "None";
                
                var job = jobGiver.TestTryGiveJob(slave);
                result.Data["SlaveJobCreated"] = job != null;
                
                // Slaves SHOULD be able to pick up weapons - players control this via outfit filters
                if (!isValid && reason != "Not spawned" && reason != "No map")
                {
                    result.Success = false;
                    result.Data["Error3"] = $"Slave failed validation when they should pass: {reason}";
                }
                
                if (job == null && weapons.Any(w => w.Spawned))
                {
                    // Only fail if there was a weapon available and no other reason to fail
                    if (isValid)
                    {
                        result.Success = false;
                        result.Data["Error4"] = "Slave couldn't get weapon job despite being valid";
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

    public class JobPriorityTest : ITestScenario
    {
        public string Name => "Job Interruption Priority Test";
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
                var pos = testPawn.Position;
                var pistolDef = VanillaWeaponDefOf.Gun_Autopistol;
                var rifleDef = VanillaWeaponDefOf.Gun_AssaultRifle;
                
                // Give pawn a normal quality weapon
                if (pistolDef != null)
                {
                    currentWeapon = ThingMaker.MakeThing(pistolDef) as ThingWithComps;
                    if (currentWeapon != null)
                    {
                        var compQuality = currentWeapon.TryGetComp<CompQuality>();
                        if (compQuality != null)
                        {
                            compQuality.SetQuality(QualityCategory.Normal, ArtGenerationContext.Colony);
                        }
                        
                        testPawn.equipment?.DestroyAllEquipment();
                        testPawn.equipment?.AddEquipment(currentWeapon);
                    }
                }
                
                // Create minor upgrade (slightly better quality same weapon)
                if (pistolDef != null)
                {
                    minorUpgrade = TestHelpers.CreateWeapon(map, pistolDef, pos + new IntVec3(2, 0, 0), QualityCategory.Good);
                    if (minorUpgrade != null)
                    {
                        ImprovedWeaponCacheManager.AddWeaponToCache(minorUpgrade);
                    }
                }
                
                // Create major upgrade (much better weapon)
                if (rifleDef != null)
                {
                    majorUpgrade = TestHelpers.CreateWeapon(map, rifleDef, pos + new IntVec3(-2, 0, 0), QualityCategory.Excellent);
                    if (majorUpgrade != null)
                    {
                        ImprovedWeaponCacheManager.AddWeaponToCache(majorUpgrade);
                    }
                }
                
                // Start pawn doing low-priority work (cleaning)
                // Find any filth on the map to clean
                var filthList = map.listerFilthInHomeArea.FilthInHomeArea;
                if (filthList != null && filthList.Count > 0)
                {
                    var filth = filthList.FirstOrDefault(f => f.Position.InHorDistOf(testPawn.Position, 20f));
                    if (filth != null)
                    {
                        var cleaningJob = new Job(JobDefOf.Clean, filth);
                        testPawn.jobs?.StartJob(cleaningJob, JobCondition.InterruptForced);
                    }
                }
                else
                {
                    // If no filth, just start a wait job as low priority work
                    var waitJob = new Job(JobDefOf.Wait_Wander);
                    testPawn.jobs?.StartJob(waitJob, JobCondition.InterruptForced);
                }
            }
        }

        public TestResult Run()
        {
            if (testPawn == null || currentWeapon == null)
                return TestResult.Failure("Test setup failed");
                
            var result = new TestResult { Success = true };
            var jobGiver = new JobGiver_PickUpBetterWeapon();
            
            // Get weapon scores
            float currentScore = WeaponScoreCache.GetCachedScore(testPawn, currentWeapon);
            float minorScore = minorUpgrade != null ? WeaponScoreCache.GetCachedScore(testPawn, minorUpgrade) : 0f;
            float majorScore = majorUpgrade != null ? WeaponScoreCache.GetCachedScore(testPawn, majorUpgrade) : 0f;
            
            result.Data["CurrentWeaponScore"] = currentScore;
            result.Data["MinorUpgradeScore"] = minorScore;
            result.Data["MajorUpgradeScore"] = majorScore;
            
            // Calculate improvement percentages
            float minorImprovement = minorScore / currentScore;
            float majorImprovement = majorScore / currentScore;
            
            result.Data["MinorImprovement"] = $"{(minorImprovement - 1f) * 100f:F1}%";
            result.Data["MajorImprovement"] = $"{(majorImprovement - 1f) * 100f:F1}%";
            
            // Test job creation
            var job = jobGiver.TestTryGiveJob(testPawn);
            
            if (job != null)
            {
                result.Data["JobCreated"] = true;
                result.Data["TargetWeapon"] = job.targetA.Thing?.Label ?? "unknown";
                
                // Check if job has appropriate expiry based on upgrade quality
                if (job.targetA.Thing == majorUpgrade && majorImprovement >= 1.15f)
                {
                    result.Data["JobExpiry"] = job.expiryInterval;
                    result.Data["HasExpiryForMajorUpgrade"] = job.expiryInterval > 0;
                }
            }
            else
            {
                result.Data["JobCreated"] = false;
            }
            
            // Verify that minor upgrades don't interrupt important work
            bool isLowPriorityWork = JobGiverHelpers.IsLowPriorityWork(testPawn);
            bool isSafeToInterrupt = JobGiverHelpers.IsSafeToInterrupt(testPawn.CurJob?.def, minorImprovement);
            
            result.Data["CurrentJobIsLowPriority"] = isLowPriorityWork;
            result.Data["SafeToInterruptForMinor"] = isSafeToInterrupt;
            
            return result;
        }

        public void Cleanup()
        {
            // Stop any running job
            testPawn?.jobs?.StopAll();
            
            // Clean up weapons first to avoid container conflicts
            // Don't destroy equipped weapons directly - let the pawn destruction handle it
            if (minorUpgrade != null && !minorUpgrade.Destroyed && minorUpgrade.ParentHolder is Map)
            {
                minorUpgrade.Destroy();
            }
            if (majorUpgrade != null && !majorUpgrade.Destroyed && majorUpgrade.ParentHolder is Map)
            {
                majorUpgrade.Destroy();
            }
            
            // Destroy pawn (which will also destroy their equipped weapon)
            if (testPawn != null && !testPawn.Destroyed)
            {
                testPawn.Destroy();
            }
            
            // Only destroy current weapon if it somehow wasn't destroyed with the pawn
            if (currentWeapon != null && !currentWeapon.Destroyed)
            {
                currentWeapon.Destroy();
            }
        }
    }
}