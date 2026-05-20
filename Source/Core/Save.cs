
using AutoArm.Caching;
using AutoArm.Helpers;
using AutoArm.Jobs;
using AutoArm.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace AutoArm
{
    public sealed class AutoArmGameComponent : GameComponent
    {
        private const string SIDEARM_PAWN_IDS_KEY = "forcedSidearmPawnIds";
        private const string SIDEARM_COUNT_KEY = "forcedSidearmDefListCount";
        private const string SIDEARM_LIST_PREFIX = "forcedSidearmDefList_";

        private const string WEAPON_ID_PAWN_IDS_KEY = "forcedWeaponIdPawnIds";
        private const string WEAPON_ID_COUNT_KEY = "forcedWeaponIdListCount";
        private const string WEAPON_ID_LIST_PREFIX = "forcedWeaponIdList_";

        private Dictionary<Pawn, List<string>> forcedSidearmDefs = new Dictionary<Pawn, List<string>>();
        private Dictionary<Pawn, List<int>> forcedWeaponIds = new Dictionary<Pawn, List<int>>();

        [Unsaved(false)] private List<string> sidearmPawnIdsBuffer;
        [Unsaved(false)] private List<List<string>> sidearmDefBuffer;
        [Unsaved(false)] private List<string> weaponIdPawnIdsBuffer;
        [Unsaved(false)] private List<List<int>> weaponIdBuffer;

        public AutoArmGameComponent(Game game) : base()
        {
        }

        public override void ExposeData()
        {
            base.ExposeData();

            if (Scribe.mode == LoadSaveMode.LoadingVars || Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                AutoArmLogger.ReinitializeIfNeeded();
            }

            if (Scribe.mode == LoadSaveMode.Saving)
            {
                PrepareDataForSaving();
                BuildSerializationBuffers();
            }

            try
            {
                Scribe_Collections.Look(ref sidearmPawnIdsBuffer, SIDEARM_PAWN_IDS_KEY, LookMode.Value);
                Scribe_Collections.Look(ref weaponIdPawnIdsBuffer, WEAPON_ID_PAWN_IDS_KEY, LookMode.Value);

                if (Scribe.mode == LoadSaveMode.Saving)
                {
                    int sidearmCount = sidearmDefBuffer?.Count ?? 0;
                    Scribe_Values.Look(ref sidearmCount, SIDEARM_COUNT_KEY, 0);

                    if (sidearmDefBuffer != null)
                    {
                        for (int i = 0; i < sidearmDefBuffer.Count; i++)
                        {
                            var defList = sidearmDefBuffer[i];
                            Scribe_Collections.Look(ref defList, $"{SIDEARM_LIST_PREFIX}{i}", LookMode.Value);
                        }
                    }

                    int weaponIdCount = weaponIdBuffer?.Count ?? 0;
                    Scribe_Values.Look(ref weaponIdCount, WEAPON_ID_COUNT_KEY, 0);

                    if (weaponIdBuffer != null)
                    {
                        for (int i = 0; i < weaponIdBuffer.Count; i++)
                        {
                            var idList = weaponIdBuffer[i];
                            Scribe_Collections.Look(ref idList, $"{WEAPON_ID_LIST_PREFIX}{i}", LookMode.Value);
                        }
                    }
                }
                else if (Scribe.mode == LoadSaveMode.LoadingVars)
                {
                    int sidearmCount = 0;
                    Scribe_Values.Look(ref sidearmCount, SIDEARM_COUNT_KEY, 0);
                    sidearmDefBuffer = sidearmCount > 0 ? new List<List<string>>(sidearmCount) : new List<List<string>>();

                    for (int i = 0; i < sidearmCount; i++)
                    {
                        List<string> defList = null;
                        Scribe_Collections.Look(ref defList, $"{SIDEARM_LIST_PREFIX}{i}", LookMode.Value);
                        sidearmDefBuffer.Add(defList ?? new List<string>());
                    }

                    int weaponIdCount = 0;
                    Scribe_Values.Look(ref weaponIdCount, WEAPON_ID_COUNT_KEY, 0);
                    weaponIdBuffer = weaponIdCount > 0 ? new List<List<int>>(weaponIdCount) : new List<List<int>>();

                    for (int i = 0; i < weaponIdCount; i++)
                    {
                        List<int> idList = null;
                        Scribe_Collections.Look(ref idList, $"{WEAPON_ID_LIST_PREFIX}{i}", LookMode.Value);
                        weaponIdBuffer.Add(idList ?? new List<int>());
                    }
                }
            }
            catch (Exception ex)
            {
                AutoArmLogger.Error("Error loading forced weapon data from save", ex);
                forcedSidearmDefs = new Dictionary<Pawn, List<string>>();
                forcedWeaponIds = new Dictionary<Pawn, List<int>>();
                sidearmPawnIdsBuffer = null;
                sidearmDefBuffer = null;
                weaponIdPawnIdsBuffer = null;
                weaponIdBuffer = null;
            }

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                ReconstructDictionariesFromBuffers();
                CleanupLegacyData();
                pendingRestoreAfterLoad = true;
            }
        }

        [Unsaved(false)] private bool pendingRestoreAfterLoad;


        private void PrepareDataForSaving()
        {
            forcedSidearmDefs.Clear();
            forcedWeaponIds.Clear();


            var sidearmData = ForcedWeapons.GetSidearmSaveData();
            foreach (var kvp in sidearmData)
            {
                if (IsPawnValidForPersistence(kvp.Key) && kvp.Value != null && kvp.Value.Count > 0)
                {
                    var defNames = new List<string>();
                    foreach (var def in kvp.Value)
                    {
                        if (def != null && !string.IsNullOrEmpty(def.defName))
                        {
                            defNames.Add(def.defName);
                        }
                    }

                    if (defNames.Count > 0)
                    {
                        forcedSidearmDefs[kvp.Key] = defNames;
                    }
                }
            }

            var weaponIdData = ForcedWeapons.GetForcedWeaponIds();
            foreach (var kvp in weaponIdData)
            {
                if (IsPawnValidForPersistence(kvp.Key) && kvp.Value != null && kvp.Value.Count > 0)
                {
                    var sanitizedIds = kvp.Value.Where(id => id != 0).ToList();
                    if (sanitizedIds.Count > 0)
                    {
                        forcedWeaponIds[kvp.Key] = sanitizedIds;
                    }
                }
            }


            RemoveInvalidEntries(forcedSidearmDefs,
                kvp => kvp.Value == null || kvp.Value.Count == 0);

            RemoveInvalidEntries(forcedWeaponIds,
                kvp => kvp.Value == null || kvp.Value.Count == 0);

            AutoArmLogger.Debug(() => $"Saving forced weapon data: {forcedSidearmDefs.Count} weapon defs, {forcedWeaponIds.Count} weapon IDs");
        }

        private void BuildSerializationBuffers()
        {
            sidearmPawnIdsBuffer = new List<string>();
            sidearmDefBuffer = new List<List<string>>();

            if (forcedSidearmDefs != null)
            {
                foreach (var kvp in forcedSidearmDefs)
                {
                    var pawn = kvp.Key;
                    if (!IsPawnValidForPersistence(pawn) || kvp.Value == null || kvp.Value.Count == 0)
                    {
                        continue;
                    }

                    var loadId = pawn.GetUniqueLoadID();
                    if (string.IsNullOrEmpty(loadId))
                    {
                        continue;
                    }

                    var sanitized = kvp.Value.Where(defName => !string.IsNullOrEmpty(defName)).Distinct().ToList();
                    if (sanitized.Count == 0)
                    {
                        continue;
                    }

                    sidearmPawnIdsBuffer.Add(loadId);
                    sidearmDefBuffer.Add(sanitized);
                }
            }

            weaponIdPawnIdsBuffer = new List<string>();
            weaponIdBuffer = new List<List<int>>();

            if (forcedWeaponIds != null)
            {
                foreach (var kvp in forcedWeaponIds)
                {
                    var pawn = kvp.Key;
                    if (!IsPawnValidForPersistence(pawn) || kvp.Value == null || kvp.Value.Count == 0)
                    {
                        continue;
                    }

                    var loadId = pawn.GetUniqueLoadID();
                    if (string.IsNullOrEmpty(loadId))
                    {
                        continue;
                    }

                    var filteredIds = kvp.Value.Where(id => id != 0).Distinct().ToList();
                    if (filteredIds.Count == 0)
                    {
                        continue;
                    }

                    weaponIdPawnIdsBuffer.Add(loadId);
                    weaponIdBuffer.Add(filteredIds);
                }
            }
        }

        private void ReconstructDictionariesFromBuffers()
        {
            forcedSidearmDefs.Clear();
            forcedWeaponIds.Clear();

            var byLoadId = BuildPawnLoadIdLookup();

            if (sidearmPawnIdsBuffer != null && sidearmDefBuffer != null)
            {
                for (int i = 0; i < Math.Min(sidearmPawnIdsBuffer.Count, sidearmDefBuffer.Count); i++)
                {
                    Pawn pawn = null;
                    var loadId = sidearmPawnIdsBuffer[i];
                    if (!string.IsNullOrEmpty(loadId))
                        byLoadId.TryGetValue(loadId, out pawn);

                    if (!IsPawnValidForPersistence(pawn))
                    {
                        continue;
                    }

                    var defList = sidearmDefBuffer[i];
                    if (defList == null || defList.Count == 0)
                    {
                        continue;
                    }

                    var sanitized = defList.Where(defName => !string.IsNullOrEmpty(defName)).Distinct().ToList();
                    if (sanitized.Count == 0)
                    {
                        continue;
                    }

                    forcedSidearmDefs[pawn] = sanitized;
                }
            }

            if (weaponIdPawnIdsBuffer != null && weaponIdBuffer != null)
            {
                for (int i = 0; i < Math.Min(weaponIdPawnIdsBuffer.Count, weaponIdBuffer.Count); i++)
                {
                    Pawn pawn = null;
                    var loadId = weaponIdPawnIdsBuffer[i];
                    if (!string.IsNullOrEmpty(loadId))
                        byLoadId.TryGetValue(loadId, out pawn);

                    if (!IsPawnValidForPersistence(pawn))
                    {
                        continue;
                    }

                    var idList = weaponIdBuffer[i];
                    if (idList == null || idList.Count == 0)
                    {
                        continue;
                    }

                    var sanitized = idList.Where(id => id != 0).Distinct().ToList();
                    if (sanitized.Count == 0)
                    {
                        continue;
                    }

                    forcedWeaponIds[pawn] = sanitized;
                }
            }

            sidearmPawnIdsBuffer = null;
            sidearmDefBuffer = null;
            weaponIdPawnIdsBuffer = null;
            weaponIdBuffer = null;
        }

        private static bool IsPawnValidForPersistence(Pawn pawn)
        {
            if (pawn == null)
            {
                return false;
            }

            if (pawn.Discarded || pawn.Destroyed || pawn.Dead)
            {
                return false;
            }

            return true;
        }

        private static Dictionary<string, Pawn> BuildPawnLoadIdLookup()
        {
            var map = new Dictionary<string, Pawn>(256);

            if (Current.Game?.Maps != null)
            {
                foreach (var gameMap in Current.Game.Maps)
                {
                    if (gameMap?.mapPawns == null) continue;
                    foreach (var pawn in gameMap.mapPawns.AllPawns)
                    {
                        if (pawn == null) continue;
                        map[pawn.GetUniqueLoadID()] = pawn;
                    }
                }
            }

            if (Find.WorldPawns != null)
            {
                foreach (var pawn in Find.WorldPawns.AllPawnsAliveOrDead)
                {
                    if (pawn == null) continue;
                    var id = pawn.GetUniqueLoadID();
                    if (!map.ContainsKey(id))
                        map[id] = pawn;
                }
            }

            if (Find.WorldObjects != null)
            {
                foreach (var caravan in Find.WorldObjects.Caravans)
                {
                    if (caravan?.PawnsListForReading == null) continue;
                    foreach (var pawn in caravan.PawnsListForReading)
                    {
                        if (pawn == null) continue;
                        var id = pawn.GetUniqueLoadID();
                        if (!map.ContainsKey(id))
                            map[id] = pawn;
                    }
                }
            }

            return map;
        }

        private static bool HasDef(Pawn pawn, ThingDef def)
        {
            if (pawn == null || def == null)
            {
                return false;
            }

            if (pawn.equipment?.Primary?.def == def)
            {
                return true;
            }

            var inventory = pawn.inventory?.innerContainer;
            if (inventory != null)
            {
                for (int i = 0; i < inventory.Count; i++)
                {
                    if (inventory[i] is ThingWithComps weapon && weapon.def == def)
                    {
                        return true;
                    }
                }
            }

            var carried = pawn.carryTracker?.CarriedThing as ThingWithComps;
            if (carried != null && carried.def == def)
            {
                return true;
            }

            return false;
        }


        private void RestoreDataAfterLoading()
        {
            ForcedWeapons.Reset();

            var validPawns = new HashSet<Pawn>();
            if (Current.Game?.Maps != null)
            {
                foreach (var map in Current.Game.Maps)
                {
                    if (map?.mapPawns != null)
                    {
                        validPawns.UnionWith(map.mapPawns.AllPawns.Where(p => p != null && !p.Destroyed));
                    }
                }
            }

            if (Find.WorldObjects != null)
            {
                foreach (var caravan in Find.WorldObjects.Caravans)
                {
                    if (caravan?.PawnsListForReading != null)
                    {
                        validPawns.UnionWith(caravan.PawnsListForReading.Where(p => p != null && !p.Destroyed));
                    }
                }
            }

            if (Find.WorldPawns != null)
            {
                validPawns.UnionWith(Find.WorldPawns.AllPawnsAliveOrDead.Where(p => p != null && !p.Destroyed));
            }

            var sidearmDataToRestore = new Dictionary<Pawn, HashSet<ThingDef>>();

            if (forcedSidearmDefs != null)
            {
                foreach (var kvp in forcedSidearmDefs)
                {
                    if (kvp.Key == null || kvp.Value == null || kvp.Value.Count == 0)
                        continue;

                    if (!validPawns.Contains(kvp.Key))
                    {
                        if (Prefs.DevMode)
                            AutoArmLogger.Debug(() => $"Skipping forced sidearms for missing/invalid pawn: {kvp.Key?.Name?.ToStringShort ?? kvp.Key?.LabelShort ?? "null"}");
                        continue;
                    }

                    var defs = new HashSet<ThingDef>();
                    foreach (var defName in kvp.Value)
                    {
                        if (string.IsNullOrEmpty(defName))
                        {
                            continue;
                        }

                        var def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
                        if (def != null)
                        {
                            if (HasDef(kvp.Key, def))
                            {
                                defs.Add(def);
                            }
                        }
                        else if (Prefs.DevMode)
                        {
                            AutoArmLogger.Debug(() => $"Could not find sidearm def '{defName}' when loading save");
                        }
                    }
                    if (defs.Count > 0)
                    {
                        sidearmDataToRestore[kvp.Key] = defs;
                    }
                }
            }

            ForcedWeapons.LoadSidearmSaveData(sidearmDataToRestore);

            if (forcedWeaponIds != null && forcedWeaponIds.Count > 0)
            {
                var validatedWeaponIds = new Dictionary<Pawn, List<int>>();

                foreach (var kvp in forcedWeaponIds)
                {
                    if (kvp.Key == null || !validPawns.Contains(kvp.Key))
                        continue;

                    var validIds = new List<int>();
                    foreach (var weaponId in kvp.Value)
                    {
                        bool weaponExists = false;

                        if (kvp.Key.equipment?.Primary?.thingIDNumber == weaponId)
                            weaponExists = true;
                        else if (kvp.Key.inventory?.innerContainer != null)
                        {
                            foreach (var thing in kvp.Key.inventory.innerContainer)
                            {
                                if (thing is ThingWithComps w && w.thingIDNumber == weaponId)
                                {
                                    weaponExists = true;
                                    break;
                                }
                            }
                        }

                        if (!weaponExists)
                        {
                            var carried = kvp.Key.carryTracker?.CarriedThing as ThingWithComps;
                            if (carried != null && carried.thingIDNumber == weaponId)
                            {
                                weaponExists = true;
                            }
                        }

                        if (weaponExists)
                        {
                            validIds.Add(weaponId);
                        }
                        else if (Prefs.DevMode)
                        {
                            AutoArmLogger.Debug(() => $"Weapon ID {weaponId} no longer exists for {kvp.Key.Name?.ToStringShort ?? kvp.Key.LabelShort}");
                        }
                    }

                    if (validIds.Count > 0)
                    {
                        validatedWeaponIds[kvp.Key] = validIds;
                    }
                }

                ForcedWeapons.LoadForcedWeaponIds(validatedWeaponIds);

                foreach (var kvp in validatedWeaponIds)
                {
                    var pawn = kvp.Key;
                    if (pawn == null || pawn.Destroyed || pawn.Dead)
                        continue;

                    var primary = pawn.equipment?.Primary;
                    if (primary != null && kvp.Value != null && kvp.Value.Contains(primary.thingIDNumber))
                    {
                        ForcedWeapons.SetForced(pawn, primary);
                    }
                }
            }

            if (AutoArmMod.settings?.debugLogging == true)
            {
                int weaponIdCount = 0;
                if (forcedWeaponIds != null)
                {
                    foreach (var list in forcedWeaponIds.Values)
                    {
                        weaponIdCount += list?.Count ?? 0;
                    }
                }
                AutoArmLogger.Debug(() => $"Loaded forced weapon data: {sidearmDataToRestore.Count} sidearm entries, {weaponIdCount} weapon IDs");
            }

            forcedSidearmDefs?.Clear();
            forcedWeaponIds?.Clear();
        }

        public override void StartedNewGame()
        {
            base.StartedNewGame();

            // Clear scheduler
            TickScheduler.Reset();

            CooldownMetrics.Reset();
            DroppedItems.Reset();
            AutoArm.Blacklist.Reset();
            ForcedWeapons.Reset();
            ForcedWeaponState.Reset();
            AutoArm.Jobs.AutoEquipState.Reset();

            Scoring.ResetSkillCache();

            // Built in FinalizeInit
            foreach (var map in Find.Maps)
            {
                var cacheManager = map?.GetComponent<Caching.WeaponCache.AutoArmWeaponMapComponent>();
                cacheManager?.Reset();
            }

            if (Compatibility.SimpleSidearmsCompat.IsLoaded)
            {
                Compatibility.SimpleSidearmsCompat.Reset();
            }

            try
            {
                ThingFilter_Allows_Thing_Patch.DisableForDialog();
                ThingFilter_Allows_Thing_Patch.InvalidateCache();
                OutfitFilterCache.RebuildCache();
                EquipEligibility.Clear();
            }
            catch (Exception e)
            {
                AutoArmLogger.ErrorPatch(e, "StartedNewGame.UIAndCaches");
            }

            AutoArmLogger.Debug(() => "GameComponent initialized for new game (all event trackers reset)");

            LongEventHandler.ExecuteWhenFinished(WarmupCaches);
        }

        public override void LoadedGame()
        {
            base.LoadedGame();

            AutoArmLogger.ReinitializeIfNeeded();

            AutoArmLogger.Debug(() => "GameComponent loaded from save");

            if (pendingRestoreAfterLoad)
            {
                pendingRestoreAfterLoad = false;
                try { RestoreDataAfterLoading(); }
                catch (Exception e) { AutoArmLogger.Error("RestoreDataAfterLoading failed", e); }
            }

            TickScheduler.Reset();

            CooldownMetrics.Reset();
            DroppedItems.Reset();
            Blacklist.Reset();
            ForcedWeaponState.Reset();
            AutoArm.Jobs.AutoEquipState.Reset();
            Scoring.ResetSkillCache();

            foreach (var map in Find.Maps)
            {
                var cacheManager = map?.GetComponent<Caching.WeaponCache.AutoArmWeaponMapComponent>();
                cacheManager?.RebuildCachedWeaponSet();
            }

            try
            {
                Game_LoadGame_InjectThinkTree_Patch.Postfix();
                ThingFilter_Allows_Thing_Patch.DisableForDialog();
                ThingFilter_Allows_Thing_Patch.InvalidateCache();
                PawnValidation.ClearCache();
                EquipEligibility.Clear();
                OutfitFilterCache.RebuildCache();
            }
            catch (Exception e)
            {
                AutoArmLogger.ErrorPatch(e, "LoadedGame.UIAndCaches");
            }

            if (AutoArmMod.settings?.modEnabled == true &&
                AutoArmMod.settings?.respectWeaponBonds == true &&
                ModsConfig.RoyaltyActive)
            {
                LongEventHandler.ExecuteWhenFinished(() =>
                {
                    AutoArmMod.MarkAllBondedWeaponsAsForcedOnLoad();
                });
            }

            LongEventHandler.ExecuteWhenFinished(WarmupCaches);
        }

        private void WarmupCaches()
        {
            var maps = Find.Maps;
            if (maps == null || maps.Count == 0)
                return;

            // Warm SimpleSidearms
            bool ssLoaded = Compatibility.SimpleSidearmsCompat.IsLoaded;
            if (ssLoaded)
            {
                Compatibility.SimpleSidearmsCompat.EnsureInitialized();
            }

            int colonistCount = 0;
            int weaponScores = 0;

            foreach (var map in maps)
            {
                if (map?.mapPawns?.FreeColonistsSpawned == null)
                    continue;

                // Get all weapons
                var weapons = Caching.WeaponCache.GetAllWeapons(map);
                var weaponList = new List<ThingWithComps>();
                foreach (var w in weapons)
                    weaponList.Add(w);

                // Snapshot colonists
                var colonists = new List<Pawn>(map.mapPawns.FreeColonistsSpawned);
                foreach (var pawn in colonists)
                {
                    if (pawn == null || pawn.Dead || pawn.Downed)
                        continue;

                    // Warm validation cache
                    Caching.PawnValidation.CanConsiderWeapons(pawn);

                    // Warm skill cache
                    if (pawn.skills?.skills != null)
                    {
                        Caching.WeaponCache.PreWarmColonistScore(pawn, true);
                        Caching.WeaponCache.PreWarmColonistScore(pawn, false);
                    }

                    // Pre-warm scores
                    foreach (var weapon in weaponList)
                    {
                        if (weapon != null && !weapon.Destroyed)
                        {
                            // Warm score cache
                            Scoring.GetTotalScore(pawn, weapon);

                            // Warm eligibility
                            Caching.EquipEligibility.CanEquip(pawn, weapon, out _);

                            // Warm SS cache
                            if (ssLoaded)
                            {
                                Compatibility.SimpleSidearmsCompat.CanPickupSidearm(weapon, pawn, out _);
                            }

                            weaponScores++;
                        }
                    }

                    colonistCount++;
                }
            }

            if (AutoArmMod.settings?.debugLogging == true && colonistCount > 0)
            {
                AutoArmLogger.Debug(() => $"Pre-warmed caches: {colonistCount} colonists, {weaponScores} weapon scores");
            }

            // Start grace period
            Cleanup.OnWarmupCompleted();
        }

        public override void GameComponentTick()
        {
            base.GameComponentTick();

            int currentTick = Find.TickManager.TicksGame;

            TickScheduler.ProcessTick(currentTick);

            if (currentTick % 12000 == 0)
            {
                CooldownMetrics.CorrectDrift(out int eventCount, out int actualCount);
            }

            if (currentTick % 61 == 0)
            {
                Cleanup.PerformStaggeredCleanup();
            }

            if (currentTick % 3600 == 0)
            {
                AutoArmLogger.Flush();
            }

            if (currentTick % 60000 == 0 && AutoArmMod.settings?.modEnabled == true)
            {
                try { Caching.WeaponCache.CleanupDestroyedMaps(); }
                catch (Exception e) { AutoArmLogger.WarnCleanup(e, "GameComponentTick.CleanupDestroyedMaps"); }
            }
        }


        private void CleanupLegacyData()
        {
            int totalCleaned = 0;

            if (forcedSidearmDefs != null)
            {
                foreach (var kvp in forcedSidearmDefs.ToList())
                {
                    if (kvp.Value != null)
                    {
                        kvp.Value.RemoveAll(s => string.IsNullOrEmpty(s));
                    }
                }

                totalCleaned += RemoveInvalidEntries(forcedSidearmDefs,
                    kvp => kvp.Key == null || kvp.Value == null || kvp.Value.Count == 0);
            }

            if (totalCleaned > 0 && Prefs.DevMode)
            {
                AutoArmLogger.Debug(() => $"Cleaned up {totalCleaned} invalid entries from legacy save data");
            }
        }


        private static int RemoveInvalidEntries<TKey, TValue>(Dictionary<TKey, TValue> dictionary,
            Func<KeyValuePair<TKey, TValue>, bool> isInvalid)
        {
            if (dictionary == null)
                return 0;

            var keysToRemove = new List<TKey>();
            foreach (var kvp in dictionary)
            {
                if (isInvalid(kvp))
                {
                    keysToRemove.Add(kvp.Key);
                }
            }

            foreach (var key in keysToRemove)
            {
                dictionary.Remove(key);
            }

            return keysToRemove.Count;
        }
    }
}
