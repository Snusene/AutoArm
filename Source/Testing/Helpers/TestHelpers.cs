using AutoArm.Caching;
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
        private static int _createdWeaponsCount;

        private static int _lastCreatedSummaryTick = -1;
        private const int CreatedSummaryWindowTicks = 300;

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
                    -1,
                    forceGenerateNewPawn: true,
                    allowDead: false,
                    allowDowned: false,
                    canGeneratePawnRelations: false,
                    mustBeCapableOfViolence: config.EnsureViolenceCapable
                );

                if (config.BiologicalAge.HasValue)
                {
                    request.FixedBiologicalAge = config.BiologicalAge.Value;
                    request.FixedChronologicalAge = config.BiologicalAge.Value;
                }

                var pawn = PawnGenerator.GeneratePawn(request);
                if (pawn == null)
                {
                    AutoArmLogger.Error("[TEST] CreateTestPawn: PawnGenerator returned null");
                    return null;
                }

                if (config.EnsureViolenceCapable && pawn.WorkTagIsDisabled(WorkTags.Violent))
                {
                    FixViolenceCapability(pawn);
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
                            SetSkillLevel(skill, kvp.Value);
                        }
                    }
                }

                if (config.MakeNoble && ModsConfig.RoyaltyActive)
                {
                    ApplyNobility(pawn, config.Conceited);
                }

                IntVec3 spawnPos = config.SpawnPosition ?? FindSafeSpawnPosition(map, map.Center);

                GenSpawn.Spawn(pawn, spawnPos, map);

                if (pawn.Faction == Faction.OfPlayer && !pawn.IsColonist)
                {
                    if (map.mapPawns != null)
                    {
                        if (!map.mapPawns.FreeColonists.Contains(pawn))
                        {
                            AutoArmLogger.Debug(() => $"[TEST] WARNING: Pawn {pawn.LabelShort} spawned with player faction but not recognized as colonist!");
                        }
                    }
                }

                VerifyPawnSystems(pawn);


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
                pawn.story.traits.allTraits.RemoveAll(t =>
                    t.def.disabledWorkTags.HasFlag(WorkTags.Violent));

                var validChildhood = DefDatabase<BackstoryDef>.AllDefs
                    .Where(b => b.slot == BackstorySlot.Childhood &&
                           !HasDisabledWorkTag(b, WorkTags.Violent))
                    .RandomElementWithFallback();

                var validAdulthood = DefDatabase<BackstoryDef>.AllDefs
                    .Where(b => b.slot == BackstorySlot.Adulthood &&
                           !HasDisabledWorkTag(b, WorkTags.Violent))
                    .RandomElementWithFallback();

                var storyType = pawn.story.GetType();

                if (validChildhood != null)
                {
                    SetBackstory(pawn.story, storyType, "childhood", validChildhood);
                }

                if (validAdulthood != null && pawn.ageTracker.AgeBiologicalYears >= 20)
                {
                    SetBackstory(pawn.story, storyType, "adulthood", validAdulthood);
                }

                pawn.Notify_DisabledWorkTypesChanged();

                if (pawn.WorkTagIsDisabled(WorkTags.Violent))
                {
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
                skill.xpSinceLastLevel = 1000f;
                skill.xpSinceMidnight = 0f;

                var skillType = skill.GetType();
                var levelField = skillType.GetField("levelInt", BindingFlags.NonPublic | BindingFlags.Instance) ??
                               skillType.GetField("level", BindingFlags.NonPublic | BindingFlags.Instance);

                if (levelField != null)
                {
                    levelField.SetValue(skill, level);
                }
                else
                {
                    skill.Level = level;
                }

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
            if (preferredPos.InBounds(map) && preferredPos.Standable(map) && preferredPos.Walkable(map))
                return preferredPos;

            if (CellFinder.TryFindRandomCellNear(preferredPos, map, 10,
                c => c.Standable(map) && c.Walkable(map) && c.GetRoom(map) != null && !c.Fogged(map),
                out IntVec3 nearbyPos))
            {
                return nearbyPos;
            }

            if (CellFinder.TryFindRandomCellNear(preferredPos, map, 20,
                c => c.Standable(map) && c.Walkable(map),
                out IntVec3 expandedPos))
            {
                return expandedPos;
            }

            IntVec3 randomPos;
            if (CellFinder.TryRandomClosewalkCellNear(map.Center, map, 50, out randomPos))
            {
                return randomPos;
            }

            for (int i = 0; i < 200; i++)
            {
                var cell = CellFinder.RandomCell(map);
                if (cell.Standable(map) && cell.Walkable(map))
                {
                    return cell;
                }
            }

            var center = map.Center;
            if (!center.Walkable(map))
            {
                CellFinder.TryFindRandomCellNear(center, map, 30,
                    c => c.Walkable(map), out center);
            }
            return center;
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
                if (!position.InBounds(map) || !position.Standable(map))
                {
                    position = FindSafeSpawnPosition(map, position);
                }

                ThingWithComps weapon;

                if (weaponDef.MadeFromStuff)
                {
                    var stuffDef = GetStuff(weaponDef);
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

                var compQuality = weapon.TryGetComp<CompQuality>();
                if (compQuality != null)
                {
                    compQuality.SetQuality(quality, ArtGenerationContext.Colony);
                }

                GenSpawn.Spawn(weapon, position, map);

                weapon.SetForbidden(false, false);

                var forbidComp = weapon.TryGetComp<CompForbiddable>();
                if (forbidComp != null)
                {
                    forbidComp.Forbidden = false;
                }

                var playerFaction = Find.FactionManager?.OfPlayer;
                if (playerFaction != null && weapon.IsForbidden(playerFaction))
                {
                    AutoArmLogger.Debug(() => $"[TEST] CreateWeapon: Weapon {weapon.Label} still forbidden after unforbidding attempt");

                    weapon.SetFaction(playerFaction);
                    weapon.SetForbidden(false, false);
                }

                WeaponCacheManager.AddWeaponToCache(weapon);

                if (TestRunner.IsRunningTests)
                {
                    if (!WeaponCacheManager.IsWeaponTracked(map, weapon))
                    {
                        AutoArmLogger.Error($"[TEST] CreateWeapon: Weapon {weapon.Label} not found in cache after adding!");

                        WeaponCacheManager.AddWeaponToCache(weapon);

                        if (!WeaponCacheManager.IsWeaponTracked(map, weapon))
                        {
                            AutoArmLogger.Error($"[TEST] CreateWeapon: Weapon {weapon.Label} STILL not in cache after second add attempt!");
                        }
                    }
                    else if (AutoArmMod.settings?.debugLogging == true)
                    {
                        int now = Find.TickManager?.TicksGame ?? 0;
                        _createdWeaponsCount++;
                        if (_lastCreatedSummaryTick < 0)
                        {
                            _lastCreatedSummaryTick = now;
                        }
                        else if (now - _lastCreatedSummaryTick >= CreatedSummaryWindowTicks)
                        {
                            AutoArmLogger.Debug(() => $"[TEST] Weapons created and cached: {_createdWeaponsCount} in last 5s");
                            _createdWeaponsCount = 0;
                            _lastCreatedSummaryTick = now;
                        }
                    }
                }


                return weapon;
            }
            catch (Exception e)
            {
                AutoArmLogger.Error($"[TEST] Failed to create weapon {weaponDef?.defName}", e);
                return null;
            }
        }

        private static ThingDef GetStuff(ThingDef weaponDef)
        {
            if (weaponDef?.stuffCategories == null || weaponDef.stuffCategories.Count == 0)
                return ThingDefOf.Steel;

            if (weaponDef.IsMeleeWeapon)
            {
                var plasteel = DefDatabase<ThingDef>.GetNamedSilentFail("Plasteel");
                if (plasteel != null && CanUseStuff(weaponDef, plasteel))
                    return plasteel;
            }

            var steel = ThingDefOf.Steel;
            if (steel != null && CanUseStuff(weaponDef, steel))
                return steel;

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
                var existingZones = map.zoneManager.AllZones.OfType<Zone_Stockpile>()
                    .Where(z => z.Cells.Any(c => c.DistanceTo(center) <= size + 2))
                    .ToList();

                foreach (var zone in existingZones)
                {
                    zone.Delete();
                }

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

                var stockpile = new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile, map.zoneManager);
                map.zoneManager.RegisterZone(stockpile);

                foreach (var cell in cells)
                {
                    stockpile.AddCell(cell);
                }

                stockpile.settings.filter.SetDisallowAll();
                stockpile.settings.filter.SetAllow(ThingCategoryDefOf.Root, true);

                return stockpile;
            }
            catch (Exception e)
            {
                AutoArmLogger.Error($"[TEST] Failed to create test stockpile", e);
                return null;
            }
        }

        public static void SafeDestroyWeapon(ThingWithComps weapon)
        {
            AutoArm.Testing.Framework.TestCleanupHelper.DestroyWeapon(weapon);
        }

        public static void SafeDestroyPawn(Pawn pawn)
        {
            AutoArm.Testing.Framework.TestCleanupHelper.DestroyPawn(pawn);
        }

        public static void CleanupWeapons(List<ThingWithComps> weapons)
        {
            if (weapons == null)
                return;

            foreach (var weapon in weapons)
            {
                SafeDestroyWeapon(weapon);
            }
            weapons.Clear();
        }

        /// <summary>
        /// Clean up test pawns list
        /// </summary>
        public static void CleanupPawns(List<Pawn> pawns)
        {
            if (pawns == null)
                return;

            foreach (var pawn in pawns)
            {
                SafeDestroyPawn(pawn);
            }
            pawns.Clear();
        }
    }
}
