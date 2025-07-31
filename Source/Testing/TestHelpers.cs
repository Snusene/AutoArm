using RimWorld;
using System;
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
            public bool EnsureViolenceCapable = true; // Default to creating violence-capable pawns for weapon tests
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

                // Filter backstories to exclude those that disable violence (if requested)
                BackstoryDef validChildhood = null;
                BackstoryDef validAdulthood = null;

                if (config.EnsureViolenceCapable)
                {
                    // Find backstories that don't disable violence
                    // Note: BackstoryDef property names vary by RimWorld version
                    var allBackstories = DefDatabase<BackstoryDef>.AllDefs.Where(b =>
                    {
                        if (b == null) return false;

                        // Try different property names for disabled work tags
                        var backstoryType = b.GetType();

                        // Try "workDisables" (common in newer versions)
                        var workDisablesField = backstoryType.GetField("workDisables", BindingFlags.Public | BindingFlags.Instance);
                        if (workDisablesField != null)
                        {
                            var workTags = workDisablesField.GetValue(b) as WorkTags?;
                            if (workTags.HasValue && workTags.Value.HasFlag(WorkTags.Violent))
                                return false;
                        }

                        // Try "disabledWorkTags" (alternative naming)
                        var disabledWorkTagsField = backstoryType.GetField("disabledWorkTags", BindingFlags.Public | BindingFlags.Instance);
                        if (disabledWorkTagsField != null)
                        {
                            var workTags = disabledWorkTagsField.GetValue(b) as WorkTags?;
                            if (workTags.HasValue && workTags.Value.HasFlag(WorkTags.Violent))
                                return false;
                        }

                        // Try "DisabledWorkTags" property
                        var disabledWorkTagsProp = backstoryType.GetProperty("DisabledWorkTags", BindingFlags.Public | BindingFlags.Instance);
                        if (disabledWorkTagsProp != null)
                        {
                            var workTags = disabledWorkTagsProp.GetValue(b) as WorkTags?;
                            if (workTags.HasValue && workTags.Value.HasFlag(WorkTags.Violent))
                                return false;
                        }

                        return true; // If we can't find the field, assume it doesn't disable violence
                    });

                    var childBackstories = allBackstories.Where(b => b.slot == BackstorySlot.Childhood).ToList();
                    var adultBackstories = allBackstories.Where(b => b.slot == BackstorySlot.Adulthood).ToList();

                    if (childBackstories.Count > 0)
                        validChildhood = childBackstories.RandomElement();
                    if (adultBackstories.Count > 0 && (config.BiologicalAge == null || config.BiologicalAge >= 20))
                        validAdulthood = adultBackstories.RandomElement();
                }

                var request = new PawnGenerationRequest(
                    pawnKind,
                    faction,
                    PawnGenerationContext.NonPlayer,
                    -1,  // Use -1 for compatibility with both RimWorld 1.5 and 1.6
                    forceGenerateNewPawn: true
                );

                // Try to set specific backstories (property names vary by version)
                var requestType = request.GetType();

                // Try different property names for forcing backstories
                var childBackstoryProps = new[] { "ForcedChildBackstory", "forcedChildBackstory", "FixedChildBackstory", "fixedChildBackstory" };
                var adultBackstoryProps = new[] { "ForcedAdultBackstory", "forcedAdultBackstory", "FixedAdultBackstory", "fixedAdultBackstory" };

                foreach (var propName in childBackstoryProps)
                {
                    var prop = requestType.GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
                    if (prop != null && validChildhood != null)
                    {
                        try { prop.SetValue(request, validChildhood); break; }
                        catch { }
                    }
                }

                foreach (var propName in adultBackstoryProps)
                {
                    var prop = requestType.GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
                    if (prop != null && validAdulthood != null)
                    {
                        try { prop.SetValue(request, validAdulthood); break; }
                        catch { }
                    }
                }

                if (config.BiologicalAge.HasValue)
                {
                    request.FixedBiologicalAge = config.BiologicalAge.Value;
                    request.FixedChronologicalAge = config.BiologicalAge.Value;
                }

                var pawn = PawnGenerator.GeneratePawn(request);

                if (config.EnsureViolenceCapable && pawn.story != null && !pawn.WorkTagIsDisabled(WorkTags.Violent) && validChildhood != null)
                {
                    AutoArmDebug.Log($"[TEST] Successfully created violence-capable pawn {pawn.Name} using filtered backstories");
                }

                // Override backstories to ensure violence capability (fallback if forced backstories didn't work)
                if (config.EnsureViolenceCapable && pawn.story != null && pawn.WorkTagIsDisabled(WorkTags.Violent))
                {
                    // Clear traits that might disable violence
                    pawn.story.traits.allTraits.RemoveAll(t =>
                        t.def.disabledWorkTags.HasFlag(WorkTags.Violent));

                    // Recalculate disabled work types
                    pawn.Notify_DisabledWorkTypesChanged();

                    // Final check - if still incapable, try to find a backstory replacement
                    if (pawn.WorkTagIsDisabled(WorkTags.Violent))
                    {
                        // Force override the backstories through reflection if necessary
                        var storyType = pawn.story.GetType();
                        var childhoodField = storyType.GetField("childhood", BindingFlags.NonPublic | BindingFlags.Instance) ??
                                           storyType.GetField("Childhood", BindingFlags.NonPublic | BindingFlags.Instance) ??
                                           storyType.GetField("_childhood", BindingFlags.NonPublic | BindingFlags.Instance);
                        var adulthoodField = storyType.GetField("adulthood", BindingFlags.NonPublic | BindingFlags.Instance) ??
                                           storyType.GetField("Adulthood", BindingFlags.NonPublic | BindingFlags.Instance) ??
                                           storyType.GetField("_adulthood", BindingFlags.NonPublic | BindingFlags.Instance);

                        if (childhoodField != null && validChildhood != null)
                        {
                            childhoodField.SetValue(pawn.story, validChildhood);
                        }
                        if (adulthoodField != null && validAdulthood != null && pawn.ageTracker.AgeBiologicalYears >= 20)
                        {
                            adulthoodField.SetValue(pawn.story, validAdulthood);
                        }

                        // Recalculate after backstory change
                        pawn.Notify_DisabledWorkTypesChanged();

                        if (pawn.WorkTagIsDisabled(WorkTags.Violent))
                        {
                            Log.Warning($"[TEST] Failed to make pawn {pawn.Name} capable of violence");
                        }
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
                    // Find appropriate stuff
                    ThingDef stuffDef = null;

                    // For tests, always use Steel for consistency
                    // This prevents material differences from affecting test results
                    stuffDef = ThingDefOf.Steel;

                    // Verify the weapon can use this material
                    if (!CanUseStuff(weaponDef, stuffDef))
                    {
                        // For knives and other weapons that might not accept steel, try other materials
                        if (weaponDef.defName.Contains("Knife") || weaponDef.defName.Contains("Shiv"))
                        {
                            // Try common materials for knives
                            var materials = new[] { ThingDefOf.Steel, ThingDefOf.Plasteel, ThingDefOf.WoodLog };
                            foreach (var mat in materials.Where(m => m != null))
                            {
                                if (CanUseStuff(weaponDef, mat))
                                {
                                    stuffDef = mat;
                                    break;
                                }
                            }
                        }
                        
                        // If still no valid material, find first valid material
                        if (stuffDef == null || !CanUseStuff(weaponDef, stuffDef))
                        {
                            if (weaponDef.stuffCategories != null)
                            {
                                foreach (var category in weaponDef.stuffCategories)
                                {
                                    var validStuff = DefDatabase<ThingDef>.AllDefs
                                        .Where(td => td.stuffProps != null &&
                                                    td.stuffProps.categories != null &&
                                                    td.stuffProps.categories.Contains(category))
                                        .FirstOrDefault();
                                    if (validStuff != null)
                                    {
                                        stuffDef = validStuff;
                                        break;
                                    }
                                }
                            }
                        }
                    }

                    // Final safety check - if still null, use wood as last resort
                    if (stuffDef == null)
                    {
                        stuffDef = ThingDefOf.WoodLog;
                        // If wood doesn't work either, just use the first available material
                        if (!CanUseStuff(weaponDef, stuffDef))
                        {
                            stuffDef = DefDatabase<ThingDef>.AllDefs
                                .FirstOrDefault(td => td.stuffProps != null && CanUseStuff(weaponDef, td));
                        }
                    }
                    
                    if (stuffDef == null)
                    {
                        AutoArmDebug.LogError($"[TEST] No valid material found for {weaponDef.defName}");
                        return null;
                    }

                    weapon = ThingMaker.MakeThing(weaponDef, stuffDef) as ThingWithComps;

                    if (weapon != null)
                    {
                        AutoArmDebug.Log($"[TEST] Created {weaponDef.defName} with material {stuffDef.defName}");
                    }
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

        // Helper method to check if a weapon can use a specific material
        private static bool CanUseStuff(ThingDef weaponDef, ThingDef stuffDef)
        {
            if (weaponDef?.stuffCategories == null || stuffDef?.stuffProps?.categories == null)
                return false;

            foreach (var weaponCategory in weaponDef.stuffCategories)
            {
                if (stuffDef.stuffProps.categories.Contains(weaponCategory))
                    return true;
            }

            return false;
        }

        public static ThingDef GetWeaponDef(string defName)
        {
            return DefDatabase<ThingDef>.GetNamedSilentFail(defName);
        }

        /// <summary>
        /// Safely destroy a weapon, handling container removal
        /// </summary>
        public static void SafeDestroyWeapon(ThingWithComps weapon)
        {
            if (weapon == null || weapon.Destroyed)
                return;

            try
            {
                // Track that we're destroying this to prevent double-destroy
                var weaponId = weapon.thingIDNumber;
                
                // Remove from equipment if equipped
                if (weapon.ParentHolder is Pawn_EquipmentTracker equipment)
                {
                    var pawn = equipment.pawn;
                    if (pawn?.equipment?.Primary == weapon)
                    {
                        pawn.equipment.Remove(weapon);
                    }
                }
                
                // Remove from inventory if in inventory
                else if (weapon.ParentHolder is Pawn_InventoryTracker inventory)
                {
                    inventory.innerContainer.Remove(weapon);
                }
                
                // Remove from any other container
                else if (weapon.holdingOwner != null)
                {
                    weapon.holdingOwner.Remove(weapon);
                }

                // Despawn if spawned
                if (weapon.Spawned)
                {
                    weapon.DeSpawn();
                }

                // Now destroy - but check again in case it was destroyed during cleanup
                if (!weapon.Destroyed)
                {
                    weapon.Destroy();
                }
            }
            catch (Exception e)
            {
                // Don't log errors for already destroyed weapons
                if (!e.Message.Contains("already-destroyed"))
                {
                    AutoArmDebug.LogError($"[TEST] Failed to safely destroy weapon {weapon?.Label}: {e.Message}");
                }
            }
        }
    }
}