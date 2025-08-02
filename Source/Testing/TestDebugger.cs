// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: Test debugging utilities
// Helper methods for debugging test failures

using RimWorld;
using System.Linq;
using Verse;

namespace AutoArm.Testing
{
    public static class TestDebugger
    {
        public static void DebugSkillBasedTest(Map map)
        {
            if (map == null) return;
            
            // Create test weapons
            var rifleDef = DefDatabase<ThingDef>.GetNamed("Gun_AssaultRifle", false);
            var swordDef = DefDatabase<ThingDef>.GetNamed("MeleeWeapon_LongSword", false);
            
            if (rifleDef == null || swordDef == null)
            {
                AutoArmLogger.LogError("[TEST DEBUG] Weapon defs not found!");
                return;
            }
            
            // Create weapons
            var rifle = ThingMaker.MakeThing(rifleDef, ThingDefOf.Steel) as ThingWithComps;
            var sword = ThingMaker.MakeThing(swordDef, ThingDefOf.Steel) as ThingWithComps;
            
            // Create test pawn with high melee skill
            var pawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
            pawn.skills.GetSkill(SkillDefOf.Shooting).Level = 3;
            pawn.skills.GetSkill(SkillDefOf.Melee).Level = 15;
            
            // Calculate scores
            float rifleScore = WeaponScoringHelper.GetTotalScore(pawn, rifle);
            float swordScore = WeaponScoringHelper.GetTotalScore(pawn, sword);
            
            AutoArmLogger.Log($"[TEST DEBUG] High-melee pawn weapon scores:");
            AutoArmLogger.Log($"  Assault Rifle: {rifleScore:F1}");
            AutoArmLogger.Log($"  Longsword: {swordScore:F1}");
            AutoArmLogger.Log($"  Should pick: {(swordScore > rifleScore ? "Longsword" : "Assault Rifle")}");
            
            // Calculate skill components separately
            float skillDiff = 12f;
            float skillBonus = 30f * UnityEngine.Mathf.Pow(1.15f, 11f);
            
            AutoArmLogger.Log($"[TEST DEBUG] Skill calculation:");
            AutoArmLogger.Log($"  Skill difference: {skillDiff}");
            AutoArmLogger.Log($"  Base skill bonus: {skillBonus:F1}");
            AutoArmLogger.Log($"  Ranged penalty: {-skillBonus * 0.5f:F1}");
            AutoArmLogger.Log($"  Melee bonus: {skillBonus:F1}");
            
            // Cleanup
            rifle.Destroy();
            sword.Destroy();
            pawn.Destroy();
        }
        
        public static void DebugWeaponCacheTest(Map map)
        {
            if (map == null) return;
            
            AutoArmLogger.Log($"[TEST DEBUG] Map size: {map.Size.x} x {map.Size.z}");
            
            int validPositions = 0;
            int totalPositions = 0;
            
            // Check how many valid positions exist for weapon placement
            for (int x = 10; x < map.Size.x - 10; x += 20)
            {
                for (int z = 10; z < map.Size.z - 10; z += 20)
                {
                    totalPositions++;
                    var pos = new IntVec3(x, 0, z);
                    if (pos.InBounds(map) && pos.Standable(map))
                    {
                        validPositions++;
                    }
                }
            }
            
            AutoArmLogger.Log($"[TEST DEBUG] Valid positions for weapons: {validPositions}/{totalPositions}");
            
            // Test weapon creation
            var weaponDef = DefDatabase<ThingDef>.GetNamed("Gun_Autopistol", false);
            if (weaponDef != null)
            {
                var testPos = map.Center;
                if (testPos.Standable(map))
                {
                    var weapon = ThingMaker.MakeThing(weaponDef, ThingDefOf.Steel) as ThingWithComps;
                    GenSpawn.Spawn(weapon, testPos, map);
                    
                    AutoArmLogger.Log($"[TEST DEBUG] Test weapon spawned: {weapon.Spawned}");
                    AutoArmLogger.Log($"[TEST DEBUG] Weapon position: {weapon.Position}");
                    
                    // Test cache
                    ImprovedWeaponCacheManager.AddWeaponToCache(weapon);
                    var cached = ImprovedWeaponCacheManager.GetWeaponsNear(map, testPos, 10f).ToList();
                    
                    AutoArmLogger.Log($"[TEST DEBUG] Weapons in cache near center: {cached.Count}");
                    
                    weapon.Destroy();
                }
                else
                {
                    AutoArmLogger.LogError($"[TEST DEBUG] Map center is not standable!");
                }
            }
        }
    }
}