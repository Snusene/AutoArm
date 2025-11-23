
using AutoArm.Helpers;
using AutoArm.Jobs;
using AutoArm.Logging;
using AutoArm.Weapons;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace AutoArm
{
    /// <summary>
    /// GameComponent to handle saving and loading AutoArm data
    /// Also handles periodic metrics aggregation for performance overlay
    /// </summary>
    public class AutoArmGameComponent : GameComponent
    {
        private const string PRIMARY_PAWNS_KEY = "forcedPrimaryWeaponPawns";

        private const string PRIMARY_PAWN_IDS_KEY = "forcedPrimaryWeaponPawnIds";

        private const string PRIMARY_DEFS_KEY = "forcedPrimaryWeaponDefNames";
        private const string SIDEARM_PAWNS_KEY = "forcedSidearmPawns";
        private const string SIDEARM_PAWN_IDS_KEY = "forcedSidearmPawnIds";
        private const string SIDEARM_COUNT_KEY = "forcedSidearmDefListCount";
        private const string SIDEARM_LIST_PREFIX = "forcedSidearmDefList_";

        private const string WEAPON_ID_PAWNS_KEY = "forcedWeaponIdPawns";

        private const string WEAPON_ID_PAWN_IDS_KEY = "forcedWeaponIdPawnIds";

        private const string WEAPON_ID_COUNT_KEY = "forcedWeaponIdListCount";
        private const string WEAPON_ID_LIST_PREFIX = "forcedWeaponIdList_";
        private const string SAVE_FORMAT_FLAG_KEY = "forcedWeaponUseNewFormat";

        private const string LEGACY_PRIMARY_KEYS = "forcedPrimaryWeapons/keys";

        private const string LEGACY_PRIMARY_VALUES = "forcedPrimaryWeapons/values";
        private const string LEGACY_SIDEARM_KEYS = "forcedSidearmDefs/keys";
        private const string LEGACY_SIDEARM_VALUES = "forcedSidearmDefs/values";

        private Dictionary<Pawn, string> forcedPrimaryWeaponDefs = new Dictionary<Pawn, string>();

        private Dictionary<Pawn, List<string>> forcedSidearmDefs = new Dictionary<Pawn, List<string>>();
        private Dictionary<Pawn, List<int>> forcedWeaponIds = new Dictionary<Pawn, List<int>>();

        [Unsaved(false)] private List<string> primaryPawnIdsBuffer;

        [Unsaved(false)] private List<string> primaryDefBuffer;
        [Unsaved(false)] private List<string> sidearmPawnIdsBuffer;
        [Unsaved(false)] private List<List<string>> sidearmDefBuffer;
        [Unsaved(false)] private List<string> weaponIdPawnIdsBuffer;
        [Unsaved(false)] private List<List<int>> weaponIdBuffer;
        [Unsaved(false)] private bool useNewSerializationFormat;

        public AutoArmGameComponent(Game game) : base()
        {
        }

        /// <summary>
        /// Active cooldown count (O(1) query)
        /// </summary>
        public static int GetActiveCooldowns() => CooldownMetrics.GetActiveCooldowns();

        /// <summary>
        /// Save/load
        /// </summary>
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

            if (forcedPrimaryWeaponDefs == null)
            {
                forcedPrimaryWeaponDefs = new Dictionary<Pawn, string>();
            }

            if (forcedSidearmDefs == null)
            {
                forcedSidearmDefs = new Dictionary<Pawn, List<string>>();
            }

            if (forcedWeaponIds == null)
            {
                forcedWeaponIds = new Dictionary<Pawn, List<int>>();
            }

            bool formatFlag = useNewSerializationFormat;
            Scribe_Values.Look(ref formatFlag, SAVE_FORMAT_FLAG_KEY, false);

            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                useNewSerializationFormat = formatFlag;
            }
            else if (Scribe.mode == LoadSaveMode.Saving)
            {
                useNewSerializationFormat = true;
            }

            try
            {
                if (useNewSerializationFormat || Scribe.mode == LoadSaveMode.Saving)
                {
                    Scribe_Collections.Look(ref primaryPawnIdsBuffer, PRIMARY_PAWN_IDS_KEY, LookMode.Value);
                    Scribe_Collections.Look(ref primaryDefBuffer, PRIMARY_DEFS_KEY, LookMode.Value);
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

                if (Scribe.mode == LoadSaveMode.LoadingVars && !useNewSerializationFormat)
                {
                    LoadLegacyForcedData();
                }
            }
            catch (Exception ex)
            {
                AutoArmLogger.Error("Error loading forced weapon data from save", ex);
                forcedPrimaryWeaponDefs = new Dictionary<Pawn, string>();
                forcedSidearmDefs = new Dictionary<Pawn, List<string>>();
                forcedWeaponIds = new Dictionary<Pawn, List<int>>();
                primaryPawnIdsBuffer = null;
                primaryDefBuffer = null;
                sidearmPawnIdsBuffer = null;
                sidearmDefBuffer = null;
                weaponIdPawnIdsBuffer = null;
                weaponIdBuffer = null;
            }

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (useNewSerializationFormat)
                {
                    ReconstructDictionariesFromBuffers();
                }

                CleanupLegacyData();
                ValidateRestoredWeaponIds();
                RestoreDataAfterLoading();
            }
        }


        private void PrepareDataForSaving()
        {
            forcedPrimaryWeaponDefs.Clear();
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
            useNewSerializationFormat = true;

            primaryPawnIdsBuffer = new List<string>();
            primaryDefBuffer = new List<string>();

            if (forcedPrimaryWeaponDefs != null)
            {
                foreach (var kvp in forcedPrimaryWeaponDefs)
                {
                    var pawn = kvp.Key;
                    if (!IsPawnValidForPersistence(pawn) || string.IsNullOrEmpty(kvp.Value))
                    {
                        continue;
                    }

                    var loadId = pawn.GetUniqueLoadID();
                    if (string.IsNullOrEmpty(loadId))
                    {
                        continue;
                    }

                    primaryPawnIdsBuffer.Add(loadId);
                    primaryDefBuffer.Add(kvp.Value);
                }
            }

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
            forcedPrimaryWeaponDefs.Clear();
            forcedSidearmDefs.Clear();
            forcedWeaponIds.Clear();

            if (primaryPawnIdsBuffer != null && primaryDefBuffer != null)
            {
                for (int i = 0; i < Math.Min(primaryPawnIdsBuffer.Count, primaryDefBuffer.Count); i++)
                {
                    var pawn = ResolvePawnByLoadId(primaryPawnIdsBuffer[i]);
                    if (!IsPawnValidForPersistence(pawn))
                    {
                        continue;
                    }

                    var defName = primaryDefBuffer[i];
                    if (string.IsNullOrEmpty(defName))
                    {
                        continue;
                    }

                    forcedPrimaryWeaponDefs[pawn] = defName;
                }
            }

            if (sidearmPawnIdsBuffer != null && sidearmDefBuffer != null)
            {
                for (int i = 0; i < Math.Min(sidearmPawnIdsBuffer.Count, sidearmDefBuffer.Count); i++)
                {
                    var pawn = ResolvePawnByLoadId(sidearmPawnIdsBuffer[i]);
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
                    var pawn = ResolvePawnByLoadId(weaponIdPawnIdsBuffer[i]);
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

            primaryPawnIdsBuffer = null;
            primaryDefBuffer = null;
            sidearmPawnIdsBuffer = null;
            sidearmDefBuffer = null;
            weaponIdPawnIdsBuffer = null;
            weaponIdBuffer = null;
        }

        private void LoadLegacyForcedData()
        {
            List<Pawn> legacyPrimaryKeys = null;
            List<string> legacyPrimaryValues = null;
            List<Pawn> legacySidearmKeys = null;
            List<List<string>> legacySidearmValues = null;
            List<Pawn> legacyWeaponIdKeys = null;
            List<List<int>> legacyWeaponIdValues = null;

            List<Pawn> listPrimaryPawns = null;
            List<string> listPrimaryDefs = null;
            List<Pawn> listSidearmPawns = null;
            List<List<string>> listSidearmDefLists = null;

            try
            {
                Scribe_Collections.Look(ref legacyPrimaryKeys, LEGACY_PRIMARY_KEYS, LookMode.Reference);
                Scribe_Collections.Look(ref legacyPrimaryValues, LEGACY_PRIMARY_VALUES, LookMode.Value);
                Scribe_Collections.Look(ref legacySidearmKeys, LEGACY_SIDEARM_KEYS, LookMode.Reference);

                try
                {
                    Scribe_Collections.Look(ref legacySidearmValues, LEGACY_SIDEARM_VALUES, LookMode.Deep);
                }
                catch (Exception)
                {
                    try
                    {
                        AutoArmLogger.Warn("Skipping invalid legacy sidearm data in save file");
                    }
                    catch
                    {
                    }
                }

                Scribe_Collections.Look(ref legacyWeaponIdKeys, WEAPON_ID_PAWNS_KEY, LookMode.Reference);

                int legacyWeaponIdCount = 0;
                Scribe_Values.Look(ref legacyWeaponIdCount, WEAPON_ID_COUNT_KEY, 0);
                if (legacyWeaponIdCount > 0)
                {
                    legacyWeaponIdValues = new List<List<int>>(legacyWeaponIdCount);
                    for (int i = 0; i < legacyWeaponIdCount; i++)
                    {
                        List<int> idList = null;
                        Scribe_Collections.Look(ref idList, $"{WEAPON_ID_LIST_PREFIX}{i}", LookMode.Value);
                        legacyWeaponIdValues.Add(idList ?? new List<int>());
                    }
                }

                forcedPrimaryWeaponDefs.Clear();
                if (legacyPrimaryKeys != null && legacyPrimaryValues != null)
                {
                    for (int i = 0; i < Math.Min(legacyPrimaryKeys.Count, legacyPrimaryValues.Count); i++)
                    {
                        var pawn = legacyPrimaryKeys[i];
                        if (!IsPawnValidForPersistence(pawn))
                        {
                            continue;
                        }

                        var defName = legacyPrimaryValues[i];
                        if (string.IsNullOrEmpty(defName))
                        {
                            continue;
                        }

                        forcedPrimaryWeaponDefs[pawn] = defName;
                    }
                }

                forcedSidearmDefs.Clear();
                if (legacySidearmKeys != null && legacySidearmValues != null)
                {
                    for (int i = 0; i < Math.Min(legacySidearmKeys.Count, legacySidearmValues.Count); i++)
                    {
                        var pawn = legacySidearmKeys[i];
                        if (!IsPawnValidForPersistence(pawn))
                        {
                            continue;
                        }

                        var defs = legacySidearmValues[i];
                        if (defs == null || defs.Count == 0)
                        {
                            continue;
                        }

                        var sanitized = defs.Where(defName => !string.IsNullOrEmpty(defName)).Distinct().ToList();
                        if (sanitized.Count == 0)
                        {
                            continue;
                        }

                        forcedSidearmDefs[pawn] = sanitized;
                    }
                }

                Scribe_Collections.Look(ref listPrimaryPawns, PRIMARY_PAWNS_KEY, LookMode.Reference);
                Scribe_Collections.Look(ref listPrimaryDefs, PRIMARY_DEFS_KEY, LookMode.Value);
                Scribe_Collections.Look(ref listSidearmPawns, SIDEARM_PAWNS_KEY, LookMode.Reference);

                int sidearmCountRef = 0;
                Scribe_Values.Look(ref sidearmCountRef, SIDEARM_COUNT_KEY, 0);
                if (sidearmCountRef > 0)
                {
                    listSidearmDefLists = new List<List<string>>(sidearmCountRef);
                    for (int i = 0; i < sidearmCountRef; i++)
                    {
                        List<string> defList = null;
                        Scribe_Collections.Look(ref defList, $"{SIDEARM_LIST_PREFIX}{i}", LookMode.Value);
                        listSidearmDefLists.Add(defList ?? new List<string>());
                    }
                }

                if (listPrimaryPawns != null && listPrimaryDefs != null)
                {
                    for (int i = 0; i < Math.Min(listPrimaryPawns.Count, listPrimaryDefs.Count); i++)
                    {
                        var pawn = listPrimaryPawns[i];
                        var defName = listPrimaryDefs[i];
                        if (!IsPawnValidForPersistence(pawn) || string.IsNullOrEmpty(defName))
                            continue;

                        if (!forcedPrimaryWeaponDefs.ContainsKey(pawn))
                        {
                            forcedPrimaryWeaponDefs[pawn] = defName;
                        }
                    }
                }

                if (listSidearmPawns != null && listSidearmDefLists != null)
                {
                    for (int i = 0; i < Math.Min(listSidearmPawns.Count, listSidearmDefLists.Count); i++)
                    {
                        var pawn = listSidearmPawns[i];
                        var defs = listSidearmDefLists[i];
                        if (!IsPawnValidForPersistence(pawn) || defs == null || defs.Count == 0)
                            continue;

                        var sanitized = defs.Where(d => !string.IsNullOrEmpty(d)).Distinct().ToList();
                        if (sanitized.Count == 0)
                            continue;

                        if (forcedSidearmDefs.ContainsKey(pawn))
                        {
                            var merged = new HashSet<string>(forcedSidearmDefs[pawn]);
                            for (int j = 0; j < sanitized.Count; j++)
                                merged.Add(sanitized[j]);
                            forcedSidearmDefs[pawn] = merged.ToList();
                        }
                        else
                        {
                            forcedSidearmDefs[pawn] = sanitized;
                        }
                    }
                }

                forcedWeaponIds.Clear();
                if (legacyWeaponIdKeys != null && legacyWeaponIdValues != null)
                {
                    for (int i = 0; i < Math.Min(legacyWeaponIdKeys.Count, legacyWeaponIdValues.Count); i++)
                    {
                        var pawn = legacyWeaponIdKeys[i];
                        if (!IsPawnValidForPersistence(pawn))
                        {
                            continue;
                        }

                        var ids = legacyWeaponIdValues[i];
                        if (ids == null || ids.Count == 0)
                        {
                            continue;
                        }

                        var sanitized = ids.Where(id => id != 0).Distinct().ToList();
                        if (sanitized.Count == 0)
                        {
                            continue;
                        }

                        forcedWeaponIds[pawn] = sanitized;
                    }
                }
            }
            catch (Exception ex)
            {
                AutoArmLogger.Error("Error loading legacy forced weapon data", ex);
                forcedPrimaryWeaponDefs.Clear();
                forcedSidearmDefs.Clear();
                forcedWeaponIds.Clear();
            }
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

        private Pawn ResolvePawnByLoadId(string loadId)
        {
            if (string.IsNullOrEmpty(loadId))
            {
                return null;
            }

            if (Current.Game?.Maps != null)
            {
                foreach (var map in Current.Game.Maps)
                {
                    if (map?.mapPawns == null)
                    {
                        continue;
                    }

                    var pawn = map.mapPawns.AllPawns.FirstOrDefault(p => p != null && p.GetUniqueLoadID() == loadId);
                    if (pawn != null)
                    {
                        return pawn;
                    }
                }
            }

            if (Find.WorldPawns != null)
            {
                foreach (var pawn in Find.WorldPawns.AllPawnsAliveOrDead)
                {
                    if (pawn != null && pawn.GetUniqueLoadID() == loadId)
                    {
                        return pawn;
                    }
                }
            }

            if (Find.WorldObjects != null)
            {
                foreach (var caravan in Find.WorldObjects.Caravans)
                {
                    var pawn = caravan?.PawnsListForReading.FirstOrDefault(p => p != null && p.GetUniqueLoadID() == loadId);
                    if (pawn != null)
                    {
                        return pawn;
                    }
                }
            }

            return null;
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

            var primaryDataToRestore = new Dictionary<Pawn, ThingDef>();

            if (forcedPrimaryWeaponDefs != null)
            {
                foreach (var kvp in forcedPrimaryWeaponDefs)
                {
                    if (kvp.Key == null || string.IsNullOrEmpty(kvp.Value))
                        continue;

                    if (!validPawns.Contains(kvp.Key))
                    {
                        if (Prefs.DevMode)
                            AutoArmLogger.Debug(() => $"Skipping forced weapon for missing/invalid pawn: {kvp.Key?.Name?.ToStringShort ?? kvp.Key?.LabelShort ?? "null"}");
                        continue;
                    }

                    var def = DefDatabase<ThingDef>.GetNamedSilentFail(kvp.Value);
                    if (def != null && HasDef(kvp.Key, def))
                    {
                        primaryDataToRestore[kvp.Key] = def;
                    }
                    else
                    {
                        if (Prefs.DevMode)
                            AutoArmLogger.Debug(() => $"Could not find weapon def '{kvp.Value}' when loading save");
                    }
                }
            }

            ForcedWeapons.LoadSaveData(primaryDataToRestore);

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
                AutoArmLogger.Debug(() => $"Loaded forced weapon data: {primaryDataToRestore.Count} primary, {sidearmDataToRestore.Count} sidearm entries, {weaponIdCount} weapon IDs");
            }

            forcedPrimaryWeaponDefs?.Clear();
            forcedSidearmDefs?.Clear();
            forcedWeaponIds?.Clear();
        }

        /// <summary>
        /// Init new game
        /// </summary>
        public override void StartedNewGame()
        {
            base.StartedNewGame();

            CooldownMetrics.Reset();
            DroppedItemTracker.Reset();
            AutoArm.Weapons.WeaponBlacklist.Reset();
            ForcedWeaponState.Reset();

            WeaponScoringHelper.ResetSkillCache();

            foreach (var map in Find.Maps)
            {
                var jobGiverComponent = JobGiverMapComponent.GetComponent(map);
                jobGiverComponent?.ResetTempBlacklistSchedule();
            }

            if (Compatibility.SimpleSidearmsCompat.IsLoaded)
            {
                Compatibility.SimpleSidearmsCompat.Reset();
            }

            foreach (var map in Find.Maps)
            {
                var cacheManager = map?.GetComponent<Caching.WeaponCacheManager.AutoArmWeaponMapComponent>();
                if (cacheManager != null)
                {
                    cacheManager.Reset();
                }
            }

            AutoArmLogger.Debug(() => "GameComponent initialized for new game (all event trackers reset)");
        }

        /// <summary>
        /// Init from save
        /// </summary>
        public override void LoadedGame()
        {
            base.LoadedGame();

            AutoArmLogger.ReinitializeIfNeeded();

            AutoArmLogger.Debug(() => "GameComponent loaded from save");

            CooldownMetrics.RebuildFromPawnStates();
            DroppedItemTracker.RebuildFromExistingItems();
            AutoArm.Weapons.WeaponBlacklist.RebuildFromExistingBlacklists();
            ForcedWeaponState.RebuildFromExistingDrops();

            WeaponScoringHelper.RebuildSkillCacheSchedule();

            foreach (var map in Find.Maps)
            {
                var jobGiverComponent = JobGiverMapComponent.GetComponent(map);
                jobGiverComponent?.RebuildTempBlacklistSchedule();
            }

            foreach (var map in Find.Maps)
            {
                var cacheManager = map?.GetComponent<Caching.WeaponCacheManager.AutoArmWeaponMapComponent>();
                if (cacheManager != null)
                {
                    cacheManager.RebuildCachedWeaponSet();
                    cacheManager.RebuildReservationSchedule();
                }
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
        }

        /// <summary>
        /// Finalizing
        /// </summary>
        public override void FinalizeInit()
        {
            base.FinalizeInit();
        }

        /// <summary>
        /// Cleanup
        /// </summary>
        ~AutoArmGameComponent()
        {
        }

        /// <summary>
        /// Tick cleanup
        /// </summary>
        public override void GameComponentTick()
        {
            base.GameComponentTick();

            int currentTick = Find.TickManager.TicksGame;

            CooldownMetrics.OnCooldownsExpired(currentTick);

            DroppedItemTracker.ProcessExpiredItems(currentTick);

            AutoArm.Weapons.WeaponBlacklist.ProcessExpiredBlacklists(currentTick);

            ForcedWeaponState.ProcessGraceChecks(currentTick);

            foreach (var map in Find.Maps)
            {
                var cacheManager = map?.GetComponent<Caching.WeaponCacheManager.AutoArmWeaponMapComponent>();
                if (cacheManager != null)
                {
                    cacheManager.ProcessExpiredReservations(currentTick);
                }
            }

            WeaponScoringHelper.ProcessExpiredSkillCache(currentTick);

            foreach (var map in Find.Maps)
            {
                var jobGiverComponent = JobGiverMapComponent.GetComponent(map);
                jobGiverComponent?.ProcessExpiredTempBlacklists(currentTick);
            }

            if (Compatibility.SimpleSidearmsCompat.IsLoaded)
            {
                Compatibility.SimpleSidearmsCompat.ProcessExpiredValidations(currentTick);
            }

            if (currentTick % 6000 == 0)
            {
                CooldownMetrics.CorrectDrift(out int eventCount, out int actualCount);
            }

            if (currentTick % 61 == 0)
            {
                Cleanup.PerformStaggeredCleanup();
            }

            if (currentTick % 1789 == 0)
            {
                AutoArmLogger.Flush();
            }
        }


        private void CleanupLegacyData()
        {
            int totalCleaned = 0;

            totalCleaned += RemoveInvalidEntries(forcedPrimaryWeaponDefs,
                kvp => kvp.Key == null || string.IsNullOrEmpty(kvp.Value));

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


        private void ValidateRestoredWeaponIds()
        {
            if (forcedWeaponIds == null || forcedWeaponIds.Count == 0 ||
                forcedPrimaryWeaponDefs == null || forcedSidearmDefs == null)
                return;

            int mismatchCount = 0;

            foreach (var pawn in forcedWeaponIds.Keys.ToList())
            {
                if (!IsPawnValidForPersistence(pawn))
                    continue;

                if (pawn.equipment?.Primary != null)
                {
                    var primary = pawn.equipment.Primary;

                    if (forcedWeaponIds.ContainsKey(pawn) &&
                        forcedWeaponIds[pawn].Contains(primary.thingIDNumber))
                    {
                        if (forcedPrimaryWeaponDefs.ContainsKey(pawn))
                        {
                            var expectedDefName = forcedPrimaryWeaponDefs[pawn];
                            if (primary.def.defName != expectedDefName)
                            {
                                mismatchCount++;
                                if (AutoArmMod.settings?.debugLogging == true)
                                {
                                    AutoArmLogger.Warn($"Weapon ID mismatch for {pawn.Name?.ToStringShort ?? pawn.LabelShort}: Expected {expectedDefName}, got {primary.def.defName}");
                                }

                                forcedPrimaryWeaponDefs[pawn] = primary.def.defName;
                            }
                        }
                    }
                }
            }

            if (mismatchCount > 0 && AutoArmMod.settings?.debugLogging == true)
            {
                AutoArmLogger.Debug(() => $"Corrected {mismatchCount} weapon def mismatches after loading");
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
