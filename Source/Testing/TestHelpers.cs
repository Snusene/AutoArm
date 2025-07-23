using RimWorld;
using System.Collections.Generic;
using System.Linq;
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
            public int? BiologicalAge = null;
        }

        public static Pawn CreateTestPawn(Map map, TestPawnConfig config = null)
        {
            if (map == null) return null;
            config = config ?? new TestPawnConfig();

            try
            {
                var faction = Faction.OfPlayer;
                var pawnKind = config.Kind;

                if (config.BiologicalAge.HasValue && config.BiologicalAge.Value < 18 && ModsConfig.BiotechActive)
                {
                    var childKind = DefDatabase<PawnKindDef>.GetNamedSilentFail("Colonist_Child");
                    if (childKind != null)
                        pawnKind = childKind;
                }

                var request = new PawnGenerationRequest(
                    pawnKind,
                    faction,
                    PawnGenerationContext.NonPlayer,
                    -1,  // Use -1 for compatibility with both RimWorld 1.5 and 1.6
                    forceGenerateNewPawn: true
                );

                if (config.BiologicalAge.HasValue)
                {
                    request.FixedBiologicalAge = config.BiologicalAge.Value;
                    request.FixedChronologicalAge = config.BiologicalAge.Value;
                }

                var pawn = PawnGenerator.GeneratePawn(request);
                
                // Override backstories to ensure violence capability
                if (pawn.story != null && pawn.WorkTagIsDisabled(WorkTags.Violent))
                {
                    // Clear traits that might disable violence
                    pawn.story.traits.allTraits.RemoveAll(t => 
                        t.def.disabledWorkTags.HasFlag(WorkTags.Violent));
                        
                    // Recalculate disabled work types
                    pawn.Notify_DisabledWorkTypesChanged();
                    
                    // Double-check
                    if (pawn.WorkTagIsDisabled(WorkTags.Violent))
                    {
                        Log.Warning($"[TEST] Pawn {pawn.Name} still incapable of violence after trait removal - backstory issue");
                    }
                }

                if (config.BiologicalAge.HasValue && pawn.ageTracker != null)
                {
                    var ageTrackerType = pawn.ageTracker.GetType();

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
                        long currentTicks = Find.TickManager.TicksAbs;
                        long ticksPerYear = 3600000; 
                        long birthTicks = currentTicks - (config.BiologicalAge.Value * ticksPerYear);
                        birthTicksField.SetValue(pawn.ageTracker, birthTicks);

                        var currentAge = pawn.ageTracker.AgeBiologicalYears;
                    }
                }

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

                // Add nobility support
                if (config.MakeNoble && ModsConfig.RoyaltyActive && pawn.royalty != null)
                {
                    // Find Empire faction
                    var empireFaction = Find.FactionManager.FirstFactionOfDef(FactionDefOf.Empire);
                    if (empireFaction != null)
                    {
                        // Grant a noble title
                        RoyalTitleDef titleDef = null;
                        
                        if (config.Conceited)
                        {
                            // Find a conceited title
                            // In RimWorld, RoyalTitleDef has a conceited field
                            var royalTitleDefType = typeof(RoyalTitleDef);
                            var conceitedField = royalTitleDefType.GetField("conceited", BindingFlags.Public | BindingFlags.Instance);
                            
                            if (conceitedField != null)
                            {
                                titleDef = DefDatabase<RoyalTitleDef>.AllDefs
                                    .Where(t => t.seniority > 100)
                                    .FirstOrDefault(t => 
                                    {
                                        var value = conceitedField.GetValue(t);
                                        return value is bool && (bool)value;
                                    });
                            }
                        }
                        
                        if (titleDef == null)
                        {
                            // Fall back to any high-ranking title
                            titleDef = DefDatabase<RoyalTitleDef>.GetNamedSilentFail("Count");
                            if (titleDef == null)
                                titleDef = DefDatabase<RoyalTitleDef>.AllDefs.FirstOrDefault(t => t.seniority > 100);
                        }
                        
                        if (titleDef != null)
                        {
                            pawn.royalty.SetTitle(empireFaction, titleDef, false);
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


        public static ThingWithComps CreateWeapon(Map map, ThingDef weaponDef, IntVec3 position, QualityCategory quality = QualityCategory.Normal)
        {
            if (map == null || weaponDef == null || !position.IsValid)
                return null;

            try
            {
                ThingWithComps weapon;

                if (weaponDef.MadeFromStuff)
                {
                    ThingDef stuffDef = ThingDefOf.Steel;

                    if (weaponDef.IsMeleeWeapon)
                    {
                        var plasteel = DefDatabase<ThingDef>.GetNamedSilentFail("Plasteel");
                        if (plasteel != null && weaponDef.stuffCategories != null && plasteel.stuffProps?.categories != null)
                        {
                            // Check if any of the weapon's stuff categories match the plasteel's categories
                            foreach (var cat in weaponDef.stuffCategories)
                            {
                                if (plasteel.stuffProps.categories.Contains(cat))
                                {
                                    stuffDef = plasteel;
                                    break;
                                }
                            }
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

        public static ThingDef GetWeaponDef(string defName)
        {
            return DefDatabase<ThingDef>.GetNamedSilentFail(defName);
        }
    }
}