using System.Collections.Generic;
using Verse;
using RimWorld;

namespace AutoArm
{
    /// <summary>
    /// GameComponent to handle saving and loading AutoArm data
    /// </summary>
    public class AutoArmGameComponent : GameComponent
    {
        // Data to save
        private Dictionary<Pawn, string> forcedPrimaryWeaponDefs = new Dictionary<Pawn, string>();
        private Dictionary<Pawn, List<string>> forcedSidearmDefs = new Dictionary<Pawn, List<string>>();
        
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
            Scribe_Collections.Look(ref forcedPrimaryWeaponDefs, "forcedPrimaryWeapons", 
                LookMode.Reference, LookMode.Value);
            Scribe_Collections.Look(ref forcedSidearmDefs, "forcedSidearmDefs", 
                LookMode.Reference, LookMode.Value);
            
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                // Restore forced weapon data after loading
                RestoreDataAfterLoading();
            }
        }
        
        /// <summary>
        /// Prepare data for saving
        /// </summary>
        private void PrepareDataForSaving()
        {
            forcedPrimaryWeaponDefs.Clear();
            forcedSidearmDefs.Clear();
            
            // Get primary weapon data
            var primaryData = ForcedWeaponHelper.GetSaveData();
            foreach (var kvp in primaryData)
            {
                if (kvp.Key != null && kvp.Value != null)
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
                        if (def != null)
                            defNames.Add(def.defName);
                    }
                    if (defNames.Count > 0)
                        forcedSidearmDefs[kvp.Key] = defNames;
                }
            }
            
            AutoArmDebug.Log($"Saving forced weapon data: {forcedPrimaryWeaponDefs.Count} primary, {forcedSidearmDefs.Count} sidearm entries");
        }
        
        /// <summary>
        /// Restore data after loading
        /// </summary>
        private void RestoreDataAfterLoading()
        {
            // Convert saved defNames back to ThingDefs and restore
            var primaryDataToRestore = new Dictionary<Pawn, ThingDef>();
            
            if (forcedPrimaryWeaponDefs != null)
            {
                foreach (var kvp in forcedPrimaryWeaponDefs)
                {
                    if (kvp.Key != null && !string.IsNullOrEmpty(kvp.Value))
                    {
                        var def = DefDatabase<ThingDef>.GetNamedSilentFail(kvp.Value);
                        if (def != null)
                        {
                            primaryDataToRestore[kvp.Key] = def;
                        }
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
                    if (kvp.Key != null && kvp.Value != null && kvp.Value.Count > 0)
                    {
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
                            }
                        }
                        if (defs.Count > 0)
                        {
                            sidearmDataToRestore[kvp.Key] = defs;
                        }
                    }
                }
            }
            
            ForcedWeaponHelper.LoadSidearmSaveData(sidearmDataToRestore);
            
            AutoArmDebug.Log($"Loaded forced weapon data: {primaryDataToRestore.Count} primary, {sidearmDataToRestore.Count} sidearm entries");
            
            // Clear the temporary storage
            forcedPrimaryWeaponDefs?.Clear();
            forcedSidearmDefs?.Clear();
        }
        
        /// <summary>
        /// Called after game component is created
        /// </summary>
        public override void StartedNewGame()
        {
            base.StartedNewGame();
            AutoArmDebug.Log("AutoArm GameComponent initialized for new game");
        }
        
        /// <summary>
        /// Called when loading a saved game
        /// </summary>
        public override void LoadedGame()
        {
            base.LoadedGame();
            AutoArmDebug.Log("AutoArm GameComponent loaded from save");
        }
    }
}