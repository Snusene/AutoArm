// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: Test helper utilities
// Provides helper methods for creating test pawns, weapons, and scenarios

using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;
using AutoArm.Testing.Helpers;

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
            public bool EnableHunting = false; // Default to false to prevent the +500 hunter bonus affecting tests
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
                    forceGenerateNewPawn: true,
                    allowDead: false,
                    allowDowned: false
                );
                
                // If we want a pacifist, don't force backstories
                if (!config.EnsureViolenceCapable)
                {
                    validChildhood = null;
                    validAdulthood = null;
                }

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
                    AutoArmLogger.Log($"[TEST] Successfully created violence-capable pawn {pawn.Name} using filtered backstories");
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
                
                // Special handling for creating violence-incapable pawns
                if (!config.EnsureViolenceCapable && !pawn.WorkTagIsDisabled(WorkTags.Violent))
                {
                    bool madeIncapable = false;
                    
                    // Method 1: Try to find and add a trait that disables violence
                    var pacifistTraits = DefDatabase<TraitDef>.AllDefs
                        .Where(t => t.disabledWorkTags.HasFlag(WorkTags.Violent))
                        .ToList();
                    
                    // Try multiple traits if needed
                    foreach (var pacifistTrait in pacifistTraits)
                    {
                        if (pacifistTrait != null && !pawn.story.traits.HasTrait(pacifistTrait))
                        {
                            // Check for conflicting traits
                            var conflictingTraits = pawn.story.traits.allTraits
                                .Where(t => pacifistTrait.ConflictsWith(t))
                                .ToList();
                            
                            foreach (var conflict in conflictingTraits)
                            {
                                pawn.story.traits.RemoveTrait(conflict);
                            }
                            
                            // Remove traits if at capacity
                            while (pawn.story.traits.allTraits.Count >= 3)
                            {
                                var traitToRemove = pawn.story.traits.allTraits
                                    .FirstOrDefault(t => !t.def.disabledWorkTags.HasFlag(WorkTags.Violent));
                                if (traitToRemove != null)
                                {
                                    pawn.story.traits.RemoveTrait(traitToRemove);
                                }
                                else
                                {
                                    break;
                                }
                            }
                            
                            // Add the pacifist trait
                            pawn.story.traits.GainTrait(new Trait(pacifistTrait));
                            pawn.Notify_DisabledWorkTypesChanged();
                            AutoArmLogger.Log($"[TEST] Added pacifist trait {pacifistTrait.defName} to make pawn violence-incapable");
                            
                            if (pawn.WorkTagIsDisabled(WorkTags.Violent))
                            {
                                madeIncapable = true;
                                break;
                            }
                        }
                    }
                    
                    // Method 2: If still not incapable, try to force a pacifist backstory
                    if (!madeIncapable && !pawn.WorkTagIsDisabled(WorkTags.Violent))
                    {
                        // Find backstories that disable violence
                        var pacifistBackstories = DefDatabase<BackstoryDef>.AllDefs.Where(b =>
                        {
                            if (b == null) return false;
                            
                            // Check various property names for disabled work tags
                            var backstoryType = b.GetType();
                            
                            var workDisablesField = backstoryType.GetField("workDisables", BindingFlags.Public | BindingFlags.Instance);
                            if (workDisablesField != null)
                            {
                                var workTags = workDisablesField.GetValue(b) as WorkTags?;
                                if (workTags.HasValue && workTags.Value.HasFlag(WorkTags.Violent))
                                    return true;
                            }
                            
                            var disabledWorkTagsField = backstoryType.GetField("disabledWorkTags", BindingFlags.Public | BindingFlags.Instance);
                            if (disabledWorkTagsField != null)
                            {
                                var workTags = disabledWorkTagsField.GetValue(b) as WorkTags?;
                                if (workTags.HasValue && workTags.Value.HasFlag(WorkTags.Violent))
                                    return true;
                            }
                            
                            return false;
                        }).ToList();
                        
                        if (pacifistBackstories.Any())
                        {
                            // Force override the childhood backstory
                            var pacifistChildhood = pacifistBackstories.FirstOrDefault(b => b.slot == BackstorySlot.Childhood);
                            if (pacifistChildhood != null)
                            {
                                var storyType = pawn.story.GetType();
                                var childhoodField = storyType.GetField("childhood", BindingFlags.NonPublic | BindingFlags.Instance) ??
                                                   storyType.GetField("Childhood", BindingFlags.NonPublic | BindingFlags.Instance);
                                                   
                                if (childhoodField != null)
                                {
                                    childhoodField.SetValue(pawn.story, pacifistChildhood);
                                    pawn.Notify_DisabledWorkTypesChanged();
                                    AutoArmLogger.Log($"[TEST] Forced pacifist backstory {pacifistChildhood.defName} to make pawn violence-incapable");
                                }
                            }
                        }
                    }
                    
                    // Final verification and forced approach if needed
                    if (!pawn.WorkTagIsDisabled(WorkTags.Violent))
                    {
                        // Last resort: Try the "Wimp" trait which is commonly available
                        var wimpTrait = DefDatabase<TraitDef>.GetNamedSilentFail("Wimp");
                        if (wimpTrait != null && wimpTrait.disabledWorkTags.HasFlag(WorkTags.Violent))
                        {
                            // Clear all traits and add wimp
                            pawn.story.traits.allTraits.Clear();
                            pawn.story.traits.GainTrait(new Trait(wimpTrait));
                            pawn.Notify_DisabledWorkTypesChanged();
                            AutoArmLogger.Log("[TEST] Used Wimp trait as last resort for pacifist");
                        }
                        
                        if (!pawn.WorkTagIsDisabled(WorkTags.Violent))
                        {
                            Log.Warning("[TEST] Failed to create violence-incapable pawn after all attempts");
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

                // Only set faction if it's null or different from PlayerColony
                // This avoids the "Used SetFaction to change to same faction" warning
                if (pawn.Faction == null || (pawn.Faction != faction && faction == Faction.OfPlayer))
                {
                    pawn.SetFactionDirect(faction);
                }
                
                // Find a valid spawn position
                IntVec3 spawnPos = map.Center;
                bool foundSpawnPos = CellFinder.TryFindRandomCellNear(map.Center, map, 20,
                    c => c.InBounds(map) && c.Standable(map) && !c.Fogged(map) && 
                         c.GetRoom(map) != null && !c.GetRoom(map).PsychologicallyOutdoors,
                    out spawnPos);
                    
                if (!foundSpawnPos)
                {
                    // Fallback to any standable position
                    foundSpawnPos = CellFinder.TryFindRandomCellNear(map.Center, map, 30,
                        c => c.InBounds(map) && c.Standable(map) && !c.Fogged(map),
                        out spawnPos);
                }
                
                if (!foundSpawnPos)
                {
                    spawnPos = map.Center; // Last resort
                }
                
                GenSpawn.Spawn(pawn, spawnPos, map);
                
                // Ensure outfit system is initialized for weapon tests
                if (pawn.outfits == null)
                {
                    // This shouldn't happen for colonists, but just in case
                    AutoArmLogger.Log($"[TEST] Warning: Pawn {pawn.Name} has no outfit tracker");
                }
                else
                {
                    // Create or get a test outfit that allows all weapons
                    ApparelPolicy testOutfit = null;
                    
                    // Try to find existing test outfit
                    if (Current.Game?.outfitDatabase != null)
                    {
                        testOutfit = Current.Game.outfitDatabase.AllOutfits.FirstOrDefault(o => o.label == "Test Outfit - All Weapons");
                        
                        if (testOutfit == null)
                        {
                            // Create a new outfit that allows all weapons
                            testOutfit = new ApparelPolicy(Current.Game.outfitDatabase.AllOutfits.Count, "Test Outfit - All Weapons");
                            
                            // Configure the filter to allow all weapons
                            if (testOutfit.filter != null)
                            {
                                // Start by allowing everything
                                testOutfit.filter.SetAllowAll(null);
                                
                                // Explicitly ensure all weapons are allowed
                                foreach (ThingDef weaponDef in DefDatabase<ThingDef>.AllDefs.Where(d => d.IsWeapon))
                                {
                                    testOutfit.filter.SetAllow(weaponDef, true);
                                }
                                
                                // Make sure no special disallows are set
                                if (testOutfit.filter.AllowedQualityLevels != null)
                                {
                                    testOutfit.filter.AllowedQualityLevels = QualityRange.All;
                                }
                                
                                if (testOutfit.filter.AllowedHitPointsPercents != null)
                                {
                                    testOutfit.filter.AllowedHitPointsPercents = FloatRange.ZeroToOne;
                                }
                                
                                AutoArmLogger.Log($"[TEST] Created test outfit allowing all weapons");
                            }
                            
                            // Add to outfit database
                            Current.Game.outfitDatabase.AllOutfits.Add(testOutfit);
                        }
                    }
                    
                    // Apply the test outfit or fall back to default
                    if (testOutfit != null)
                    {
                        pawn.outfits.CurrentApparelPolicy = testOutfit;
                        AutoArmLogger.Log($"[TEST] Applied test outfit to pawn {pawn.Name}");
                    }
                    else if (pawn.outfits.CurrentApparelPolicy == null)
                    {
                        // Fallback to default outfit
                        var defaultOutfit = Current.Game?.outfitDatabase?.DefaultOutfit();
                        if (defaultOutfit != null)
                        {
                            pawn.outfits.CurrentApparelPolicy = defaultOutfit;
                            AutoArmLogger.Log($"[TEST] Set default outfit for test pawn {pawn.Name} (couldn't create test outfit)");
                        }
                        else
                        {
                            AutoArmLogger.Log($"[TEST] Warning: No outfit available for test pawn {pawn.Name}");
                        }
                    }
                }
                
                // Configure work settings - disable hunting by default to prevent +500 bonus in tests
                if (pawn.workSettings != null && !config.EnableHunting)
                {
                    // Ensure work settings are initialized
                    if (pawn.workSettings.EverWork)
                    {
                        pawn.workSettings.EnableAndInitialize();
                    }
                    
                    // Disable hunting work to prevent the +500 hunter bonus from affecting tests
                    if (WorkTypeDefOf.Hunting != null && !pawn.WorkTypeIsDisabled(WorkTypeDefOf.Hunting))
                    {
                        try
                        {
                            pawn.workSettings.SetPriority(WorkTypeDefOf.Hunting, 0);
                            AutoArmLogger.Log($"[TEST] Disabled hunting work for test pawn {pawn.Name} to prevent +500 bonus");
                        }
                        catch (Exception e)
                        {
                            Log.Warning($"[TEST] Failed to disable hunting for test pawn {pawn.Name}: {e.Message}");
                        }
                    }
                }
                else if (pawn.workSettings != null && config.EnableHunting)
                {
                    // If hunting is explicitly enabled, make sure it's set
                    if (pawn.workSettings.EverWork)
                    {
                        pawn.workSettings.EnableAndInitialize();
                    }
                    
                    if (WorkTypeDefOf.Hunting != null && !pawn.WorkTypeIsDisabled(WorkTypeDefOf.Hunting))
                    {
                        try
                        {
                            pawn.workSettings.SetPriority(WorkTypeDefOf.Hunting, 1);
                            AutoArmLogger.Log($"[TEST] Enabled hunting work for test pawn {pawn.Name} (explicitly requested)");
                        }
                        catch (Exception e)
                        {
                            Log.Warning($"[TEST] Failed to enable hunting for test pawn {pawn.Name}: {e.Message}");
                        }
                    }
                }
                
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

            // Ensure the position is reachable
            IntVec3 spawnPos = position;
            if (!position.InBounds(map) || !position.Standable(map))
            {
                // Find a nearby standable position
                bool found = CellFinder.TryFindRandomCellNear(position, map, 5, 
                    c => c.InBounds(map) && c.Standable(map) && !c.Fogged(map), out spawnPos);
                    
                if (!found)
                {
                    // Try to find any standable position near the map center
                    found = CellFinder.TryFindRandomCellNear(map.Center, map, 20,
                        c => c.InBounds(map) && c.Standable(map) && !c.Fogged(map), out spawnPos);
                        
                    if (!found)
                    {
                        Log.Warning($"[AutoArm] Could not find valid spawn position for weapon {weaponDef?.defName}");
                        return null;
                    }
                }
            }

            // Use the safe creation method
            var weapon = SafeTestCleanup.SafeCreateWeapon(weaponDef, null, quality);
            if (weapon == null) return null;
            
            // Spawn at the validated position
            try
            {
                GenSpawn.Spawn(weapon, spawnPos, map);
                weapon.SetForbidden(false, false); // Ensure weapon is not forbidden
                return weapon;
            }
            catch (Exception e)
            {
                Log.Warning($"[AutoArm] Failed to spawn weapon {weaponDef?.defName}: {e.Message}");
                if (!weapon.Destroyed)
                    weapon.Destroy();
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
            SafeTestCleanup.SafeDestroyWeapon(weapon);
        }
    }
}