using AutoArm.Caching;
using AutoArm.Testing.Helpers;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;
using Verse.AI;

namespace AutoArm.Testing
{
    internal static class TestHelpers
    {
        internal class TestPawnConfig
        {
            public string Name = "TestPawn";
            public List<TraitDef> Traits = new List<TraitDef>();
            public Dictionary<SkillDef, int> Skills = new Dictionary<SkillDef, int>();
            public PawnKindDef Kind = PawnKindDefOf.Colonist;
            public int? BiologicalAge = null;
            public bool EnsureViolenceCapable = true;
            public IntVec3? SpawnPosition = null;
        }

        public static Pawn CreateTestPawn(Map map, TestPawnConfig config = null)
        {
            if (map == null)
            {
                AutoArmLogger.Warn("[TEST] CreateTestPawn: Map is null");
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
                    AutoArmLogger.Warn("[TEST] CreateTestPawn: PawnGenerator returned null");
                    return null;
                }

                if (config.EnsureViolenceCapable && pawn.WorkTagIsDisabled(WorkTags.Violent))
                {
                    FixViolenceCapability(pawn);
                }

                StripConflictingTraits(pawn, config.Traits);
                StripConflictingApparel(pawn);

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

                IntVec3 spawnPos = FindSafeSpawnPosition(map, config.SpawnPosition ?? map.Center);

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

                CleanupTracker.MarkCreated(pawn);

                return pawn;
            }
            catch (Exception e)
            {
                AutoArmLogger.Warn($"[TEST] Failed to create test pawn", e);
                return null;
            }
        }

        private static void StripConflictingTraits(Pawn pawn, List<TraitDef> requestedTraits)
        {
            if (pawn?.story?.traits?.allTraits == null) return;

            bool keepBrawler = requestedTraits != null && requestedTraits.Contains(TraitDefOf.Brawler);
            if (!keepBrawler)
            {
                var brawler = pawn.story.traits.allTraits.FirstOrDefault(t => t.def == TraitDefOf.Brawler);
                if (brawler != null) pawn.story.traits.allTraits.Remove(brawler);
            }

            var shooting = DefDatabase<TraitDef>.GetNamedSilentFail("ShootingAccuracy");
            if (shooting != null && (requestedTraits == null || !requestedTraits.Contains(shooting)))
                pawn.story.traits.allTraits.RemoveAll(t => t.def == shooting);
        }

        private static void StripConflictingApparel(Pawn pawn)
        {
            if (pawn?.apparel == null) return;
            var shields = pawn.apparel.WornApparel
                .Where(a => a.def.defName.IndexOf("Shield", StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();
            foreach (var s in shields)
            {
                pawn.apparel.Remove(s);
                s.Destroy();
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
                AutoArmLogger.Warn($"[TEST] Error fixing violence capability for {pawn.Name}", e);
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
                AutoArmLogger.Warn($"[TEST] Error setting skill level", e);
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
                AutoArmLogger.Warn($"[TEST] Pawn {pawn.Name} spawned without equipment tracker");
            }
            if (pawn.jobs == null)
            {
                AutoArmLogger.Warn($"[TEST] Pawn {pawn.Name} spawned without job tracker");
            }
            if (pawn.inventory == null)
            {
                AutoArmLogger.Warn($"[TEST] Pawn {pawn.Name} spawned without inventory tracker");
            }
        }

        public static ThingWithComps CreateWeapon(Map map, ThingDef weaponDef, IntVec3 position,
            QualityCategory quality = QualityCategory.Normal)
        {
            if (map == null || weaponDef == null || !position.IsValid)
            {
                AutoArmLogger.Warn($"[TEST] CreateWeapon: Invalid parameters - map:{map != null}, def:{weaponDef != null}, pos:{position.IsValid}");
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
                    AutoArmLogger.Warn($"[TEST] CreateWeapon: Failed to create weapon {weaponDef.defName}");
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

                if (TestRunner.IsRunningTests)
                    CleanupTracker.MarkCreated(weapon);


                return weapon;
            }
            catch (Exception e)
            {
                AutoArmLogger.Warn($"[TEST] Failed to create weapon {weaponDef?.defName}", e);
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
                AutoArmLogger.Warn("[TEST] CreateStockpile: Invalid parameters");
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
                AutoArmLogger.Warn($"[TEST] Failed to create test stockpile", e);
                return null;
            }
        }

        public static List<ThingWithComps> CachedWeapons(Map map)
        {
            var weapons = new List<ThingWithComps>();
            WeaponCache.Collect(map, weapons);
            return weapons;
        }

        public static void SafeDestroyWeapon(ThingWithComps weapon)
        {
            AutoArm.Testing.Helpers.CleanupHelper.DestroyWeapon(weapon);
        }

        public static void SafeDestroyPawn(Pawn pawn)
        {
            AutoArm.Testing.Helpers.CleanupHelper.DestroyPawn(pawn);
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

    internal static class Sim
    {
        public static void Tick(int ticks)
        {
            var tickManager = Find.TickManager;
            for (int i = 0; i < ticks; i++)
                tickManager.DoSingleTick();
        }

        public static bool TickUntil(Func<bool> done, int maxTicks)
        {
            var tickManager = Find.TickManager;
            for (int i = 0; i < maxTicks; i++)
            {
                if (done())
                    return true;
                tickManager.DoSingleTick();
            }
            return done();
        }

        public static bool RunJob(Pawn pawn, Job job, int maxTicks = 2500)
        {
            int id = job.loadID;
            pawn.jobs.StartJob(job, JobCondition.InterruptForced);
            return TickUntil(() => pawn.CurJob == null || pawn.CurJob.loadID != id, maxTicks);
        }

        public static void Idle(Pawn pawn)
        {
            pawn.jobs?.StopAll();
            if (pawn.workSettings != null && pawn.workSettings.EverWork)
                pawn.workSettings.DisableAll();
            if (pawn.timetable != null)
            {
                for (int hour = 0; hour < 24; hour++)
                    pawn.timetable.SetAssignment(hour, TimeAssignmentDefOf.Anything);
            }
            var needs = pawn.needs;
            if (needs?.food != null) needs.food.CurLevelPercentage = 1f;
            if (needs?.rest != null) needs.rest.CurLevelPercentage = 1f;
            if (needs?.joy != null) needs.joy.CurLevelPercentage = 1f;
            if (pawn.mindState != null) pawn.mindState.nextApparelOptimizeTick = int.MaxValue;
            if (pawn.drugs != null)
            {
                var sober = Current.Game.drugPolicyDatabase.AllPolicies.FirstOrDefault(Sober);
                if (sober != null)
                    pawn.drugs.CurrentPolicy = sober;
            }
        }

        private static bool Sober(DrugPolicy policy)
        {
            for (int i = 0; i < policy.Count; i++)
            {
                if (policy[i].allowScheduled || policy[i].takeToInventory > 0)
                    return false;
            }
            return true;
        }
    }

    internal sealed class SimScope : IDisposable
    {
        private readonly List<Pawn> drafted = new List<Pawn>();
        private readonly bool wasRunning;
        private readonly bool wasTestMode;

        public SimScope(Map map, params Pawn[] actors)
        {
            foreach (var colonist in map.mapPawns.FreeColonistsSpawned.ToList())
            {
                if (actors.Contains(colonist))
                {
                    Sim.Idle(colonist);
                    continue;
                }
                if (colonist.drafter != null && !colonist.Drafted && !colonist.Downed)
                {
                    colonist.drafter.Drafted = true;
                    drafted.Add(colonist);
                }
            }

            wasRunning = TestRunner.IsRunningTests;
            wasTestMode = Jobs.JobGiver_PickUpBetterWeapon.TestModeEnabled;
            Jobs.JobGiver_PickUpBetterWeapon.EnableTestMode(false);
            TestRunner.IsRunningTests = false;
        }

        public void Dispose()
        {
            TestRunner.IsRunningTests = wasRunning;
            Jobs.JobGiver_PickUpBetterWeapon.EnableTestMode(wasTestMode);
            foreach (var colonist in drafted)
            {
                if (colonist.Spawned && colonist.drafter != null)
                    colonist.drafter.Drafted = false;
            }
        }
    }
}

namespace AutoArm.Testing
{
    internal static class IdeoTestHelper
    {
        private static readonly HarmonyLib.AccessTools.FieldRef<RimWorld.Precept_Weapon, HashSet<ThingDef>> despisedCache =
            HarmonyLib.AccessTools.FieldRefAccess<RimWorld.Precept_Weapon, HashSet<ThingDef>>("despisedWeaponsCached");

        private static readonly HarmonyLib.AccessTools.FieldRef<RimWorld.Precept_Weapon, HashSet<ThingDef>> nobleCache =
            HarmonyLib.AccessTools.FieldRefAccess<RimWorld.Precept_Weapon, HashSet<ThingDef>>("nobleWeaponsCached");

        public static void SetDespised(RimWorld.Precept_Weapon precept, WeaponClassDef weaponClass)
        {
            precept.despised = weaponClass;
            despisedCache(precept) = null;
        }

        public static void SetNoble(RimWorld.Precept_Weapon precept, WeaponClassDef weaponClass)
        {
            precept.noble = weaponClass;
            nobleCache(precept) = null;
        }
    }

    internal static class SidearmLimits
    {
        private static object Settings()
        {
            if (!AutoArm.Compatibility.SimpleSidearmsCompat.IsLoaded)
                return null;
            var mod = HarmonyLib.AccessTools.TypeByName("PeteTimesSix.SimpleSidearms.SimpleSidearms");
            return mod == null ? null : HarmonyLib.AccessTools.Property(mod, "Settings")?.GetValue(null, null);
        }

        public static List<(object settings, FieldInfo field, object value)> Set(params (string name, int value)[] values)
        {
            var changed = new List<(object, FieldInfo, object)>();
            var settings = Settings();
            if (settings == null)
                return changed;
            foreach (var (name, value) in values)
            {
                var field = settings.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance);
                if (field == null)
                    continue;
                changed.Add((settings, field, field.GetValue(settings)));
                field.SetValue(settings, field.FieldType.IsEnum ? Enum.ToObject(field.FieldType, value) : (object)value);
            }
            AutoArm.Compatibility.SimpleSidearmsCompat.InvalidateAllCaches();
            return changed;
        }

        public static List<(object settings, FieldInfo field, object value)> Lift()
        {
            var lifted = new List<(object, FieldInfo, object)>();
            var settings = Settings();
            if (settings == null)
                return lifted;
            foreach (var field in settings.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!field.FieldType.IsEnum || !field.Name.StartsWith("LimitMode"))
                    continue;
                lifted.Add((settings, field, field.GetValue(settings)));
                field.SetValue(settings, Enum.ToObject(field.FieldType, 0));
            }
            AutoArm.Compatibility.SimpleSidearmsCompat.InvalidateAllCaches();
            return lifted;
        }

        public static void Restore(List<(object settings, FieldInfo field, object value)> lifted)
        {
            if (lifted.Count == 0)
                return;
            foreach (var (settings, field, value) in lifted)
                field.SetValue(settings, value);
            AutoArm.Compatibility.SimpleSidearmsCompat.InvalidateAllCaches();
        }
    }
}
