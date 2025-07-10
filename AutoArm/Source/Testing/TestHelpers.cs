using RimWorld;
using System.Collections.Generic;
using System.Reflection;
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
            public int? BiologicalAge = null; // Add age option
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

                // If we need a specific age, try to use a child pawn kind
                if (config.BiologicalAge.HasValue && config.BiologicalAge.Value < 18 && ModsConfig.BiotechActive)
                {
                    // Try to find a child pawn kind
                    var childKind = DefDatabase<PawnKindDef>.GetNamedSilentFail("Colonist_Child");
                    if (childKind != null)
                        pawnKind = childKind;
                }

                var request = new PawnGenerationRequest(
                    pawnKind,
                    faction,
                    PawnGenerationContext.NonPlayer,
                    map.Tile,
                    forceGenerateNewPawn: true
                );

                // Try to set fixed biological age in the request if possible
                if (config.BiologicalAge.HasValue)
                {
                    request.FixedBiologicalAge = config.BiologicalAge.Value;
                    request.FixedChronologicalAge = config.BiologicalAge.Value;
                }

                var pawn = PawnGenerator.GeneratePawn(request);

                // If the age still isn't right, try a more direct approach
                if (config.BiologicalAge.HasValue && pawn.ageTracker != null)
                {
                    var ageTrackerType = pawn.ageTracker.GetType();

                    // Try multiple field names that might store age
                    string[] possibleFieldNames = { "birthAbsTicksInt", "birthAbsTicks", "ageBiologicalTicksInt" };
                    FieldInfo birthTicksField = null;

                    foreach (var fieldName in possibleFieldNames)
                    {
                        birthTicksField = ageTrackerType.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
                        if (birthTicksField != null)
                            break;
                    }

                    if (birthTicksField != null)
                    {
                        // Calculate birth ticks based on desired age
                        long currentTicks = Find.TickManager.TicksAbs;
                        long ticksPerYear = 3600000; // 60 * 60 * 1000
                        long birthTicks = currentTicks - (config.BiologicalAge.Value * ticksPerYear);
                        birthTicksField.SetValue(pawn.ageTracker, birthTicks);

                        // The age tracker should update automatically, but we can try to force an update
                        // by accessing a property that triggers recalculation
                        var currentAge = pawn.ageTracker.AgeBiologicalYears;
                    }
                }

                // Apply traits if specified
                if (config.Traits != null && pawn.story?.traits != null)
                {
                    foreach (var traitDef in config.Traits)
                    {
                        if (traitDef != null && !pawn.story.traits.HasTrait(traitDef))
                        {
                            pawn.story.traits.GainTrait(new Trait(traitDef));
                        }
                    }
                }

                // Apply skills if specified
                if (config.Skills != null && pawn.skills?.skills != null)
                {
                    foreach (var kvp in config.Skills)
                    {
                        var skill = pawn.skills.GetSkill(kvp.Key);
                        if (skill != null)
                        {
                            skill.Level = kvp.Value;
                        }
                    }
                }

                GenSpawn.Spawn(pawn, map.Center, map);
                return pawn;
            }
            catch (System.Exception e)
            {
                Log.Warning($"[AutoArm] Failed to create test pawn: {e.Message}");
                return null;
            }
        }

        // Fixed CreateWeapon method in TestHelpers.cs

        public static ThingWithComps CreateWeapon(Map map, ThingDef weaponDef, IntVec3 position, QualityCategory quality = QualityCategory.Normal)
        {
            if (map == null || weaponDef == null || !position.IsValid)
                return null;

            try
            {
                ThingWithComps weapon;

                // Check if weapon needs stuff (material)
                if (weaponDef.MadeFromStuff)
                {
                    // Use steel as default material
                    ThingDef stuffDef = ThingDefOf.Steel;

                    // For melee weapons, you might want to use different materials
                    if (weaponDef.IsMeleeWeapon)
                    {
                        // Try to use a better material if available
                        var plasteel = DefDatabase<ThingDef>.GetNamedSilentFail("Plasteel");
                        if (plasteel != null && weaponDef.stuffCategories?.Any(cat => plasteel.stuffProps?.categories?.Contains(cat) ?? false) == true)
                        {
                            stuffDef = plasteel;
                        }
                    }

                    weapon = ThingMaker.MakeThing(weaponDef, stuffDef) as ThingWithComps;
                }
                else
                {
                    weapon = ThingMaker.MakeThing(weaponDef) as ThingWithComps;
                }

                if (weapon == null) return null;

                var compQuality = weapon.TryGetComp<CompQuality>();
                if (compQuality != null)
                {
                    compQuality.SetQuality(quality, ArtGenerationContext.Colony);
                }

                GenSpawn.Spawn(weapon, position, map);
                return weapon;
            }
            catch (System.Exception e)
            {
                Log.Warning($"[AutoArm] Failed to create weapon {weaponDef?.defName}: {e.Message}");
                return null;
            }
        }

        public static Pawn CreateTestPawnWithAge(Map map, int age, string name = "TestPawn")
        {
            var config = new TestPawnConfig
            {
                Name = name,
                BiologicalAge = age
            };
            return CreateTestPawn(map, config);
        }

        // Helper to get weapon defs safely
        public static ThingDef GetWeaponDef(string defName)
        {
            return DefDatabase<ThingDef>.GetNamedSilentFail(defName);
        }
    }
}