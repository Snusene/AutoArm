// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: Debug test for understanding weapon scoring calculations
// Uses extreme skill differences to validate scoring formulas

using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using AutoArm.Logging; using AutoArm.Weapons;
using AutoArm.Definitions;

namespace AutoArm.Testing.Scenarios
{
    /// <summary>
    /// Debug test to understand weapon scoring issues
    /// </summary>
    public class WeaponScoringDebugTest : ITestScenario
    {
        public string Name => "Weapon Scoring Debug";
        private Map testMap;

        public void Setup(Map map)
        {
            testMap = map;
        }

        public TestResult Run()
        {
            var result = new TestResult { Success = true };
            
            try
            {
                // Create test pawns with extreme skill differences
                var extremeMeleePawn = TestHelpers.CreateTestPawn(testMap, new TestHelpers.TestPawnConfig
                {
                    Name = "ExtremeMelee",
                    Skills = new Dictionary<SkillDef, int>
                    {
                        { SkillDefOf.Shooting, 0 },
                        { SkillDefOf.Melee, 20 }
                    }
                });
                
                var balancedPawn = TestHelpers.CreateTestPawn(testMap, new TestHelpers.TestPawnConfig
                {
                    Name = "Balanced",
                    Skills = new Dictionary<SkillDef, int>
                    {
                        { SkillDefOf.Shooting, 10 },
                        { SkillDefOf.Melee, 10 }
                    }
                });
                
                // Test specific weapons
                var rifleDef = VanillaWeaponDefOf.Gun_AssaultRifle;
                var swordDef = VanillaWeaponDefOf.MeleeWeapon_LongSword;
                
                if (rifleDef == null || swordDef == null)
                {
                    result.Data["Error"] = "Weapon defs not found";
                    return result;
                }
                
                var rifle = ThingMaker.MakeThing(rifleDef) as ThingWithComps;
                var sword = ThingMaker.MakeThing(swordDef, ThingDefOf.Steel) as ThingWithComps;
                
                // Calculate scores for extreme melee pawn
                float extremeMeleeRifleScore = WeaponScoringHelper.GetTotalScore(extremeMeleePawn, rifle);
                float extremeMeleeSwordScore = WeaponScoringHelper.GetTotalScore(extremeMeleePawn, sword);
                
                result.Data["ExtremeMelee_RifleScore"] = extremeMeleeRifleScore;
                result.Data["ExtremeMelee_SwordScore"] = extremeMeleeSwordScore;
                result.Data["ExtremeMelee_Preference"] = extremeMeleeSwordScore > extremeMeleeRifleScore ? "Sword" : "Rifle";
                
                // Calculate detailed breakdown
                float rifleBase = WeaponScoringHelper.GetWeaponPropertyScore(extremeMeleePawn, rifle);
                float swordBase = WeaponScoringHelper.GetWeaponPropertyScore(extremeMeleePawn, sword);
                
                result.Data["Rifle_BaseScore"] = rifleBase;
                result.Data["Sword_BaseScore"] = swordBase;
                
                // Calculate skill contribution (20 level difference)
                float skillDiff = 20f;
                float expectedBonus = 30f * (float)Math.Pow(1.15f, skillDiff - 1); // Base 30
                result.Data["Expected_SkillBonus"] = expectedBonus;
                
                // For balanced pawn
                float balancedRifleScore = WeaponScoringHelper.GetTotalScore(balancedPawn, rifle);
                float balancedSwordScore = WeaponScoringHelper.GetTotalScore(balancedPawn, sword);
                
                result.Data["Balanced_RifleScore"] = balancedRifleScore;
                result.Data["Balanced_SwordScore"] = balancedSwordScore;
                
                // Log everything
                AutoArmLogger.Log("[TEST] === Weapon Scoring Debug ===");
                AutoArmLogger.Log($"[TEST] Assault Rifle base: {rifleBase:F1}");
                AutoArmLogger.Log($"[TEST] Longsword base: {swordBase:F1}");
                AutoArmLogger.Log($"[TEST] Expected skill bonus (20 levels): {expectedBonus:F1}");
                AutoArmLogger.Log($"[TEST] Extreme melee pawn - Rifle: {extremeMeleeRifleScore:F1}, Sword: {extremeMeleeSwordScore:F1}");
                AutoArmLogger.Log($"[TEST] Balanced pawn - Rifle: {balancedRifleScore:F1}, Sword: {balancedSwordScore:F1}");
                
                // Cleanup
                rifle?.Destroy();
                sword?.Destroy();
                extremeMeleePawn?.Destroy();
                balancedPawn?.Destroy();
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Data["Exception"] = ex.Message;
                AutoArmLogger.LogError($"[TEST] WeaponScoringDebugTest exception: {ex}");
            }
            
            return result;
        }

        public void Cleanup()
        {
            // Cleanup handled in Run method
        }
    }
}
