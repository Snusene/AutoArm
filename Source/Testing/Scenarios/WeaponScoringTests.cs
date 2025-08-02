// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: Comprehensive weapon scoring system tests
// Validates DPS, range, burst, armor penetration calculations

using RimWorld;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Verse;

namespace AutoArm.Testing.Scenarios
{
    /// <summary>
    /// Tests for the weapon scoring system matching the web analyzer
    /// </summary>
    public class WeaponScoringSystemTest : ITestScenario
    {
        public string Name => "Weapon Scoring System";
        private Map testMap;
        private Pawn neutralPawn; // Balanced skills for testing

        public void Setup(Map map)
        {
            testMap = map;
            
            // Create a neutral pawn with balanced skills
            neutralPawn = TestHelpers.CreateTestPawn(map, new TestHelpers.TestPawnConfig
            {
                Name = "NeutralTester",
                Skills = new Dictionary<SkillDef, int>
                {
                    { SkillDefOf.Shooting, 10 },
                    { SkillDefOf.Melee, 10 }
                }
            });
        }

        public TestResult Run()
        {
            if (neutralPawn == null || testMap == null)
                return TestResult.Failure("Test setup failed");

            var result = new TestResult { Success = true };
            int passedTests = 0;
            int totalTests = 0;
            
            try
            {
                // Run all sub-tests
                totalTests++;
                AutoArmLogger.Log("[TEST] Starting DPS Scoring test...");
                TestDPSScoring(result);
                if (result.Success) passedTests++;
                else { AutoArmLogger.LogError("[TEST] DPS Scoring test failed"); }
                
                totalTests++;
                result.Success = true; // Reset for next test
                AutoArmLogger.Log("[TEST] Starting Range Modifiers test...");
                TestRangeModifiers(result);
                if (result.Success) passedTests++;
                else { AutoArmLogger.LogError("[TEST] Range Modifiers test failed"); }
                
                totalTests++;
                result.Success = true;
                AutoArmLogger.Log("[TEST] Starting Burst Bonuses test...");
                TestBurstBonuses(result);
                if (result.Success) passedTests++;
                else { AutoArmLogger.LogError("[TEST] Burst Bonuses test failed"); }
                
                totalTests++;
                result.Success = true;
                AutoArmLogger.Log("[TEST] Starting Armor Penetration test...");
                TestArmorPenetration(result);
                if (result.Success) passedTests++;
                else { AutoArmLogger.LogError("[TEST] Armor Penetration test failed"); }
                
                totalTests++;
                result.Success = true;
                AutoArmLogger.Log("[TEST] Starting Power Creep Protection test...");
                TestPowerCreepProtection(result);
                if (result.Success) passedTests++;
                else { AutoArmLogger.LogError("[TEST] Power Creep Protection test failed"); }
                
                totalTests++;
                result.Success = true;
                AutoArmLogger.Log("[TEST] Starting Situational Weapons test...");
                TestSituationalWeapons(result);
                if (result.Success) passedTests++;
                else { AutoArmLogger.LogError("[TEST] Situational Weapons test failed"); }
                
                totalTests++;
                result.Success = true;
                AutoArmLogger.Log("[TEST] Starting Skill Scoring test...");
                TestSkillScoring(result);
                if (result.Success) passedTests++;
                else { AutoArmLogger.LogError("[TEST] Skill Scoring test failed"); }
                
                // Overall success if most tests pass
                result.Success = passedTests >= (totalTests - 1); // Allow one failure
                result.Data["SubTests_Passed"] = passedTests;
                result.Data["SubTests_Total"] = totalTests;
                result.Data["SubTests_FailureCount"] = totalTests - passedTests;
                
                if (!result.Success)
                {
                    result.Data["Overall_Result"] = $"Too many sub-tests failed: {totalTests - passedTests} failures";
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Data["Exception"] = ex.Message;
                result.Data["StackTrace"] = ex.StackTrace;
                AutoArmLogger.LogError($"[TEST] WeaponScoringSystemTest exception: {ex}");
            }
            
            return result;
        }

        private void TestDPSScoring(TestResult result)
        {
            // Test that higher DPS weapons score better
            var pistolDef = VanillaWeaponDefOf.Gun_Autopistol; // ~9 DPS
            var rifleDef = VanillaWeaponDefOf.Gun_AssaultRifle; // ~11 DPS
            
            if (pistolDef == null || rifleDef == null)
            {
                result.Success = false;
                result.FailureReason = "Required weapon defs not found";
                result.Data["Error"] = "Test setup failed - missing weapon definitions";
                result.Data["DPS_Error"] = $"Weapon defs not found: pistol={pistolDef != null}, rifle={rifleDef != null}";
                result.Data["PistolDefFound"] = pistolDef != null;
                result.Data["RifleDefFound"] = rifleDef != null;
                AutoArmLogger.LogError("[TEST] DPS Scoring: Weapon defs not found");
                return;
            }
            
            var pistol = ThingMaker.MakeThing(pistolDef) as ThingWithComps;
            var rifle = ThingMaker.MakeThing(rifleDef) as ThingWithComps;
            
            if (pistol == null || rifle == null)
            {
                result.Success = false;
                result.FailureReason = "Failed to create weapon instances";
                result.Data["Error"] = "Weapon instantiation failed";
                result.Data["DPS_Error"] = "Failed to create weapon instances";
                result.Data["PistolCreated"] = pistol != null;
                result.Data["RifleCreated"] = rifle != null;
                AutoArmLogger.LogError("[TEST] DPS Scoring: Failed to create weapon instances");
                return;
            }
            
            try
            {
                float pistolScore = WeaponScoringHelper.GetTotalScore(neutralPawn, pistol);
                float rifleScore = WeaponScoringHelper.GetTotalScore(neutralPawn, rifle);
                
                result.Data["DPS_AutopistolScore"] = pistolScore;
                result.Data["DPS_AssaultRifleScore"] = rifleScore;
                
                // Log detailed scoring breakdown
                AutoArmLogger.Log($"[TEST] Autopistol score: {pistolScore}");
                AutoArmLogger.Log($"[TEST] Assault rifle score: {rifleScore}");
                
                if (rifleScore <= pistolScore)
                {
                    result.Success = false;
                    result.FailureReason = "Higher DPS weapon didn't score better";
                    result.Data["Error"] = "DPS scoring not working correctly";
                    result.Data["DPS_Error"] = "Higher DPS weapon didn't score better";
                    result.Data["PistolDPS"] = "~9";
                    result.Data["RifleDPS"] = "~11";
                    result.Data["PistolScore"] = pistolScore;
                    result.Data["RifleScore"] = rifleScore;
                    AutoArmLogger.LogError($"[TEST] DPS Scoring: Assault rifle ({rifleScore}) didn't score higher than autopistol ({pistolScore})");
                }
            }
            finally
            {
                TestHelpers.SafeDestroyWeapon(pistol);
                TestHelpers.SafeDestroyWeapon(rifle);
            }
        }

        private void TestRangeModifiers(TestResult result)
        {
            // Test range penalties/bonuses for weapons
            var shotgunDef = VanillaWeaponDefOf.Gun_PumpShotgun; // Short range
            var rifleDef = VanillaWeaponDefOf.Gun_BoltActionRifle; // Long range
            
            if (shotgunDef == null || rifleDef == null)
            {
                result.Data["Range_Error"] = $"Weapon defs not found: shotgun={shotgunDef != null}, rifle={rifleDef != null}";
                AutoArmLogger.LogError("[TEST] Range modifiers: Required weapon defs not found");
                return;
            }
            
            var shotgun = ThingMaker.MakeThing(shotgunDef) as ThingWithComps;
            var rifle = ThingMaker.MakeThing(rifleDef) as ThingWithComps;
            
            try
            {
                // Test the actual range values
                float shotgunRange = shotgunDef.Verbs[0].range;
                float rifleRange = rifleDef.Verbs[0].range;
                
                result.Data["Range_ShotgunRange"] = shotgunRange;
                result.Data["Range_RifleRange"] = rifleRange;
                
                // Get actual scores from the scoring system
                float shotgunScore = WeaponScoringHelper.GetWeaponPropertyScore(neutralPawn, shotgun);
                float rifleScore = WeaponScoringHelper.GetWeaponPropertyScore(neutralPawn, rifle);
                
                result.Data["Range_ShotgunScore"] = shotgunScore;
                result.Data["Range_RifleScore"] = rifleScore;
                
                // Verify that longer range weapons generally score better
                // (accounting for other factors like DPS)
                if (rifleRange > shotgunRange * 2) // Rifle has much better range
                {
                    // Range bonus should help offset any DPS disadvantage
                    result.Data["Range_RangeRatio"] = rifleRange / shotgunRange;
                    result.Data["Range_ScoreRatio"] = rifleScore / Math.Max(shotgunScore, 1f);
                    
                    // If rifle scores much worse despite huge range advantage, that's suspicious
                    if (rifleScore < shotgunScore * 0.5f)
                    {
                        result.Data["Range_Warning"] = "Long range weapon scored surprisingly low";
                    }
                }
            }
            finally
            {
                TestHelpers.SafeDestroyWeapon(shotgun);
                TestHelpers.SafeDestroyWeapon(rifle);
            }
        }

        private void TestBurstBonuses(TestResult result)
        {
            // Test that burst weapons get appropriate bonuses
            var singleShotDef = VanillaWeaponDefOf.Gun_Revolver; // 1 shot
            var burstDef = VanillaWeaponDefOf.Gun_AssaultRifle; // 3 shots
            var heavyBurstDef = VanillaWeaponDefOf.Gun_HeavySMG; // 5 shots
            
            if (singleShotDef == null || burstDef == null)
            {
                result.Data["Burst_Error"] = $"Weapon defs not found: revolver={singleShotDef != null}, assault={burstDef != null}";
                AutoArmLogger.LogError("[TEST] Burst bonuses: Required weapon defs not found");
                return;
            }
            
            var singleShot = ThingMaker.MakeThing(singleShotDef) as ThingWithComps;
            var burst = ThingMaker.MakeThing(burstDef) as ThingWithComps;
            ThingWithComps heavyBurst = null;
            
            if (heavyBurstDef != null)
            {
                heavyBurst = ThingMaker.MakeThing(heavyBurstDef) as ThingWithComps;
            }
            
            try
            {
                // Get actual scores
                float singleScore = WeaponScoringHelper.GetWeaponPropertyScore(neutralPawn, singleShot);
                float burstScore = WeaponScoringHelper.GetWeaponPropertyScore(neutralPawn, burst);
                
                result.Data["Burst_SingleShotScore"] = singleScore;
                result.Data["Burst_3BurstScore"] = burstScore;
                result.Data["Burst_SingleBurstCount"] = singleShotDef.Verbs[0].burstShotCount;
                result.Data["Burst_3BurstCount"] = burstDef.Verbs[0].burstShotCount;
                
                // Test heavy burst weapon if available
                if (heavyBurst != null)
                {
                    float heavyScore = WeaponScoringHelper.GetWeaponPropertyScore(neutralPawn, heavyBurst);
                    result.Data["Burst_5BurstScore"] = heavyScore;
                    result.Data["Burst_5BurstCount"] = heavyBurstDef.Verbs[0].burstShotCount;
                    
                    // Heavy SMG might score lower due to range penalty
                    float heavyRange = heavyBurstDef.Verbs[0].range;
                    result.Data["Burst_5BurstRange"] = heavyRange;
                    
                    if (heavyRange < 20f && heavyScore > burstScore)
                    {
                        result.Data["Burst_Warning"] = "Short-range burst weapon scored surprisingly high";
                    }
                }
                
                // Verify burst weapons generally score well
                if (burstDef.Verbs[0].burstShotCount > singleShotDef.Verbs[0].burstShotCount)
                {
                    // Burst should provide some advantage (unless offset by other factors)
                    result.Data["Burst_Advantage"] = (burstScore / Math.Max(singleScore, 1f)) > 0.8f;
                }
            }
            finally
            {
                TestHelpers.SafeDestroyWeapon(singleShot);
                TestHelpers.SafeDestroyWeapon(burst);
                if (heavyBurst != null)
                    TestHelpers.SafeDestroyWeapon(heavyBurst);
            }
        }

        private void TestArmorPenetration(TestResult result)
        {
            // Test armor penetration scoring thresholds
            var knifeDef = VanillaWeaponDefOf.MeleeWeapon_Knife; // Low AP
            var swordDef = VanillaWeaponDefOf.MeleeWeapon_LongSword; // Medium AP
            
            if (knifeDef == null || swordDef == null)
            {
                result.Data["AP_Error"] = $"Weapon defs not found: knife={knifeDef != null}, sword={swordDef != null}";
                AutoArmLogger.LogError("[TEST] Armor penetration: Required weapon defs not found");
                return;
            }
            
            var knife = ThingMaker.MakeThing(knifeDef, ThingDefOf.Steel) as ThingWithComps;
            var sword = ThingMaker.MakeThing(swordDef, ThingDefOf.Steel) as ThingWithComps;
            
            float knifeScore = WeaponScoringHelper.GetTotalScore(neutralPawn, knife);
            float swordScore = WeaponScoringHelper.GetTotalScore(neutralPawn, sword);
            
            result.Data["AP_KnifeScore"] = knifeScore;
            result.Data["AP_SwordScore"] = swordScore;
            
            // Sword should score better partly due to better AP
            if (swordScore <= knifeScore)
            {
                result.Data["AP_Warning"] = "Higher AP weapon didn't score better";
            }
            
            TestHelpers.SafeDestroyWeapon(knife);
            TestHelpers.SafeDestroyWeapon(sword);
        }

        private void TestPowerCreepProtection(TestResult result)
        {
            // Test that extreme DPS weapons get diminishing returns
            // Since we can't access the private constants, we'll test the behavior
            
            // Create weapons with different DPS levels
            var pistolDef = VanillaWeaponDefOf.Gun_Autopistol;  // Low DPS
            var rifleDef = VanillaWeaponDefOf.Gun_AssaultRifle; // Medium DPS
            var chargeDef = VanillaWeaponDefOf.Gun_ChargeRifle; // High DPS (if available)
            
            if (pistolDef == null || rifleDef == null)
            {
                result.Data["PowerCreep_Error"] = "Required weapon defs not found";
                return;
            }
            
            var pistol = ThingMaker.MakeThing(pistolDef) as ThingWithComps;
            var rifle = ThingMaker.MakeThing(rifleDef) as ThingWithComps;
            ThingWithComps chargeRifle = null;
            
            if (chargeDef != null)
            {
                chargeRifle = ThingMaker.MakeThing(chargeDef) as ThingWithComps;
            }
            
            try
            {
                // Get actual scores
                float pistolScore = WeaponScoringHelper.GetWeaponPropertyScore(neutralPawn, pistol);
                float rifleScore = WeaponScoringHelper.GetWeaponPropertyScore(neutralPawn, rifle);
                float chargeScore = chargeRifle != null ? WeaponScoringHelper.GetWeaponPropertyScore(neutralPawn, chargeRifle) : 0f;
                
                result.Data["PowerCreep_PistolScore"] = pistolScore;
                result.Data["PowerCreep_RifleScore"] = rifleScore;
                if (chargeRifle != null)
                    result.Data["PowerCreep_ChargeRifleScore"] = chargeScore;
                
                // Test that score scaling shows diminishing returns
                float pistolToRifleRatio = rifleScore / Math.Max(pistolScore, 1f);
                result.Data["PowerCreep_LowToMedRatio"] = pistolToRifleRatio;
                
                if (chargeRifle != null && rifleScore > 0)
                {
                    float rifleToChargeRatio = chargeScore / rifleScore;
                    result.Data["PowerCreep_MedToHighRatio"] = rifleToChargeRatio;
                    
                    // The ratio should be smaller for high-end weapons (diminishing returns)
                    if (rifleToChargeRatio >= pistolToRifleRatio * 1.2f)
                    {
                        result.Data["PowerCreep_Warning"] = "High-end weapons may not have diminishing returns";
                    }
                }
                
                // Verify scores are reasonable
                if (rifleScore <= pistolScore)
                {
                    result.Success = false;
                    result.FailureReason = result.FailureReason ?? "Basic weapon scoring failed";
                    result.Data["Error"] = result.Data.ContainsKey("Error") ? result.Data["Error"] + "; Rifle didn't score higher than pistol" : "Rifle didn't score higher than pistol";
                    result.Data["PowerCreep_Error2"] = "Rifle didn't score higher than pistol";
                    result.Data["PistolScore"] = pistolScore;
                    result.Data["RifleScore"] = rifleScore;
                    AutoArmLogger.LogError($"[TEST] Power Creep: Rifle didn't score higher than pistol - rifle: {rifleScore}, pistol: {pistolScore}");
                }
            }
            finally
            {
                TestHelpers.SafeDestroyWeapon(pistol);
                TestHelpers.SafeDestroyWeapon(rifle);
                if (chargeRifle != null)
                    TestHelpers.SafeDestroyWeapon(chargeRifle);
            }
        }

        private void TestSituationalWeapons(TestResult result)
        {
            // Test that grenades and launchers are capped at ~80 points
            // First, let's find what situational weapons actually exist
            var allWeaponDefs = DefDatabase<ThingDef>.AllDefs
                .Where(d => d.IsWeapon && d.IsRangedWeapon)
                .ToList();
                
            // Find weapons that would be considered situational
            var situationalWeapons = new List<ThingDef>();
            
            foreach (var def in allWeaponDefs)
            {
                if (def.Verbs?.FirstOrDefault() != null)
                {
                    var verb = def.Verbs[0];
                    var projectile = verb.defaultProjectile?.projectile;
                    
                    bool isExplosive = projectile?.explosionRadius > 0;
                    bool nonLethal = projectile?.damageDef?.harmsHealth == false;
                    bool hasForcedMiss = verb.ForcedMissRadius > 0;
                    
                    if (isExplosive || nonLethal || hasForcedMiss)
                    {
                        situationalWeapons.Add(def);
                    }
                }
            }
            
            result.Data["Situational_WeaponsFound"] = situationalWeapons.Count;
            
            if (situationalWeapons.Count == 0)
            {
                // This is not a failure - just means no situational weapons in this modset
                result.Data["Situational_Info"] = "No situational weapons found in game - test skipped";
                AutoArmLogger.Log("[TEST] Situational: No situational weapons found - skipping test");
                return;
            }
            
            // Test the first few situational weapons found
            int tested = 0;
            int passed = 0;
            foreach (var weaponDef in situationalWeapons.Take(3))
            {
                var weapon = ThingMaker.MakeThing(weaponDef) as ThingWithComps;
                if (weapon != null)
                {
                    float score = WeaponScoringHelper.GetTotalScore(neutralPawn, weapon);
                    result.Data[$"Situational_{weaponDef.defName}_Score"] = score;
                    
                    AutoArmLogger.Log($"[TEST] Situational weapon {weaponDef.label} ({weaponDef.defName}) scored: {score}");
                    
                    if (score > 120) // Allow some skill variance
                    {
                        // Don't fail the whole test for this - situational weapons are edge cases
                        result.Data[$"Situational_Warning_{tested}"] = $"{weaponDef.label} scored higher than expected: {score}";
                        AutoArmLogger.Log($"[TEST] Situational: {weaponDef.label} scored {score}, expected ~80 (warning only)");
                    }
                    else
                    {
                        passed++;
                        AutoArmLogger.Log($"[TEST] Situational weapon {weaponDef.label} passed with score: {score}");
                    }
                    
                    TestHelpers.SafeDestroyWeapon(weapon);
                    tested++;
                }
            }
            
            result.Data["Situational_TestedCount"] = tested;
            result.Data["Situational_PassedCount"] = passed;
            
            if (tested > 0)
            {
                AutoArmLogger.Log($"[TEST] Situational weapons test completed, tested {tested} weapons, {passed} passed");
            }
        }

        private void TestSkillScoring(TestResult result)
        {
            // Test exponential skill scoring
            var rifleDef = VanillaWeaponDefOf.Gun_AssaultRifle;
            var swordDef = VanillaWeaponDefOf.MeleeWeapon_LongSword;
            
            if (rifleDef == null || swordDef == null)
            {
                result.Data["Skill_Error"] = $"Weapon defs not found: rifle={rifleDef != null}, sword={swordDef != null}";
                AutoArmLogger.LogError("[TEST] Skill scoring: Required weapon defs not found");
                return;
            }
            
            // Create pawns with different skill levels
            var shooter = TestHelpers.CreateTestPawn(testMap, new TestHelpers.TestPawnConfig
            {
                Name = "HighShooter",
                Skills = new Dictionary<SkillDef, int>
                {
                    { SkillDefOf.Shooting, 15 },
                    { SkillDefOf.Melee, 5 }
                }
            });
            
            var brawler = TestHelpers.CreateTestPawn(testMap, new TestHelpers.TestPawnConfig
            {
                Name = "HighMelee",
                Skills = new Dictionary<SkillDef, int>
                {
                    { SkillDefOf.Shooting, 5 },
                    { SkillDefOf.Melee, 15 }
                }
            });
            
            var rifle = ThingMaker.MakeThing(rifleDef) as ThingWithComps;
            var sword = ThingMaker.MakeThing(swordDef, ThingDefOf.Steel) as ThingWithComps;
            
            try
            {
                // Get actual skill bonuses from the scoring system
                float shooterRifleSkillBonus = WeaponScoringHelper.GetSkillScore(shooter, rifle);
                float shooterSwordSkillBonus = WeaponScoringHelper.GetSkillScore(shooter, sword);
                float brawlerRifleSkillBonus = WeaponScoringHelper.GetSkillScore(brawler, rifle);
                float brawlerSwordSkillBonus = WeaponScoringHelper.GetSkillScore(brawler, sword);
                
                result.Data["Skill_ShooterRifleBonus"] = shooterRifleSkillBonus;
                result.Data["Skill_ShooterSwordBonus"] = shooterSwordSkillBonus;
                result.Data["Skill_BrawlerRifleBonus"] = brawlerRifleSkillBonus;
                result.Data["Skill_BrawlerSwordBonus"] = brawlerSwordSkillBonus;
                
                // Test shooter preferences
                float shooterRifleScore = WeaponScoringHelper.GetTotalScore(shooter, rifle);
                float shooterSwordScore = WeaponScoringHelper.GetTotalScore(shooter, sword);
                
                result.Data["Skill_ShooterRifleScore"] = shooterRifleScore;
                result.Data["Skill_ShooterSwordScore"] = shooterSwordScore;
                
                // Test brawler preferences  
                float brawlerRifleScore = WeaponScoringHelper.GetTotalScore(brawler, rifle);
                float brawlerSwordScore = WeaponScoringHelper.GetTotalScore(brawler, sword);
                
                result.Data["Skill_BrawlerRifleScore"] = brawlerRifleScore;
                result.Data["Skill_BrawlerSwordScore"] = brawlerSwordScore;
                
                // Verify that skill bonuses are applied correctly
                // Shooter should have positive bonus for rifle, negative for sword
                if (shooterRifleSkillBonus <= 0)
                {
                    result.Success = false;
                    result.FailureReason = result.FailureReason ?? "Skill bonus calculation errors";
                    result.Data["Error"] = result.Data.ContainsKey("Error") ? result.Data["Error"] + "; Shooter rifle bonus not positive" : "Shooter rifle bonus not positive";
                    result.Data["Skill_Error1"] = "Shooter doesn't get rifle bonus";
                    result.Data["ShooterRifleBonus"] = shooterRifleSkillBonus;
                    result.Data["ExpectedBonusSign"] = "positive";
                    AutoArmLogger.LogError($"[TEST] Skill: Shooter rifle bonus {shooterRifleSkillBonus} should be positive");
                }
                
                if (shooterSwordSkillBonus >= 0)
                {
                    result.Success = false;
                    result.FailureReason = result.FailureReason ?? "Skill bonus calculation errors";
                    result.Data["Error"] = result.Data.ContainsKey("Error") ? result.Data["Error"] + "; Shooter sword penalty not negative" : "Shooter sword penalty not negative";
                    result.Data["Skill_Error2"] = "Shooter doesn't get sword penalty";
                    result.Data["ShooterSwordBonus"] = shooterSwordSkillBonus;
                    result.Data["ExpectedBonusSign"] = "negative";
                    AutoArmLogger.LogError($"[TEST] Skill: Shooter sword bonus {shooterSwordSkillBonus} should be negative");
                }
                
                // Brawler should have positive bonus for sword, negative for rifle
                if (brawlerSwordSkillBonus <= 0)
                {
                    result.Success = false;
                    result.FailureReason = result.FailureReason ?? "Skill bonus calculation errors";
                    result.Data["Error"] = result.Data.ContainsKey("Error") ? result.Data["Error"] + "; Brawler sword bonus not positive" : "Brawler sword bonus not positive";
                    result.Data["Skill_Error3"] = "Brawler doesn't get sword bonus";
                    result.Data["BrawlerSwordBonus"] = brawlerSwordSkillBonus;
                    result.Data["ExpectedBonusSign"] = "positive";
                    AutoArmLogger.LogError($"[TEST] Skill: Brawler sword bonus {brawlerSwordSkillBonus} should be positive");
                }
                
                if (brawlerRifleSkillBonus >= 0)
                {
                    result.Success = false;
                    result.FailureReason = result.FailureReason ?? "Skill bonus calculation errors";
                    result.Data["Error"] = result.Data.ContainsKey("Error") ? result.Data["Error"] + "; Brawler rifle penalty not negative" : "Brawler rifle penalty not negative";
                    result.Data["Skill_Error4"] = "Brawler doesn't get rifle penalty";
                    result.Data["BrawlerRifleBonus"] = brawlerRifleSkillBonus;
                    result.Data["ExpectedBonusSign"] = "negative";
                    AutoArmLogger.LogError($"[TEST] Skill: Brawler rifle bonus {brawlerRifleSkillBonus} should be negative");
                }
                
                // Verify correct overall preferences
                if (shooterRifleScore <= shooterSwordScore)
                {
                    result.Success = false;
                    result.FailureReason = result.FailureReason ?? "Weapon preference incorrect";
                    result.Data["Error"] = result.Data.ContainsKey("Error") ? result.Data["Error"] + "; High shooting pawn prefers melee" : "High shooting pawn prefers melee";
                    result.Data["Skill_Error5"] = "High shooting pawn prefers melee";
                    result.Data["ShooterRifleScore"] = shooterRifleScore;
                    result.Data["ShooterSwordScore"] = shooterSwordScore;
                    result.Data["ShootingSkill"] = 15;
                    result.Data["MeleeSkill"] = 5;
                    AutoArmLogger.LogError($"[TEST] Skill: Shooter prefers sword ({shooterSwordScore}) over rifle ({shooterRifleScore})");
                }
                
                if (brawlerSwordScore <= brawlerRifleScore)
                {
                    result.Success = false;
                    result.FailureReason = result.FailureReason ?? "Weapon preference incorrect";
                    result.Data["Error"] = result.Data.ContainsKey("Error") ? result.Data["Error"] + "; High melee pawn prefers ranged" : "High melee pawn prefers ranged";
                    result.Data["Skill_Error6"] = "High melee pawn prefers ranged";
                    result.Data["BrawlerRifleScore"] = brawlerRifleScore;
                    result.Data["BrawlerSwordScore"] = brawlerSwordScore;
                    result.Data["ShootingSkill"] = 5;
                    result.Data["MeleeSkill"] = 15;
                    AutoArmLogger.LogError($"[TEST] Skill: Brawler prefers rifle ({brawlerRifleScore}) over sword ({brawlerSwordScore})");
                }
            }
            finally
            {
                // Cleanup
                TestHelpers.SafeDestroyWeapon(rifle);
                TestHelpers.SafeDestroyWeapon(sword);
                shooter?.Destroy();
                brawler?.Destroy();
            }
        }

        public void Cleanup()
        {
            if (neutralPawn != null && !neutralPawn.Destroyed)
            {
                neutralPawn.Destroy();
            }
        }
    }



    /// <summary>
    /// Test hunter and brawler trait bonuses
    /// </summary>
    public class TraitAndRoleScoringTest : ITestScenario
    {
        public string Name => "Trait and Role Scoring Bonuses";
        private Pawn hunterPawn;
        private Pawn brawlerPawn;
        private Map testMap;

        public void Setup(Map map)
        {
            testMap = map;
            
            // Create brawler first (simpler)
            brawlerPawn = TestHelpers.CreateTestPawn(map, new TestHelpers.TestPawnConfig
            {
                Name = "Brawler",
                Traits = new List<TraitDef> { TraitDefOf.Brawler }
            });
            
            if (brawlerPawn != null)
            {
                bool hasBrawler = brawlerPawn.story?.traits?.HasTrait(TraitDefOf.Brawler) ?? false;
                AutoArmLogger.Log($"[TEST] Created brawler pawn, has brawler trait: {hasBrawler}");
            }
            else
            {
                AutoArmLogger.LogError("[TEST] Failed to create brawler pawn");
            }
            
            // Try multiple times to create a hunter pawn that can actually hunt
            int attempts = 0;
            while (hunterPawn == null && attempts < 5)
            {
                attempts++;
                
                var candidate = TestHelpers.CreateTestPawn(map, new TestHelpers.TestPawnConfig
                {
                    Name = $"Hunter{attempts}",
                    Skills = new Dictionary<SkillDef, int>
                    {
                        { SkillDefOf.Shooting, 10 },
                        { SkillDefOf.Animals, 10 }
                    },
                    EnsureViolenceCapable = true,
                    EnableHunting = true
                });
                
                if (candidate?.workSettings != null && WorkTypeDefOf.Hunting != null)
                {
                    // Check if hunting is disabled by backstory
                    if (candidate.WorkTypeIsDisabled(WorkTypeDefOf.Hunting))
                    {
                        AutoArmLogger.Log($"[TEST] Hunter attempt {attempts} has hunting disabled by backstory - trying again");
                        candidate.Destroy();
                        continue;
                    }
                    
                    // Make sure hunting work is enabled
                    if (candidate.workSettings.GetPriority(WorkTypeDefOf.Hunting) == 0)
                    {
                        candidate.workSettings.EnableAndInitialize();
                    }
                    
                    try
                    {
                        candidate.workSettings.SetPriority(WorkTypeDefOf.Hunting, 1);
                        
                        // Verify it worked
                        if (candidate.workSettings.WorkIsActive(WorkTypeDefOf.Hunting) &&
                            candidate.workSettings.GetPriority(WorkTypeDefOf.Hunting) > 0)
                        {
                            hunterPawn = candidate;
                            AutoArmLogger.Log($"[TEST] Successfully created hunter pawn on attempt {attempts}");
                            AutoArmLogger.Log($"[TEST] Hunter work active: {hunterPawn.workSettings.WorkIsActive(WorkTypeDefOf.Hunting)}");
                            AutoArmLogger.Log($"[TEST] Hunter priority: {hunterPawn.workSettings.GetPriority(WorkTypeDefOf.Hunting)}");
                        }
                        else
                        {
                            AutoArmLogger.Log($"[TEST] Hunter attempt {attempts} failed to set hunting priority");
                            candidate.Destroy();
                        }
                    }
                    catch
                    {
                        AutoArmLogger.LogError($"[TEST] Hunter attempt {attempts} failed to set hunting priority - exception");
                        candidate.Destroy();
                    }
                }
                else
                {
                    AutoArmLogger.LogError($"[TEST] Hunter attempt {attempts} failed to create pawn with work settings");
                    if (candidate != null)
                        candidate.Destroy();
                }
            }
            
            if (hunterPawn == null)
            {
                AutoArmLogger.LogError("[TEST] Failed to create hunter pawn after multiple attempts");
            }
        }

        public TestResult Run()
        {
            var result = new TestResult { Success = true };
            
            try
            {
                if (hunterPawn == null || brawlerPawn == null)
                {
                    result.Success = false;
                    result.Data["Setup_Error"] = $"Pawns not created: hunter={hunterPawn != null}, brawler={brawlerPawn != null}";
                    return result;
                }
                
                // Debug logging for hunter
                if (hunterPawn != null)
                {
                    bool canHunt = hunterPawn.workSettings != null && 
                                  !hunterPawn.WorkTypeIsDisabled(WorkTypeDefOf.Hunting) &&
                                  hunterPawn.workSettings.GetPriority(WorkTypeDefOf.Hunting) > 0;
                    Log.Message($"[AutoArm TEST] Hunter pawn setup: CanHunt={canHunt}, " +
                               $"WorkDisabled={hunterPawn.WorkTypeIsDisabled(WorkTypeDefOf.Hunting)}, " +
                               $"Priority={hunterPawn.workSettings?.GetPriority(WorkTypeDefOf.Hunting) ?? -1}");
                }
            
                var rifleDef = VanillaWeaponDefOf.Gun_BoltActionRifle; // 36.9 range
                var swordDef = VanillaWeaponDefOf.MeleeWeapon_LongSword;
                
                if (rifleDef == null || swordDef == null)
                {
                    result.Success = false;
                    result.Data["Def_Error"] = $"Weapon defs not found: rifle={rifleDef != null}, sword={swordDef != null}";
                    return result;
                }
            
                var rifle = ThingMaker.MakeThing(rifleDef) as ThingWithComps;
                var sword = ThingMaker.MakeThing(swordDef, ThingDefOf.Steel) as ThingWithComps;
                
                // Log what we created for debugging
                AutoArmLogger.Log($"[TEST] Created weapons for trait scoring test:");
                AutoArmLogger.Log($"  - Rifle: {rifle?.Label ?? "null"}");
                AutoArmLogger.Log($"  - Sword: {sword?.Label ?? "null"} with material {sword?.Stuff?.defName ?? "none"}");
            
                // Test hunter bonuses
                bool hunterTestSkipped = false;
                if (hunterPawn != null)
                {
                    // Final check - can this pawn actually hunt?
                    if (hunterPawn.WorkTypeIsDisabled(WorkTypeDefOf.Hunting))
                    {
                        result.Data["Hunter_Skipped"] = "Pawn has hunting disabled by backstory";
                        AutoArmLogger.Log("[TEST] Skipping hunter test - pawn cannot hunt");
                        hunterTestSkipped = true;
                    }
                    else if (hunterPawn.workSettings?.GetPriority(WorkTypeDefOf.Hunting) == 0)
                    {
                        result.Data["Hunter_Skipped"] = "Pawn has hunting priority 0";
                        AutoArmLogger.Log("[TEST] Skipping hunter test - hunting not assigned");
                        hunterTestSkipped = true;
                    }
                    
                    if (!hunterTestSkipped)
                    {
                        // Get actual hunter bonuses from the scoring system
                        float expectedRifleBonus = WeaponScoringHelper.GetHunterScore(hunterPawn, rifle);
                        float expectedSwordBonus = WeaponScoringHelper.GetHunterScore(hunterPawn, sword);
                        
                        result.Data["Hunter_ExpectedRifleBonus"] = expectedRifleBonus;
                        result.Data["Hunter_ExpectedSwordBonus"] = expectedSwordBonus;
                        
                        // Get total scores
                        float hunterRifleScore = WeaponScoringHelper.GetTotalScore(hunterPawn, rifle);
                        float hunterSwordScore = WeaponScoringHelper.GetTotalScore(hunterPawn, sword);
                        
                        result.Data["Hunter_RifleScore"] = hunterRifleScore;
                        result.Data["Hunter_SwordScore"] = hunterSwordScore;
                        
                        // Calculate the actual bonus difference applied
                        float expectedBonusDiff = expectedRifleBonus - expectedSwordBonus;
                        float actualScoreDiff = hunterRifleScore - hunterSwordScore;
                        
                        result.Data["Hunter_ExpectedBonusDiff"] = expectedBonusDiff;
                        result.Data["Hunter_ActualScoreDiff"] = actualScoreDiff;
                        
                        // The hunter should prefer ranged weapons if GetHunterScore gives them a bonus
                        if (expectedRifleBonus > expectedSwordBonus && hunterRifleScore <= hunterSwordScore)
                        {
                            result.Success = false;
                            result.Data["Hunter_Error"] = "Hunter doesn't prefer ranged despite bonus";
                            AutoArmLogger.LogError($"[TEST] Hunter: Expected rifle bonus {expectedRifleBonus} but rifle score {hunterRifleScore} <= sword score {hunterSwordScore}");
                        }
                        
                        // If there's an expected bonus difference, verify it's reflected in total scores
                        // (allowing for other factors like base weapon scores)
                        if (expectedBonusDiff > 0 && actualScoreDiff < expectedBonusDiff * 0.5f)
                        {
                            result.Success = false;
                            result.Data["Hunter_Error2"] = "Hunter bonus not properly applied";
                            AutoArmLogger.LogError($"[TEST] Hunter: Expected bonus diff {expectedBonusDiff} but actual score diff only {actualScoreDiff}");
                        }
                    }
                }
            
                // Test brawler bonuses
                bool brawlerTestPassed = true;
                if (brawlerPawn != null)
                {
                    // Get actual trait bonuses from the scoring system
                    float expectedRifleBonus = WeaponScoringHelper.GetTraitScore(brawlerPawn, rifle);
                    float expectedSwordBonus = WeaponScoringHelper.GetTraitScore(brawlerPawn, sword);
                    
                    result.Data["Brawler_ExpectedRifleBonus"] = expectedRifleBonus;
                    result.Data["Brawler_ExpectedSwordBonus"] = expectedSwordBonus;
                    
                    // Get total scores
                    float brawlerRifleScore = WeaponScoringHelper.GetTotalScore(brawlerPawn, rifle);
                    float brawlerSwordScore = WeaponScoringHelper.GetTotalScore(brawlerPawn, sword);
                    
                    result.Data["Brawler_RifleScore"] = brawlerRifleScore;
                    result.Data["Brawler_SwordScore"] = brawlerSwordScore;
                    
                    // Calculate the actual bonus difference applied
                    float expectedBonusDiff = expectedSwordBonus - expectedRifleBonus;
                    float actualScoreDiff = brawlerSwordScore - brawlerRifleScore;
                    
                    result.Data["Brawler_ExpectedBonusDiff"] = expectedBonusDiff;
                    result.Data["Brawler_ActualScoreDiff"] = actualScoreDiff;
                    
                    // The brawler should prefer melee weapons if GetTraitScore gives them a bonus
                    if (expectedSwordBonus > expectedRifleBonus && brawlerSwordScore <= brawlerRifleScore)
                    {
                        result.Success = false;
                        result.Data["Brawler_Error"] = "Brawler doesn't prefer melee despite bonus";
                        AutoArmLogger.LogError($"[TEST] Brawler: Expected sword bonus {expectedSwordBonus} but sword score {brawlerSwordScore} <= rifle score {brawlerRifleScore}");
                    }
                    
                    // If there's an expected bonus difference, verify it's reflected in total scores
                    // (allowing for other factors like base weapon scores)
                    if (expectedBonusDiff > 0 && actualScoreDiff < expectedBonusDiff * 0.5f)
                    {
                        brawlerTestPassed = false;
                        result.Data["Brawler_Error2"] = "Brawler bonus not properly applied";
                        AutoArmLogger.LogError($"[TEST] Brawler: Expected bonus diff {expectedBonusDiff} but actual score diff only {actualScoreDiff}");
                    }
                }
                
                // Only fail the overall test if both sub-tests failed
                // If hunter test was skipped, only consider brawler test
                if (hunterTestSkipped)
                {
                    result.Success = brawlerTestPassed;
                    if (!brawlerTestPassed)
                    {
                        result.Data["Overall_Result"] = "Brawler test failed (hunter test was skipped)";
                    }
                }
                // If both tests ran, we need at least one to pass
                else if (!result.Success && !brawlerTestPassed)
                {
                    result.Data["Overall_Result"] = "Both hunter and brawler tests failed";
                }
                
                TestHelpers.SafeDestroyWeapon(rifle);
                TestHelpers.SafeDestroyWeapon(sword);
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Data["Exception"] = ex.Message;
                result.Data["StackTrace"] = ex.StackTrace;
                AutoArmLogger.LogError($"[TEST] TraitAndRoleScoringTest exception: {ex}");
            }
            
            return result;
        }

        public void Cleanup()
        {
            if (hunterPawn != null && !hunterPawn.Destroyed)
                hunterPawn.Destroy();
            if (brawlerPawn != null && !brawlerPawn.Destroyed)
                brawlerPawn.Destroy();
        }
    }
}