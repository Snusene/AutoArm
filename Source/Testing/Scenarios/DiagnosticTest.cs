// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: Diagnostic test for verifying basic mod functionality
// Checks weapon def availability and scoring system

using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using AutoArm.Weapons;
using AutoArm.Definitions;

namespace AutoArm.Testing.Scenarios
{
    /// <summary>
    /// Diagnostic test to check basic functionality
    /// </summary>
    public class DiagnosticTest : ITestScenario
    {
        public string Name => "Diagnostic Test";
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
                // Ensure mod is enabled for diagnostic
                TestModEnabler.EnsureModEnabled();
                
                // Test 0: Check mod status
                result.Data["Mod_Enabled"] = AutoArmMod.settings?.modEnabled ?? false;
                result.Data["Settings_Instance"] = AutoArmMod.settings?.GetHashCode() ?? -1;
                result.Data["TestRunner_Active"] = TestRunner.IsRunningTests;
                
                if (AutoArmMod.settings?.modEnabled != true)
                {
                    result.Success = false;
                    result.FailureReason = "Mod is disabled during test execution";
                    result.Data["CRITICAL_ERROR"] = "Mod must be enabled for tests to work";
                }
                // Test 1: Check weapon defs are available
                result.Data["Gun_Autopistol_Available"] = VanillaWeaponDefOf.Gun_Autopistol != null;
                result.Data["Gun_AssaultRifle_Available"] = VanillaWeaponDefOf.Gun_AssaultRifle != null;
                result.Data["MeleeWeapon_Knife_Available"] = VanillaWeaponDefOf.MeleeWeapon_Knife != null;
                result.Data["MeleeWeapon_LongSword_Available"] = VanillaWeaponDefOf.MeleeWeapon_LongSword != null;
                
                // Test 2: Try to create weapons
                var pistolDef = VanillaWeaponDefOf.Gun_Autopistol;
                if (pistolDef != null)
                {
                    try
                    {
                        var pistol = ThingMaker.MakeThing(pistolDef) as ThingWithComps;
                        result.Data["Autopistol_Created"] = pistol != null;
                        if (pistol != null)
                        {
                            result.Data["Autopistol_Label"] = pistol.Label;
                            
                            // Test scoring
                            var testPawn = TestHelpers.CreateTestPawn(testMap, new TestHelpers.TestPawnConfig
                            {
                                Name = "TestScorer",
                                Skills = new Dictionary<SkillDef, int>
                                {
                                    { SkillDefOf.Shooting, 10 },
                                    { SkillDefOf.Melee, 10 }
                                }
                            });
                            
                            if (testPawn != null)
                            {
                                float score = WeaponScoringHelper.GetTotalScore(testPawn, pistol);
                                result.Data["Autopistol_Score"] = score;
                                testPawn.Destroy();
                            }
                            
                            pistol.Destroy();
                        }
                    }
                    catch (Exception ex)
                    {
                        result.Data["Weapon_Creation_Error"] = ex.Message;
                        result.Success = false;
                    }
                }
                else
                {
                    result.Data["Error"] = "Gun_Autopistol def is null";
                    result.Success = false;
                }
                
                // Test 3: List available weapon defs
                var weaponCount = DefDatabase<ThingDef>.AllDefs.Count(d => d.IsWeapon);
                result.Data["Total_Weapon_Defs"] = weaponCount;
                
                // Test 4: Check map validity
                result.Data["Map_Valid"] = testMap != null;
                if (testMap != null)
                {
                    result.Data["Map_Size"] = $"{testMap.Size.x}x{testMap.Size.z}";
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Data["Exception"] = ex.Message;
                result.Data["StackTrace"] = ex.StackTrace;
            }
            
            return result;
        }

        public void Cleanup()
        {
            // Nothing to cleanup
        }
    }
}
