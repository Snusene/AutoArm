using AutoArm.Logging;
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
            public bool EnsureViolenceCapable = true;
            public IntVec3? SpawnPosition = null;
        }

        public static Pawn CreateTestPawn(Map map, TestPawnConfig config = null)
        {
            if (map == null)
            {
                AutoArmLogger.Error("[TEST] CreateTestPawn: Map is null");
                return null;
            }

            config = config ?? new TestPawnConfig();

            try
            {
                var faction = Faction.OfPlayer;
                var pawnKind = config.Kind;

                // Handle child pawns if Biotech is active
                if (config.BiologicalAge.HasValue && config.BiologicalAge.Value < 18 && ModsConfig.BiotechActive)
                {
                    var childKind = DefDatabase<PawnKindDef>.GetNamedSilentFail("Colonist_Child");
                    if (childKind != null)
                        pawnKind = childKind;
                }

                // Create pawn generation request
                var request = new PawnGenerationRequest(
                    pawnKind,
                    faction,
                    PawnGenerationContext.NonPlayer,
                    -1,
                    forceGenerateNewPawn: true,
                    allowDead: false,
                    allowDowned: false,
                    canGeneratePawnRelations: false,
                    mustBeCapableOfViolence: config.EnsureViolenceCapable
                );

                // Set age if specified
                if (config.BiologicalAge.HasValue)
                {
                    request.FixedBiologicalAge = config.BiologicalAge.Value;
                    request.FixedChronologicalAge = config.BiologicalAge.Value;
                }

                // Generate the pawn
                var pawn = PawnGenerator.GeneratePawn(request);
                if (pawn == null)
                {
                    AutoArmLogger.Error("[TEST] CreateTestPawn: PawnGenerator returned null");
                    return null;
                }

                // Ensure violence capability if requested
                if (config.EnsureViolenceCapable && pawn.WorkTagIsDisabled(WorkTags.Violent))
                {
                    FixViolenceCapability(pawn);
                }

                // Apply traits
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

                // Apply skills
                if (config.Skills != null && pawn.skills?.skills != null)
                {
                    foreach (var kvp in config.Skills)
                    {
                        var skill = pawn.skills.GetSkill(kvp.Key);
                        if (skill != null)
                        {
                            SetSkillLevel(skill, kvp.Value);
                        }
                    }
                }

                // Apply nobility if requested
                if (config.MakeNoble && ModsConfig.RoyaltyActive)
                {
                    ApplyNobility(pawn, config.Conceited);
                }

                // Find spawn position
                IntVec3 spawnPos = config.SpawnPosition ?? FindSafeSpawnPosition(map, map.Center);

                // Spawn the pawn
                GenSpawn.Spawn(pawn, spawnPos, map);

                // Verify pawn systems are initialized
                VerifyPawnSystems(pawn);

                AutoArmLogger.Debug($"[TEST] Created test pawn {pawn.Name} at {spawnPos}");
                return pawn;
            }
            catch (Exception e)
            {
                AutoArmLogger.Error($"[TEST] Failed to create test pawn", e);
                return null;
            }
        }

        private static void FixViolenceCapability(Pawn pawn)
        {
            if (pawn?.story == null) return;

            try
            {
                // Remove traits that disable violence
                pawn.story.traits.allTraits.RemoveAll(t => 
                    t.def.disabledWorkTags.HasFlag(WorkTags.Violent));

                // Find and apply violence-capable backstories
                var validChildhood = DefDatabase<BackstoryDef>.AllDefs
                    .Where(b => b.slot == BackstorySlot.Childhood && 
                           !HasDisabledWorkTag(b, WorkTags.Violent))
                    .RandomElementWithFallback();

                var validAdulthood = DefDatabase<BackstoryDef>.AllDefs
                    .Where(b => b.slot == BackstorySlot.Adulthood && 
                           !HasDisabledWorkTag(b, WorkTags.Violent))
                    .RandomElementWithFallback();

                // Apply backstories via reflection
                var storyType = pawn.story.GetType();
                
                if (validChildhood != null)
                {
                    SetBackstory(pawn.story, storyType, "childhood", validChildhood);
                }
                
                if (validAdulthood != null && pawn.ageTracker.AgeBiologicalYears >= 20)
                {
                    SetBackstory(pawn.story, storyType, "adulthood", validAdulthood);
                }

                // Recalculate work types
                pawn.Notify_DisabledWorkTypesChanged();

                if (pawn.WorkTagIsDisabled(WorkTags.Violent))
                {
                    AutoArmLogger.Warn($"[TEST] Failed to make pawn {pawn.Name} violence-capable");
                }
            }
            catch (Exception e)
            {
                AutoArmLogger.Error($"[TEST] Error fixing violence capability for {pawn.Name}", e);
            }
        }

        private static bool HasDisabledWorkTag(BackstoryDef backstory, WorkTags tag)
        {
            if (backstory == null) return false;

            var type = backstory.GetType();
            var fields = new[] { "workDisables", "disabledWorkTags", "DisabledWorkTags" };

            foreach (var fieldName in fields)
            {
                var field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
                var prop = type.GetProperty(fieldName, BindingFlags.Public | BindingFlags.Instance);

                object value = field?.GetValue(backstory) ?? prop?.GetValue(backstory);
                if (value is WorkTags workTags)
                {
                    return workTags.HasFlag(tag);
                }
            }

            return false;
        }

        private static void SetBackstory(Pawn_StoryTracker story, Type storyType, string backstoryName, BackstoryDef backstory)
        {
            var fieldNames = new[] { backstoryName, char.ToUpper(backstoryName[0]) + backstoryName.Substring(1), "_" + backstoryName };
            
            foreach (var name in fieldNames)
            {
                var field = storyType.GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                if (field != null)
                {
                    field.SetValue(story, backstory);
                    return;
                }
            }
        }

        private static void SetSkillLevel(SkillRecord skill, int level)
        {
            try
            {
                // Disable skill decay
                skill.xpSinceLastLevel = 1000f;
                skill.xpSinceMidnight = 0f;

                // Set level via reflection to bypass restrictions
                var skillType = skill.GetType();
                var levelField = skillType.GetField("levelInt", BindingFlags.NonPublic | BindingFlags.Instance) ??
                               skillType.GetField("level", BindingFlags.NonPublic | BindingFlags.Instance);

                if (levelField != null)
                {
                    levelField.SetValue(skill, level);
                }
                else
                {
                    // Fallback to property
                    skill.Level = level;
                }

                // Verify
                if (skill.Level != level)
                {
                    AutoArmLogger.Warn($"[TEST] Failed to set skill level to {level}, actual: {skill.Level}");
                }
            }
            catch (Exception e)
            {
                AutoArmLogger.Error($"[TEST] Error setting skill level", e);
            }
        }

        private static void ApplyNobility(Pawn pawn, bool conceited)
        {
            if (pawn?.royalty == null) return;

            try
            {
                var empireFaction = Find.FactionManager.FirstFactionOfDef(FactionDefOf.Empire);
                if (empireFaction == null) return;

                RoyalTitleDef titleDef = null;

                if (conceited)
                {
                    // Find a conceited title
                    var royalTitleDefType = typeof(RoyalTitleDef);
                    var conceitedField = royalTitleDefType.GetField("conceited", BindingFlags.Public | BindingFlags.Instance);

                    if (conceitedField != null)
                    {
                        titleDef = DefDatabase<RoyalTitleDef>.AllDefs
                            .Where(t => t.seniority > 100)
                            .FirstOrDefault(t => conceitedField.GetValue(t) is bool b && b);
                    }
                }

                if (titleDef == null)
                {
                    // Fallback to any high-ranking title
                    titleDef = DefDatabase<RoyalTitleDef>.GetNamedSilentFail("Count") ??
                              DefDatabase<RoyalTitleDef>.AllDefs.FirstOrDefault(t => t.seniority > 100);
                }

                if (titleDef != null)
                {
                    pawn.royalty.SetTitle(empireFaction, titleDef, false);
                }
            }
            catch (Exception e)
            {
                AutoArmLogger.Error($"[TEST] Error applying nobility to {pawn.Name}", e);
            }
        }

        private static IntVec3 FindSafeSpawnPosition(Map map, IntVec3 preferredPos)
        {
            if (preferredPos.InBounds(map) && preferredPos.Standable(map))
                return preferredPos;

            // Try nearby positions
            if (CellFinder.TryFindRandomCellNear(preferredPos, map, 10, 
                c => c.Standable(map) && c.GetRoom(map) != null, out IntVec3 nearbyPos))
            {
                return nearbyPos;
            }

            // Fallback to any valid position
            return CellFinder.RandomCell(map);
        }

        private static void VerifyPawnSystems(Pawn pawn)
        {
            if (pawn.equipment == null)
            {
                AutoArmLogger.Error($"[TEST] Pawn {pawn.Name} spawned without equipment tracker");
            }
            if (pawn.jobs == null)
            {
                AutoArmLogger.Error($"[TEST] Pawn {pawn.Name} spawned without job tracker");
            }
            if (pawn.inventory == null)
            {
                AutoArmLogger.Error($"[TEST] Pawn {pawn.Name} spawned without inventory tracker");
            }
        }

        public static ThingWithComps CreateWeapon(Map map, ThingDef weaponDef, IntVec3 position, 
            QualityCategory quality = QualityCategory.Normal)
        {
            if (map == null || weaponDef == null || !position.IsValid)
            {
                AutoArmLogger.Error($"[TEST] CreateWeapon: Invalid parameters - map:{map != null}, def:{weaponDef != null}, pos:{position.IsValid}");
                return null;
            }

            try
            {
                // Ensure position is valid
                if (!position.InBounds(map) || !position.Standable(map))
                {
                    position = FindSafeSpawnPosition(map, position);
                }

                ThingWithComps weapon;

                if (weaponDef.MadeFromStuff)
                {
                    var stuffDef = GetBestStuffFor(weaponDef);
                    weapon = ThingMaker.MakeThing(weaponDef, stuffDef) as ThingWithComps;
                }
                else
                {
                    weapon = ThingMaker.MakeThing(weaponDef) as ThingWithComps;
                }

                if (weapon == null)
                {
                    AutoArmLogger.Error($"[TEST] CreateWeapon: Failed to create weapon {weaponDef.defName}");
                    return null;
                }

                // Set quality
                var compQuality = weapon.TryGetComp<CompQuality>();
                if (compQuality != null)
                {
                    compQuality.SetQuality(quality, ArtGenerationContext.Colony);
                }

                // Spawn and unforbid
                GenSpawn.Spawn(weapon, position, map);
                weapon.SetForbidden(false);

                AutoArmLogger.Debug($"[TEST] Created weapon {weapon.Label} at {position}");
                return weapon;
            }
            catch (Exception e)
            {
                AutoArmLogger.Error($"[TEST] Failed to create weapon {weaponDef?.defName}", e);
                return null;
            }
        }

        private static ThingDef GetBestStuffFor(ThingDef weaponDef)
        {
            if (weaponDef?.stuffCategories == null || weaponDef.stuffCategories.Count == 0)
                return ThingDefOf.Steel;

            // Prefer plasteel for melee weapons
            if (weaponDef.IsMeleeWeapon)
            {
                var plasteel = DefDatabase<ThingDef>.GetNamedSilentFail("Plasteel");
                if (plasteel != null && CanUseStuff(weaponDef, plasteel))
                    return plasteel;
            }

            // Always try steel first as it's the most common
            var steel = ThingDefOf.Steel;
            if (steel != null && CanUseStuff(weaponDef, steel))
                return steel;

            // Find first valid material
            foreach (var category in weaponDef.stuffCategories)
            {
                var validStuff = DefDatabase<ThingDef>.AllDefs
                    .FirstOrDefault(td => td.stuffProps?.categories?.Contains(category) == true);
                if (validStuff != null)
                    return validStuff;
            }

            return ThingDefOf.Steel;
        }

        private static bool CanUseStuff(ThingDef weaponDef, ThingDef stuffDef)
        {
            if (weaponDef?.stuffCategories == null || stuffDef?.stuffProps?.categories == null)
                return false;

            return weaponDef.stuffCategories.Any(wc => stuffDef.stuffProps.categories.Contains(wc));
        }

        public static ThingDef GetWeaponDef(string defName)
        {
            var def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (def == null)
            {
                AutoArmLogger.Warn($"[TEST] Weapon def '{defName}' not found");
            }
            return def;
        }

        public static Zone_Stockpile CreateStockpile(Map map, IntVec3 center, int size = 5)
        {
            if (map == null || !center.IsValid)
            {
                AutoArmLogger.Error("[TEST] CreateStockpile: Invalid parameters");
                return null;
            }

            try
            {
                // Clean up overlapping stockpiles
                var existingZones = map.zoneManager.AllZones.OfType<Zone_Stockpile>()
                    .Where(z => z.Cells.Any(c => c.DistanceTo(center) <= size + 2))
                    .ToList();
                
                foreach (var zone in existingZones)
                {
                    zone.Delete();
                }

                // Find valid cells
                var cells = new List<IntVec3>();
                for (int x = -size; x <= size; x++)
                {
                    for (int z = -size; z <= size; z++)
                    {
                        var cell = center + new IntVec3(x, 0, z);
                        if (cell.InBounds(map) && cell.Standable(map) && cell.GetRoom(map) != null)
                        {
                            cells.Add(cell);
                        }
                    }
                }

                if (cells.Count < 4)
                {
                    AutoArmLogger.Warn($"[TEST] CreateStockpile: Only {cells.Count} valid cells found");
                    return null;
                }

                // Create stockpile
                var stockpile = new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile, map.zoneManager);
                map.zoneManager.RegisterZone(stockpile);

                foreach (var cell in cells)
                {
                    stockpile.AddCell(cell);
                }

                // Configure to accept all items
                stockpile.settings.filter.SetDisallowAll();
                stockpile.settings.filter.SetAllow(ThingCategoryDefOf.Root, true);

                AutoArmLogger.Debug($"[TEST] Created stockpile with {cells.Count} cells at {center}");
                return stockpile;
            }
            catch (Exception e)
            {
                AutoArmLogger.Error($"[TEST] Failed to create test stockpile", e);
                return null;
            }
        }
    }
}
