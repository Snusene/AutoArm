// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: Authentic raid test for disableDuringRaids setting
// Creates realistic raid conditions using game's raid generation

using RimWorld;
using System.Linq;
using Verse;
using Verse.AI.Group;
using AutoArm.Caching; using AutoArm.Helpers; using AutoArm.Logging; using AutoArm.Testing;
using AutoArm.Jobs;
using AutoArm.Definitions;
using AutoArm.Testing.Helpers;

namespace AutoArm.Testing.Scenarios
{
    public class AuthenticRaidTest : ITestScenario
    {
        private Map testMap;
        public string Name => "Authentic Raid Detection";
        private Pawn testPawn;
        private ThingWithComps availableWeapon;
        private Lord raidLord;
        private IncidentParms raidParms;

        public void Setup(Map map)
        {
            if (map == null) return;
            testMap = map;

            // Create test colonist
            testPawn = TestHelpers.CreateTestPawn(map);
            if (testPawn != null)
            {
                testPawn.equipment?.DestroyAllEquipment();
            }

            // Place a weapon for the colonist to potentially pick up
            availableWeapon = TestHelpers.CreateWeapon(
                map, 
                TestWeaponProvider.GetAnyRangedWeapon(),
                testPawn.Position + new IntVec3(2, 0, 0)
            );

            if (availableWeapon != null)
            {
                ImprovedWeaponCacheManager.AddWeaponToCache(availableWeapon);
            }

            // Create authentic raid parameters
            var raidFaction = Find.FactionManager.AllFactions
                .Where(f => f.HostileTo(Faction.OfPlayer) && f.def.humanlikeFaction)
                .FirstOrDefault();

            if (raidFaction == null)
            {
                AutoArmLogger.Log("[TEST] No hostile faction found - creating one");
                // Create a hostile faction for testing
                var factionDef = DefDatabase<FactionDef>.GetNamedSilentFail("Pirate") ??
                               DefDatabase<FactionDef>.AllDefs.FirstOrDefault(f => f.humanlikeFaction && f.permanentEnemy);
                
                if (factionDef != null)
                {
                    raidFaction = FactionGenerator.NewGeneratedFaction(new FactionGeneratorParms(factionDef, default(IdeoGenerationParms), null));
                    raidFaction.SetRelationDirect(Faction.OfPlayer, FactionRelationKind.Hostile);
                }
            }

            if (raidFaction != null)
            {
                // Create incident parameters that match actual raid generation
                raidParms = new IncidentParms
                {
                    target = map,
                    faction = raidFaction,
                    raidStrategy = DefDatabase<RaidStrategyDef>.GetNamedSilentFail("ImmediateAttack") ?? 
                                  DefDatabase<RaidStrategyDef>.AllDefs.FirstOrDefault(),
                    points = 500f, // Moderate raid size
                    raidArrivalMode = DefDatabase<PawnsArrivalModeDef>.GetNamedSilentFail("EdgeWalkIn") ??
                                     DefDatabase<PawnsArrivalModeDef>.AllDefs.FirstOrDefault()
                };
            }
        }

        public TestResult Run()
        {
            var result = new TestResult { Success = true };
            var jobGiver = new JobGiver_PickUpBetterWeapon();

            // Store original setting
            bool originalSetting = AutoArmMod.settings?.disableDuringRaids ?? false;

            // First test - no raid, should create job regardless of setting
            AutoArmMod.settings.disableDuringRaids = true;
            var jobNoRaid = jobGiver.TestTryGiveJob(testPawn);
            result.Data["NoRaid_JobCreated"] = jobNoRaid != null;
            result.Data["NoRaid_Setting"] = true;

            if (jobNoRaid == null && availableWeapon != null && availableWeapon.Spawned)
            {
                result.Success = false;
                result.Data["Error"] = "Failed to create job when no raid active";
                AutoArmLogger.LogError("[TEST] AuthenticRaidTest: No job created when no raid active");
            }

            // Create authentic raid
            if (raidParms != null)
            {
                AutoArmLogger.Log("[TEST] Creating authentic raid...");
                
                // Generate raid pawns using actual game mechanics
                var pawns = PawnGroupMakerUtility.GeneratePawns(new PawnGroupMakerParms
                {
                    groupKind = PawnGroupKindDefOf.Combat,
                    faction = raidParms.faction,
                    points = raidParms.points,
                    tile = testMap.Tile
                }).ToList();

                if (pawns.Any())
                {
                    // Spawn raiders at map edge
                    var spawnCenter = CellFinder.RandomEdgeCell(testMap);
                    
                    foreach (var raider in pawns)
                    {
                        GenSpawn.Spawn(raider, CellFinder.RandomSpawnCellForPawnNear(spawnCenter, testMap), testMap);
                    }

                    // Create authentic raid lord with assault job
                    var lordJob = new LordJob_AssaultColony(raidParms.faction);
                    raidLord = LordMaker.MakeNewLord(raidParms.faction, lordJob, testMap, pawns);

                    result.Data["RaidersSpawned"] = pawns.Count;
                    result.Data["RaidLordType"] = lordJob.GetType().Name;
                    result.Data["RaidFaction"] = raidParms.faction.Name;

                    // Give game a moment to process (in real test would advance ticks)
                    AutoArmLogger.Log($"[TEST] Raid created with {pawns.Count} raiders");

                    // Test with raid active
                    AutoArmMod.settings.disableDuringRaids = true;
                    
                    // Clear cooldowns to ensure fresh test
                    TimingHelper.ClearAllCooldowns();
                    
                    bool raidDetected = JobGiver_PickUpBetterWeapon.IsRaidActive(testMap);
                    result.Data["RaidDetected"] = raidDetected;

                    var jobDuringRaid = jobGiver.TestTryGiveJob(testPawn);
                    result.Data["DuringRaid_JobCreated"] = jobDuringRaid != null;
                    result.Data["DuringRaid_Setting"] = true;

                    if (jobDuringRaid != null)
                    {
                        result.Success = false;
                        result.Data["Error2"] = "Job created during active raid when disabled";
                        AutoArmLogger.LogError("[TEST] AuthenticRaidTest: Job created during raid when setting disables it");
                    }

                    // Test with setting disabled
                    AutoArmMod.settings.disableDuringRaids = false;
                    TimingHelper.ClearAllCooldowns();
                    
                    var jobRaidAllowed = jobGiver.TestTryGiveJob(testPawn);
                    result.Data["RaidAllowed_JobCreated"] = jobRaidAllowed != null;
                    result.Data["RaidAllowed_Setting"] = false;

                    if (jobRaidAllowed == null && availableWeapon != null && availableWeapon.Spawned)
                    {
                        result.Success = false;
                        result.Data["Error3"] = "Job not created during raid when setting allows it";
                        AutoArmLogger.LogError("[TEST] AuthenticRaidTest: No job during raid when setting allows it");
                    }
                }
                else
                {
                    result.Data["Warning"] = "Failed to generate raid pawns";
                    AutoArmLogger.Warn("[TEST] Failed to generate raid pawns");
                }
            }
            else
            {
                result.Data["Warning"] = "Could not create raid parameters - no hostile faction";
            }

            // Restore original setting
            AutoArmMod.settings.disableDuringRaids = originalSetting;

            return result;
        }

        public void Cleanup()
        {
            // Clean up raid
            if (raidLord != null && !raidLord.AnyActivePawn)
            {
                raidLord.Cleanup();
            }
            else if (raidLord?.ownedPawns != null)
            {
                // Despawn all raiders
                foreach (var pawn in raidLord.ownedPawns.ToList())
                {
                    if (pawn.Spawned)
                        pawn.DeSpawn();
                    if (!pawn.Destroyed)
                        pawn.Destroy();
                }
                raidLord.Cleanup();
            }

            availableWeapon?.Destroy();
            testPawn?.Destroy();
        }
    }
}


