// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: Game save/load persistence for forced weapon assignments
// Handles: Saving forced weapon data between game sessions
// Uses: ForcedWeaponHelper for runtime data management
// Critical: Handles legacy save format migration for backwards compatibility

using AutoArm.Helpers;
using AutoArm.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace AutoArm
{
    /// <summary>
    /// GameComponent to handle saving and loading AutoArm data
    /// </summary>
    public class AutoArmGameComponent : GameComponent
    {
        // Save key constants
        private const string PRIMARY_PAWNS_KEY = "forcedPrimaryWeaponPawns";

        private const string PRIMARY_DEFS_KEY = "forcedPrimaryWeaponDefNames";
        private const string SIDEARM_PAWNS_KEY = "forcedSidearmPawns";
        private const string SIDEARM_COUNT_KEY = "forcedSidearmDefListCount";
        private const string SIDEARM_LIST_PREFIX = "forcedSidearmDefList_";

        // New save keys for weapon IDs
        private const string WEAPON_ID_PAWNS_KEY = "forcedWeaponIdPawns";

        private const string WEAPON_ID_COUNT_KEY = "forcedWeaponIdListCount";
        private const string WEAPON_ID_LIST_PREFIX = "forcedWeaponIdList_";

        // Legacy save keys for backwards compatibility
        private const string LEGACY_PRIMARY_KEYS = "forcedPrimaryWeapons/keys";

        private const string LEGACY_PRIMARY_VALUES = "forcedPrimaryWeapons/values";
        private const string LEGACY_SIDEARM_KEYS = "forcedSidearmDefs/keys";
        private const string LEGACY_SIDEARM_VALUES = "forcedSidearmDefs/values";

        // Data to save
        private Dictionary<Pawn, string> forcedPrimaryWeaponDefs = new Dictionary<Pawn, string>();

        private Dictionary<Pawn, List<string>> forcedSidearmDefs = new Dictionary<Pawn, List<string>>();
        private Dictionary<Pawn, List<int>> forcedWeaponIds = new Dictionary<Pawn, List<int>>();

        public AutoArmGameComponent(Game game) : base()
        {
        }

        /// <summary>
        /// Called when saving the game
        /// </summary>
        public override void ExposeData()
        {
            base.ExposeData();

            if (Scribe.mode == LoadSaveMode.Saving)
            {
                // Get current forced weapon data before saving
                PrepareDataForSaving();
            }

            // Save/Load the forced weapon data
            // Initialize dictionaries if null to prevent errors
            if (forcedPrimaryWeaponDefs == null)
                forcedPrimaryWeaponDefs = new Dictionary<Pawn, string>();
            if (forcedSidearmDefs == null)
                forcedSidearmDefs = new Dictionary<Pawn, List<string>>();

            // Handle legacy save data - we need to consume it even if we don't use it
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                // Load legacy data as separate lists to properly consume the XML nodes
                List<Pawn> legacyPrimaryKeys = null;
                List<string> legacyPrimaryValues = null;
                List<Pawn> legacySidearmKeys = null;
                List<List<string>> legacySidearmValues = null;

                // Try to load the old format data
                try
                {
                    // This loads the data from the old dictionary format
                    Scribe_Collections.Look(ref legacyPrimaryKeys, LEGACY_PRIMARY_KEYS, LookMode.Reference);
                    Scribe_Collections.Look(ref legacyPrimaryValues, LEGACY_PRIMARY_VALUES, LookMode.Value);
                    Scribe_Collections.Look(ref legacySidearmKeys, LEGACY_SIDEARM_KEYS, LookMode.Reference);

                    // Legacy sidearm values were saved incorrectly with Deep mode
                    // Try to skip over them without causing errors
                    try
                    {
                        Scribe_Collections.Look(ref legacySidearmValues, LEGACY_SIDEARM_VALUES, LookMode.Deep);
                    }
                    catch (Exception)
                    {
                        // If loading with Deep fails, try to skip the node
                        try
                        {
                            AutoArmLogger.Warn("Skipping invalid legacy sidearm data in save file");
                        }
                        catch
                        {
                            // Fail silently if even logging fails
                        }
                    }

                    // Convert legacy data to current format if successfully loaded
                    if (legacyPrimaryKeys != null && legacyPrimaryValues != null)
                    {
                        forcedPrimaryWeaponDefs.Clear();
                        for (int i = 0; i < Math.Min(legacyPrimaryKeys.Count, legacyPrimaryValues.Count); i++)
                        {
                            if (legacyPrimaryKeys[i] != null && !string.IsNullOrEmpty(legacyPrimaryValues[i]))
                            {
                                forcedPrimaryWeaponDefs[legacyPrimaryKeys[i]] = legacyPrimaryValues[i];
                            }
                        }
                        AutoArmLogger.Debug($"Loaded {forcedPrimaryWeaponDefs.Count} legacy forced primary weapons");
                    }

                    if (legacySidearmKeys != null && legacySidearmValues != null)
                    {
                        forcedSidearmDefs.Clear();
                        for (int i = 0; i < Math.Min(legacySidearmKeys.Count, legacySidearmValues.Count); i++)
                        {
                            if (legacySidearmKeys[i] != null && legacySidearmValues[i] != null)
                            {
                                forcedSidearmDefs[legacySidearmKeys[i]] = legacySidearmValues[i];
                            }
                        }
                        AutoArmLogger.Debug($"Loaded {forcedSidearmDefs.Count} legacy forced sidearms");
                    }
                }
                catch (Exception ex)
                {
                    AutoArmLogger.Warn($"Failed to load legacy save data: {ex.Message}");
                }
            }

            // Now handle current save/load format
            try
            {
                // Use Lists for save/load to avoid dictionary errors
                List<Pawn> primaryPawns = null;
                List<string> primaryDefs = null;
                List<Pawn> sidearmPawns = null;
                List<List<string>> sidearmDefLists = null;
                List<Pawn> weaponIdPawns = null;
                List<List<int>> weaponIdLists = null;

                if (Scribe.mode == LoadSaveMode.Saving)
                {
                    // Convert dictionaries to lists for saving
                    primaryPawns = new List<Pawn>(forcedPrimaryWeaponDefs.Keys);
                    primaryDefs = new List<string>(forcedPrimaryWeaponDefs.Values);

                    sidearmPawns = new List<Pawn>(forcedSidearmDefs.Keys);
                    sidearmDefLists = new List<List<string>>(forcedSidearmDefs.Values);

                    weaponIdPawns = new List<Pawn>(forcedWeaponIds.Keys);
                    weaponIdLists = new List<List<int>>(forcedWeaponIds.Values);
                }

                // Save/Load as parallel lists with new labels
                Scribe_Collections.Look(ref primaryPawns, PRIMARY_PAWNS_KEY, LookMode.Reference);
                Scribe_Collections.Look(ref primaryDefs, PRIMARY_DEFS_KEY, LookMode.Value);
                Scribe_Collections.Look(ref sidearmPawns, SIDEARM_PAWNS_KEY, LookMode.Reference);

                // Save/Load weapon ID pawns
                Scribe_Collections.Look(ref weaponIdPawns, WEAPON_ID_PAWNS_KEY, LookMode.Reference);

                // For list of lists of strings, we need to use Value mode, not Deep
                // Deep mode is only for IExposable objects
                if (Scribe.mode == LoadSaveMode.Saving)
                {
                    // Save each list separately with an index
                    if (sidearmDefLists != null)
                    {
                        int sidearmCount = sidearmDefLists.Count;
                        Scribe_Values.Look(ref sidearmCount, SIDEARM_COUNT_KEY, 0);

                        for (int i = 0; i < sidearmDefLists.Count; i++)
                        {
                            var defList = sidearmDefLists[i];
                            Scribe_Collections.Look(ref defList, $"{SIDEARM_LIST_PREFIX}{i}", LookMode.Value);
                        }
                    }
                    else
                    {
                        int sidearmCount = 0;
                        Scribe_Values.Look(ref sidearmCount, SIDEARM_COUNT_KEY, 0);
                    }

                    // Save weapon IDs
                    if (weaponIdLists != null)
                    {
                        int weaponIdCount = weaponIdLists.Count;
                        Scribe_Values.Look(ref weaponIdCount, WEAPON_ID_COUNT_KEY, 0);

                        for (int i = 0; i < weaponIdLists.Count; i++)
                        {
                            var idList = weaponIdLists[i];
                            Scribe_Collections.Look(ref idList, $"{WEAPON_ID_LIST_PREFIX}{i}", LookMode.Value);
                        }
                    }
                    else
                    {
                        int weaponIdCount = 0;
                        Scribe_Values.Look(ref weaponIdCount, WEAPON_ID_COUNT_KEY, 0);
                    }
                }
                else if (Scribe.mode == LoadSaveMode.LoadingVars)
                {
                    // Load each list separately
                    int sidearmCount = 0;
                    Scribe_Values.Look(ref sidearmCount, SIDEARM_COUNT_KEY, 0);

                    if (sidearmCount > 0)
                    {
                        sidearmDefLists = new List<List<string>>();
                        for (int i = 0; i < sidearmCount; i++)
                        {
                            List<string> defList = null;
                            Scribe_Collections.Look(ref defList, $"{SIDEARM_LIST_PREFIX}{i}", LookMode.Value);
                            if (defList != null)
                            {
                                sidearmDefLists.Add(defList);
                            }
                            else
                            {
                                sidearmDefLists.Add(new List<string>());
                            }
                        }
                    }

                    // Load weapon IDs
                    int weaponIdCount = 0;
                    Scribe_Values.Look(ref weaponIdCount, WEAPON_ID_COUNT_KEY, 0);

                    if (weaponIdCount > 0)
                    {
                        weaponIdLists = new List<List<int>>();
                        for (int i = 0; i < weaponIdCount; i++)
                        {
                            List<int> idList = null;
                            Scribe_Collections.Look(ref idList, $"{WEAPON_ID_LIST_PREFIX}{i}", LookMode.Value);
                            if (idList != null)
                            {
                                weaponIdLists.Add(idList);
                            }
                            else
                            {
                                weaponIdLists.Add(new List<int>());
                            }
                        }
                    }
                }

                if (Scribe.mode == LoadSaveMode.LoadingVars && primaryPawns != null && primaryDefs != null)
                {
                    // Only reconstruct if we have new format data (don't overwrite legacy data)
                    if (forcedPrimaryWeaponDefs.Count == 0 || sidearmPawns != null)
                    {
                        // Reconstruct dictionaries from lists
                        forcedPrimaryWeaponDefs.Clear();
                        for (int i = 0; i < Math.Min(primaryPawns.Count, primaryDefs.Count); i++)
                        {
                            if (primaryPawns[i] != null && !string.IsNullOrEmpty(primaryDefs[i]))
                            {
                                forcedPrimaryWeaponDefs[primaryPawns[i]] = primaryDefs[i];
                            }
                        }

                        forcedSidearmDefs.Clear();
                        if (sidearmPawns != null && sidearmDefLists != null)
                        {
                            for (int i = 0; i < Math.Min(sidearmPawns.Count, sidearmDefLists.Count); i++)
                            {
                                if (sidearmPawns[i] != null && sidearmDefLists[i] != null && sidearmDefLists[i].Count > 0)
                                {
                                    forcedSidearmDefs[sidearmPawns[i]] = sidearmDefLists[i];
                                }
                            }
                        }

                        // Reconstruct weapon IDs
                        forcedWeaponIds.Clear();
                        if (weaponIdPawns != null && weaponIdLists != null)
                        {
                            for (int i = 0; i < Math.Min(weaponIdPawns.Count, weaponIdLists.Count); i++)
                            {
                                if (weaponIdPawns[i] != null && weaponIdLists[i] != null && weaponIdLists[i].Count > 0)
                                {
                                    forcedWeaponIds[weaponIdPawns[i]] = weaponIdLists[i];
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AutoArmLogger.Error("Error loading forced weapon data from save", ex);
                forcedPrimaryWeaponDefs = new Dictionary<Pawn, string>();
                forcedSidearmDefs = new Dictionary<Pawn, List<string>>();
            }

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                // Clean up any invalid data from legacy saves
                CleanupLegacyData();

                // Restore forced weapon data after loading
                RestoreDataAfterLoading();
                
                // Validate that restored weapon IDs match their expected defs
                ValidateRestoredWeaponIds();
            }
        }

        /// <summary>
        /// Prepare data for saving
        /// </summary>
        private void PrepareDataForSaving()
        {
            forcedPrimaryWeaponDefs.Clear();
            forcedSidearmDefs.Clear();
            forcedWeaponIds.Clear();

            // Get primary weapon data
            var primaryData = ForcedWeaponHelper.GetSaveData();
            foreach (var kvp in primaryData)
            {
                if (kvp.Key != null && kvp.Value != null && !string.IsNullOrEmpty(kvp.Value.defName))
                {
                    forcedPrimaryWeaponDefs[kvp.Key] = kvp.Value.defName;
                }
            }

            // Get sidearm data
            var sidearmData = ForcedWeaponHelper.GetSidearmSaveData();
            foreach (var kvp in sidearmData)
            {
                if (kvp.Key != null && kvp.Value != null && kvp.Value.Count > 0)
                {
                    var defNames = new List<string>();
                    foreach (var def in kvp.Value)
                    {
                        if (def != null && !string.IsNullOrEmpty(def.defName))
                            defNames.Add(def.defName);
                    }
                    // Only add to dictionary if we have valid def names
                    if (defNames.Count > 0)
                        forcedSidearmDefs[kvp.Key] = defNames;
                }
            }

            // Get forced weapon IDs
            var weaponIdData = ForcedWeaponHelper.GetForcedWeaponIds();
            foreach (var kvp in weaponIdData)
            {
                if (kvp.Key != null && kvp.Value != null && kvp.Value.Count > 0)
                {
                    forcedWeaponIds[kvp.Key] = kvp.Value;
                }
            }

            // Remove any entries with null or empty values to prevent save errors
            RemoveInvalidEntries(forcedPrimaryWeaponDefs,
                kvp => string.IsNullOrEmpty(kvp.Value));

            RemoveInvalidEntries(forcedSidearmDefs,
                kvp => kvp.Value == null || kvp.Value.Count == 0);

            if (AutoArmMod.settings?.debugLogging == true)
            {
                AutoArmLogger.Debug($"Saving forced weapon data: {forcedPrimaryWeaponDefs.Count} primary, {forcedSidearmDefs.Count} sidearm entries, {forcedWeaponIds.Count} weapon IDs");
            }
        }

        /// <summary>
        /// Restore data after loading
        /// </summary>
        private void RestoreDataAfterLoading()
        {
            // First, validate all pawns still exist and are valid
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

            // Convert saved defNames back to ThingDefs and restore
            var primaryDataToRestore = new Dictionary<Pawn, ThingDef>();

            if (forcedPrimaryWeaponDefs != null)
            {
                foreach (var kvp in forcedPrimaryWeaponDefs)
                {
                    // Skip invalid entries
                    if (kvp.Key == null || string.IsNullOrEmpty(kvp.Value))
                        continue;

                    // Validate pawn still exists
                    if (!validPawns.Contains(kvp.Key))
                    {
                        if (Prefs.DevMode)
                            AutoArmLogger.Debug($"Skipping forced weapon for missing/invalid pawn: {kvp.Key?.LabelShort ?? "null"}");
                        continue;
                    }

                    var def = DefDatabase<ThingDef>.GetNamedSilentFail(kvp.Value);
                    if (def != null)
                    {
                        primaryDataToRestore[kvp.Key] = def;
                    }
                    else
                    {
                        if (Prefs.DevMode)
                            AutoArmLogger.Debug($"Could not find weapon def '{kvp.Value}' when loading save");
                    }
                }
            }

            ForcedWeaponHelper.LoadSaveData(primaryDataToRestore);

            // Restore sidearm data
            var sidearmDataToRestore = new Dictionary<Pawn, HashSet<ThingDef>>();

            if (forcedSidearmDefs != null)
            {
                foreach (var kvp in forcedSidearmDefs)
                {
                    // Skip invalid entries
                    if (kvp.Key == null || kvp.Value == null || kvp.Value.Count == 0)
                        continue;

                    // Validate pawn still exists
                    if (!validPawns.Contains(kvp.Key))
                    {
                        if (Prefs.DevMode)
                            AutoArmLogger.Debug($"Skipping forced sidearms for missing/invalid pawn: {kvp.Key?.LabelShort ?? "null"}");
                        continue;
                    }

                    var defs = new HashSet<ThingDef>();
                    foreach (var defName in kvp.Value)
                    {
                        if (!string.IsNullOrEmpty(defName))
                        {
                            var def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
                            if (def != null)
                            {
                                defs.Add(def);
                            }
                            else
                            {
                                if (Prefs.DevMode)
                                    AutoArmLogger.Debug($"Could not find sidearm def '{defName}' when loading save");
                            }
                        }
                    }
                    if (defs.Count > 0)
                    {
                        sidearmDataToRestore[kvp.Key] = defs;
                    }
                }
            }

            ForcedWeaponHelper.LoadSidearmSaveData(sidearmDataToRestore);

            // Load forced weapon IDs - validate they still exist
            if (forcedWeaponIds != null && forcedWeaponIds.Count > 0)
            {
                var validatedWeaponIds = new Dictionary<Pawn, List<int>>();
                
                foreach (var kvp in forcedWeaponIds)
                {
                    if (kvp.Key == null || !validPawns.Contains(kvp.Key))
                        continue;
                        
                    // Validate weapon IDs still exist
                    var validIds = new List<int>();
                    foreach (var weaponId in kvp.Value)
                    {
                        // Check if weapon with this ID exists in the pawn's equipment or inventory
                        bool weaponExists = false;
                        
                        if (kvp.Key.equipment?.Primary?.thingIDNumber == weaponId)
                            weaponExists = true;
                        else if (kvp.Key.inventory?.innerContainer != null)
                        {
                            weaponExists = kvp.Key.inventory.innerContainer
                                .OfType<ThingWithComps>()
                                .Any(w => w.thingIDNumber == weaponId);
                        }
                        
                        if (weaponExists)
                        {
                            validIds.Add(weaponId);
                        }
                        else if (Prefs.DevMode)
                        {
                            AutoArmLogger.Debug($"Weapon ID {weaponId} no longer exists for {kvp.Key.LabelShort}");
                        }
                    }
                    
                    if (validIds.Count > 0)
                    {
                        validatedWeaponIds[kvp.Key] = validIds;
                    }
                }
                
                ForcedWeaponHelper.LoadForcedWeaponIds(validatedWeaponIds);
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
                AutoArmLogger.Debug($"Loaded forced weapon data: {primaryDataToRestore.Count} primary, {sidearmDataToRestore.Count} sidearm entries, {weaponIdCount} weapon IDs");
            }

            // Clear the temporary storage
            forcedPrimaryWeaponDefs?.Clear();
            forcedSidearmDefs?.Clear();
            forcedWeaponIds?.Clear();
        }

        /// <summary>
        /// Called after game component is created
        /// </summary>
        public override void StartedNewGame()
        {
            base.StartedNewGame();
            if (AutoArmMod.settings?.debugLogging == true)
                AutoArmLogger.Debug("AutoArm GameComponent initialized for new game");
        }

        /// <summary>
        /// Called when loading a saved game
        /// </summary>
        public override void LoadedGame()
        {
            base.LoadedGame();
            if (AutoArmMod.settings?.debugLogging == true)
                AutoArmLogger.Debug("AutoArm GameComponent loaded from save");
            
            // Re-mark bonded weapons as forced after loading if the setting is enabled
            // This ensures bonded weapons created after the initial save are properly tracked
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
        /// Called every game tick - triggers cleanup operations
        /// </summary>
        public override void GameComponentTick()
        {
            base.GameComponentTick();

            // Process dropped forced weapons every tick to handle the 1-second timer
            ForcedWeaponTracker.ProcessDroppedWeapons();

            // Generate performance report periodically in debug mode
            if (AutoArmMod.settings?.debugLogging == true)
            {
                PerformanceHelper.GenerateReport();
            }

            // Force process pending weapon cache updates periodically
            if (Find.TickManager.TicksGame % 600 == 0) // Every 10 seconds
            {
                Caching.ImprovedWeaponCacheManager.ForceProcessPendingWeapons();
            }

            if (CleanupHelper.ShouldRunCleanup())
            {
                CleanupHelper.PerformFullCleanup();
            }
        }

        /// <summary>
        /// Clean up invalid data from legacy saves
        /// </summary>
        private void CleanupLegacyData()
        {
            int totalCleaned = 0;

            // Clean up primary weapon defs
            totalCleaned += RemoveInvalidEntries(forcedPrimaryWeaponDefs,
                kvp => kvp.Key == null || string.IsNullOrEmpty(kvp.Value));

            // Clean up sidearm defs
            if (forcedSidearmDefs != null)
            {
                // First clean up empty strings within the lists
                foreach (var kvp in forcedSidearmDefs.ToList())
                {
                    if (kvp.Value != null)
                    {
                        kvp.Value.RemoveAll(s => string.IsNullOrEmpty(s));
                    }
                }

                // Then remove entries with null keys or empty lists
                totalCleaned += RemoveInvalidEntries(forcedSidearmDefs,
                    kvp => kvp.Key == null || kvp.Value == null || kvp.Value.Count == 0);
            }

            if (totalCleaned > 0 && Prefs.DevMode)
            {
                AutoArmLogger.Debug($"Cleaned up {totalCleaned} invalid entries from legacy save data");
            }
        }

        /// <summary>
        /// Validate that restored weapon IDs still match their expected definitions
        /// This handles cases where mods are added/removed between saves
        /// </summary>
        private void ValidateRestoredWeaponIds()
        {
            // Only validate if we have both IDs and defs saved
            if (forcedWeaponIds == null || forcedWeaponIds.Count == 0 ||
                forcedPrimaryWeaponDefs == null || forcedSidearmDefs == null)
                return;

            int mismatchCount = 0;
            
            foreach (var pawn in forcedWeaponIds.Keys.ToList())
            {
                if (pawn == null || pawn.Destroyed || pawn.Dead)
                    continue;
                    
                // Check if the pawn's actual weapons match what we expect
                if (pawn.equipment?.Primary != null)
                {
                    var primary = pawn.equipment.Primary;
                    
                    // If we have this weapon ID tracked as forced
                    if (forcedWeaponIds.ContainsKey(pawn) && 
                        forcedWeaponIds[pawn].Contains(primary.thingIDNumber))
                    {
                        // Verify the def matches what we saved
                        if (forcedPrimaryWeaponDefs.ContainsKey(pawn))
                        {
                            var expectedDefName = forcedPrimaryWeaponDefs[pawn];
                            if (primary.def.defName != expectedDefName)
                            {
                                mismatchCount++;
                                if (AutoArmMod.settings?.debugLogging == true)
                                {
                                    AutoArmLogger.Warn($"Weapon ID mismatch for {pawn.LabelShort}: Expected {expectedDefName}, got {primary.def.defName}");
                                }
                                
                                // Update the def to match reality
                                forcedPrimaryWeaponDefs[pawn] = primary.def.defName;
                            }
                        }
                    }
                }
            }
            
            if (mismatchCount > 0 && AutoArmMod.settings?.debugLogging == true)
            {
                AutoArmLogger.Debug($"Corrected {mismatchCount} weapon def mismatches after loading");
            }
        }

        /// <summary>
        /// Helper method to remove invalid entries from a dictionary
        /// </summary>
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