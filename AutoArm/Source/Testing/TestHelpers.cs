using RimWorld;
using System.Collections.Generic;
using Verse;

namespace AutoArm.Testing
{
    public static class TestHelpers
    {
        public class TestPawnConfig
        {
            public string Name = "TestPawn";
            public List<TraitDef> Traits = new List<TraitDef>();
            public Dictionary<SkillDef, int> Skills = new Dictionary<SkillDef, int>();
            public PawnKindDef Kind = PawnKindDefOf.Colonist;
            public bool MakeNoble = false;
            public bool Conceited = false;
        }

        public static Pawn CreateTestPawn(Map map, TestPawnConfig config = null)
        {
            if (map == null) return null;
            config = config ?? new TestPawnConfig();

            try
            {
                // Simple pawn creation for testing
                var faction = Faction.OfPlayer;
                var pawnKind = config.Kind;

                var request = new PawnGenerationRequest(
                    pawnKind,
                    faction,
                    PawnGenerationContext.NonPlayer,
                    map.Tile
                );

                var pawn = PawnGenerator.GeneratePawn(request);
                GenSpawn.Spawn(pawn, map.Center, map);
                return pawn;
            }
            catch
            {
                return null;
            }
        }

        public static ThingWithComps CreateWeapon(Map map, ThingDef weaponDef, IntVec3 position, QualityCategory quality = QualityCategory.Normal)
        {
            if (map == null || weaponDef == null || !position.IsValid)
                return null;

            try
            {
                var weapon = ThingMaker.MakeThing(weaponDef) as ThingWithComps;
                if (weapon == null) return null;

                var compQuality = weapon.TryGetComp<CompQuality>();
                if (compQuality != null)
                {
                    compQuality.SetQuality(quality, ArtGenerationContext.Colony);
                }

                GenSpawn.Spawn(weapon, position, map);
                return weapon;
            }
            catch
            {
                return null;
            }
        }

        public static Pawn CreateTestPawnWithAge(Map map, int age, string name = "TestPawn")
        {
            var pawn = CreateTestPawn(map, new TestPawnConfig { Name = name });
            // Age setting would require reflection or mod extension
            return pawn;
        }

        // Helper to get weapon defs safely
        public static ThingDef GetWeaponDef(string defName)
        {
            return DefDatabase<ThingDef>.GetNamedSilentFail(defName);
        }
    }
}