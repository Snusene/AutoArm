// AutoArm RimWorld 1.5+ mod - Unified pawn validation and caching system
// This file: Combines PawnValidationCache and LordJobCache for all pawn validation needs
// Critical: Caches stable pawn properties and lord job states to improve ThinkNode performance
// Uses: ThinkNode_ConditionalWeaponStatus in ModInit.cs

using System;
using System.Collections.Generic;
using System.Linq;
using AutoArm.Definitions;
using AutoArm.Helpers;
using AutoArm.Jobs;
using AutoArm.Logging;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI.Group;

namespace AutoArm.Caching
{
    /// <summary>
    /// Unified caching system for pawn validation checks.
    /// Combines stable property caching with lord job tracking.
    /// </summary>
    public static class PawnValidationCache
    {
        // ========== STABLE PROPERTY CACHE ==========
        
        // Cache for stable pawn properties
        private static Dictionary<Pawn, CachedPawnValidation> _pawnCache = new Dictionary<Pawn, CachedPawnValidation>();
        
        // Track when we last fully validated each pawn (for periodic revalidation)
        private static Dictionary<Pawn, int> _lastFullValidationTick = new Dictionary<Pawn, int>();
        
        // How often to force a full revalidation (in ticks)
        private const int RevalidationInterval = 2500; // ~41 seconds
        
        // Track cache hits/misses for performance monitoring
        private static int _cacheHits = 0;
        private static int _cacheMisses = 0;
        private static int _lastReportTick = 0;
        
        // ========== LORD JOB CACHE (from LordJobCachePatches) ==========
        
        // Store whether pawns are in restricted lord jobs (parties, rituals, etc.)
        private static readonly HashSet<Pawn> pawnsInRestrictedLords = new HashSet<Pawn>();
        
        // Track lords to handle cleanup
        private static readonly Dictionary<Pawn, Lord> pawnLords = new Dictionary<Pawn, Lord>();
        
        // Lord job types that prevent weapon switching - exact type names for fast matching
        private static readonly HashSet<string> restrictedLordJobTypes = new HashSet<string>
        {
            // Vanilla lord jobs
            "LordJob_Joinable_Party",
            "LordJob_Joinable_MarriageCeremony",
            "LordJob_Ritual",
            "LordJob_Joinable_Speech",
            "LordJob_BestowingCeremony",
            "LordJob_Joinable_Concert",
            "LordJob_Joinable_Dance",
            "LordJob_TradeWithColony",
            "LordJob_FormAndSendCaravan",
            
            // Ideology rituals
            "LordJob_Joinable_Gathering",
            "LordJob_RitualDuel",
            "LordJob_Joinable_DateLead",
            
            // Common modded lord jobs (Hospitality, etc.)
            "LordJob_VisitColony",
            "LordJob_HospitalityParty"
        };
        
        // Substring patterns for modded lord jobs we don't know the exact names of
        private static readonly string[] restrictedPatterns = new string[]
        {
            "Party",
            "Wedding", 
            "Ritual",
            "Speech",
            "Ceremony",
            "Festival",
            "Celebration",
            "Gathering",
            "Concert",
            "Dance",
            "Funeral",
            "Date",
            "Bestowing"
        };
        
        /// <summary>
        /// Cached validation data for a pawn - only stable properties
        /// </summary>
        private class CachedPawnValidation
        {
            // Race-based properties (never change)
            public bool IsAnimal { get; set; }
            public bool IsMechanoid { get; set; }
            public bool IsToolUser { get; set; }
            public bool HasSufficientIntelligence { get; set; }
            
            // Capability-based (rarely change)
            public bool HasManipulation { get; set; }
            public bool CanDoViolence { get; set; }
            
            // Age-based (changes very slowly)
            public bool IsChild { get; set; }
            public bool MeetsAgeRequirement { get; set; }
            
            // Colonist status (changes infrequently)
            public bool IsColonist { get; set; }
            public bool IsTemporaryColonist { get; set; }
            public bool IsPrisoner { get; set; }
            
            // Timestamp for cache invalidation
            public int CachedAtTick { get; set; }
            
            // Quick validity check
            public bool IsValidForWeapons { get; set; }
        }
        
        // ========== PUBLIC API ==========
        
        /// <summary>
        /// Performs validation checks with caching for stable properties
        /// </summary>
        public static bool CanConsiderWeapons(Pawn pawn)
        {
            // === ALWAYS CHECK DYNAMIC PROPERTIES FIRST (no caching) ===
            if (!CheckDynamicProperties(pawn))
                return false;
            
            // === CHECK CACHED STABLE PROPERTIES ===
            return CheckCachedProperties(pawn);
        }
        
        /// <summary>
        /// Fast check if pawn is in a restricted lord job (from LordJobCache)
        /// </summary>
        public static bool IsInRestrictedLord(Pawn pawn)
        {
            if (pawn == null) return false;
            return pawnsInRestrictedLords.Contains(pawn);
        }
        
        // ========== DYNAMIC PROPERTY CHECKS ==========
        
        /// <summary>
        /// Checks properties that change frequently and should never be cached
        /// </summary>
        private static bool CheckDynamicProperties(Pawn pawn)
        {
            // ========== TIER 1: Ultra-cheap property checks (practically free) ==========
            
            // Null/spawned/dead/downed/drafted - most common failures
            if (pawn?.Spawned != true || pawn.Dead || pawn.Downed || pawn.Drafted)
                return false;
            
            // Mental state - single bool check
            if (pawn.InMentalState)
                return false;
            
            // ========== TIER 2: Fast lookups (HashSet/dictionary) ==========
            
            // Lord job check - HashSet lookup, very fast
            if (IsInRestrictedLord(pawn))
                return false;
            
            // ========== TIER 3: Medium cost - method calls ==========
            
            // Cache InBed() result to avoid calling it twice
            bool inBed = pawn.InBed();
            
            // In bed/sleeping check (using cached result)
            if (inBed || pawn.CurJob?.def == JobDefOf.LayDown)
                return false;
            
            // Medical bed check (reusing cached InBed result)
            if (inBed && pawn.CurrentBed()?.Medical == true &&
                HealthAIUtility.ShouldBeTendedNowByPlayer(pawn))
                return false;
            
            // Being carried or in caravan
            if (pawn.GetCaravan() != null || pawn.carryTracker?.CarriedThing != null)
                return false;
            
            // ========== TIER 4: Potentially expensive checks ==========
            
            // In ritual/ceremony - potentially complex check
            if (ValidationHelper.IsInRitual(pawn))
                return false;
            
            // Currently hauling - multiple string comparisons
            if (IsCurrentlyHauling(pawn))
                return false;
            
            return true;
        }
        
        /// <summary>
        /// Checks if pawn is currently hauling (including PUAH jobs)
        /// </summary>
        private static bool IsCurrentlyHauling(Pawn pawn)
        {
            if (pawn.CurJob == null)
                return false;
            
            var jobDef = pawn.CurJob.def;
            return jobDef == JobDefOf.HaulToCell ||
                   jobDef == JobDefOf.HaulToContainer ||
                   jobDef?.defName?.Contains("Haul") == true ||
                   jobDef?.defName?.Contains("Inventory") == true ||
                   jobDef?.defName == "HaulToInventory" ||        // PUAH hauling
                   jobDef?.defName == "UnloadYourHauledInventory"; // PUAH unloading
        }
        
        // ========== STABLE PROPERTY CACHE ==========
        
        /// <summary>
        /// Checks stable properties using cache
        /// </summary>
        private static bool CheckCachedProperties(Pawn pawn)
        {
            // Check if we need to revalidate
            int currentTick = Find.TickManager.TicksGame;
            bool needsRevalidation = false;
            
            if (_lastFullValidationTick.TryGetValue(pawn, out int lastValidation))
            {
                needsRevalidation = (currentTick - lastValidation) > RevalidationInterval;
            }
            else
            {
                needsRevalidation = true;
            }
            
            // Try to get cached data
            if (!needsRevalidation && _pawnCache.TryGetValue(pawn, out var cached))
            {
                _cacheHits++;
                ReportCacheStats();
                return cached.IsValidForWeapons;
            }
            
            // Cache miss or needs revalidation - build new cache entry
            _cacheMisses++;
            var validation = BuildCacheEntry(pawn);
            
            // Store in cache
            _pawnCache[pawn] = validation;
            _lastFullValidationTick[pawn] = currentTick;
            
            ReportCacheStats();
            return validation.IsValidForWeapons;
        }
        
        /// <summary>
        /// Builds a new cache entry for a pawn
        /// </summary>
        private static CachedPawnValidation BuildCacheEntry(Pawn pawn)
        {
            var entry = new CachedPawnValidation
            {
                CachedAtTick = Find.TickManager.TicksGame
            };
            
            // === CAPABILITY CHECKS ===
            if (pawn.health?.capacities == null)
            {
                entry.HasManipulation = false;
                entry.IsValidForWeapons = false;
                return entry;
            }
            
            entry.HasManipulation = pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation);
            if (!entry.HasManipulation)
            {
                entry.IsValidForWeapons = false;
                return entry;
            }
            
            // === RACE PROPERTY CHECKS ===
            if (pawn.RaceProps == null)
            {
                entry.IsValidForWeapons = false;
                return entry;
            }
            
            entry.IsAnimal = pawn.RaceProps.Animal;
            entry.IsMechanoid = pawn.RaceProps.IsMechanoid;
            entry.IsToolUser = pawn.RaceProps.ToolUser;
            entry.HasSufficientIntelligence = pawn.RaceProps.intelligence >= Intelligence.ToolUser;
            
            // Quick exit if basic requirements not met
            if (entry.IsAnimal || entry.IsMechanoid || !entry.IsToolUser || !entry.HasSufficientIntelligence)
            {
                entry.IsValidForWeapons = false;
                return entry;
            }
            
            // === COLONIST STATUS ===
            entry.IsColonist = JobGiverHelpers.SafeIsColonist(pawn);
            entry.IsTemporaryColonist = JobGiverHelpers.IsTemporaryColonist(pawn);
            entry.IsPrisoner = pawn.IsPrisoner;
            
            if (!entry.IsColonist || entry.IsPrisoner)
            {
                entry.IsValidForWeapons = false;
                return entry;
            }
            
            // Check temporary colonist settings
            if (entry.IsTemporaryColonist && !(AutoArmMod.settings?.allowTemporaryColonists ?? false))
            {
                entry.IsValidForWeapons = false;
                return entry;
            }
            
            // === VIOLENCE CAPABILITY ===
            try
            {
                entry.CanDoViolence = !pawn.WorkTagIsDisabled(WorkTags.Violent);
            }
            catch
            {
                entry.CanDoViolence = true; // Assume they can if we can't check
            }
            
            if (!entry.CanDoViolence)
            {
                entry.IsValidForWeapons = false;
                return entry;
            }
            
            // === AGE CHECKS (Biotech) ===
            if (ModsConfig.BiotechActive && pawn.ageTracker != null)
            {
                int age = pawn.ageTracker.AgeBiologicalYears;
                entry.IsChild = age < 18;
                
                if (entry.IsChild)
                {
                    bool childrenAllowed = AutoArmMod.settings?.allowChildrenToEquipWeapons ?? false;
                    int minAge = AutoArmMod.settings?.childrenMinAge ?? 13;
                    entry.MeetsAgeRequirement = childrenAllowed && age >= minAge;
                }
                else
                {
                    entry.MeetsAgeRequirement = true;
                }
            }
            else
            {
                entry.IsChild = false;
                entry.MeetsAgeRequirement = true;
            }
            
            if (!entry.MeetsAgeRequirement)
            {
                entry.IsValidForWeapons = false;
                return entry;
            }
            
            // All stable checks passed
            entry.IsValidForWeapons = true;
            return entry;
        }
        
        // ========== LORD JOB CACHE MANAGEMENT ==========
        
        /// <summary>
        /// Update cache when pawn's lord changes (from LordJobCache)
        /// </summary>
        public static void UpdateLordCache(Pawn pawn, Lord newLord)
        {
            if (pawn == null) return;
            
            // Only track colonists and slaves (they can pick up weapons)
            if (!pawn.IsColonist && !pawn.IsSlaveOfColony)
            {
                RemoveFromLordCache(pawn);
                return;
            }
            
            // Remove from old lord tracking
            RemoveFromLordCache(pawn);
            
            // Add to new lord if restricted
            if (newLord?.LordJob != null)
            {
                pawnLords[pawn] = newLord;
                
                var lordJobType = newLord.LordJob.GetType();
                var typeName = lordJobType.Name;
                
                // Check exact type match first (fastest)
                if (restrictedLordJobTypes.Contains(typeName))
                {
                    pawnsInRestrictedLords.Add(pawn);
                    
                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        AutoArmLogger.Debug($"[{pawn.LabelShort}] Joined restricted lord job: {typeName}");
                    }
                    return;
                }
                
                // Check patterns for unknown modded lord jobs
                foreach (var pattern in restrictedPatterns)
                {
                    if (typeName.Contains(pattern))
                    {
                        pawnsInRestrictedLords.Add(pawn);
                        
                        // Add to known types for future fast matching
                        restrictedLordJobTypes.Add(typeName);
                        
                        if (AutoArmMod.settings?.debugLogging == true)
                        {
                            AutoArmLogger.Debug($"[{pawn.LabelShort}] Joined restricted lord job (pattern match): {typeName}");
                        }
                        return;
                    }
                }
            }
        }
        
        /// <summary>
        /// Remove pawn from lord cache
        /// </summary>
        private static void RemoveFromLordCache(Pawn pawn)
        {
            if (pawn == null) return;
            
            if (pawnLords.Remove(pawn))
            {
                if (pawnsInRestrictedLords.Remove(pawn) && AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug($"[{pawn.LabelShort}] Left restricted lord job");
                }
            }
        }
        
        // ========== CACHE INVALIDATION ==========
        
        /// <summary>
        /// Invalidates cache for a specific pawn (call when major changes occur)
        /// </summary>
        public static void InvalidatePawn(Pawn pawn)
        {
            if (pawn == null)
                return;
            
            _pawnCache.Remove(pawn);
            _lastFullValidationTick.Remove(pawn);
            
            // Only log during actual gameplay, not during initialization/loading
            // Skip logging during first 10 seconds (600 ticks) after game start or if TickManager not ready
            if (AutoArmMod.settings?.debugLogging == true && 
                Find.TickManager != null && 
                Find.TickManager.TicksGame > 600 &&
                Current.ProgramState == ProgramState.Playing)
            {
                AutoArmLogger.Debug($"[PawnValidationCache] Invalidated cache for {pawn.LabelShort}");
            }
        }
        
        /// <summary>
        /// Invalidates cache for pawns that have changed faction/colonist status
        /// </summary>
        public static void InvalidateFactionChanges()
        {
            // This is called when faction changes occur
            // We need to revalidate colonist status for all cached pawns
            var pawnsToRevalidate = _pawnCache.Keys.ToList();
            foreach (var pawn in pawnsToRevalidate)
            {
                if (pawn == null || pawn.Destroyed || pawn.Dead)
                {
                    _pawnCache.Remove(pawn);
                    _lastFullValidationTick.Remove(pawn);
                }
                else
                {
                    // Force revalidation on next check
                    _lastFullValidationTick[pawn] = 0;
                }
            }
        }
        
        // ========== CLEANUP ==========
        
        /// <summary>
        /// Cleans up cache for dead/destroyed pawns
        /// </summary>
        public static void CleanupDeadPawns()
        {
            // Clean up stable property cache - combine both dictionaries in one pass
            var deadPawns = _pawnCache.Keys
                .Where(p => p == null || p.Destroyed || p.Dead)
                .ToList();
            
            foreach (var pawn in deadPawns)
            {
                _pawnCache.Remove(pawn);
                _lastFullValidationTick.Remove(pawn);  // Remove from both in same loop
            }
            
            // Clean up any remaining entries in validation tick tracking
            // (in case there are any orphaned entries not in _pawnCache)
            var orphanedValidationPawns = _lastFullValidationTick.Keys
                .Where(p => (p == null || p.Destroyed || p.Dead) && !_pawnCache.ContainsKey(p))
                .ToList();
            
            foreach (var pawn in orphanedValidationPawns)
            {
                _lastFullValidationTick.Remove(pawn);
            }
            
            // Clean up lord job cache
            var deadLordPawns = pawnLords.Keys.Where(p => p == null || p.Destroyed || p.Dead).ToList();
            foreach (var pawn in deadLordPawns)
            {
                RemoveFromLordCache(pawn);
            }
            
            // Also clean up the restricted set
            pawnsInRestrictedLords.RemoveWhere(p => p == null || p.Destroyed || p.Dead);
            
            if (AutoArmMod.settings?.debugLogging == true && 
                (deadPawns.Count > 0 || orphanedValidationPawns.Count > 0 || deadLordPawns.Count > 0))
            {
                AutoArmLogger.Debug($"[PawnValidationCache] Cleaned up {deadPawns.Count + orphanedValidationPawns.Count + deadLordPawns.Count} dead pawn entries");
            }
        }
        
        /// <summary>
        /// Clears entire cache (use sparingly)
        /// </summary>
        public static void ClearCache()
        {
            int count = _pawnCache.Count;
            _pawnCache.Clear();
            _lastFullValidationTick.Clear();
            _cacheHits = 0;
            _cacheMisses = 0;
            
            // Also clear lord cache
            ClearLordCache();
            
            if (AutoArmMod.settings?.debugLogging == true)
            {
                AutoArmLogger.Debug($"[PawnValidationCache] Cleared entire cache ({count} entries)");
            }
        }
        
        /// <summary>
        /// Clear all lord caches (for save/load)
        /// </summary>
        public static void ClearLordCache()
        {
            pawnsInRestrictedLords.Clear();
            pawnLords.Clear();
        }
        
        // ========== STATISTICS ==========
        
        /// <summary>
        /// Reports cache statistics periodically
        /// </summary>
        private static void ReportCacheStats()
        {
            if (AutoArmMod.settings?.debugLogging != true)
                return;
            
            int currentTick = Find.TickManager.TicksGame;
            if (currentTick - _lastReportTick > 6000) // Report every 100 seconds
            {
                _lastReportTick = currentTick;
                
                int total = _cacheHits + _cacheMisses;
                if (total > 0)
                {
                    float hitRate = (_cacheHits / (float)total) * 100f;
                    AutoArmLogger.Debug($"[PawnValidationCache] Stats - Entries: {_pawnCache.Count}, " +
                                      $"Hits: {_cacheHits}, Misses: {_cacheMisses}, " +
                                      $"Hit Rate: {hitRate:F1}% | " +
                                      $"Lord Cache: {pawnsInRestrictedLords.Count} restricted, {pawnLords.Count} tracked");
                }
                
                // Reset counters
                _cacheHits = 0;
                _cacheMisses = 0;
            }
        }
        
        /// <summary>
        /// Gets current cache size for monitoring
        /// </summary>
        public static int CacheSize => _pawnCache.Count;
        
        /// <summary>
        /// Gets cache hit rate for performance monitoring
        /// </summary>
        public static float GetHitRate()
        {
            int total = _cacheHits + _cacheMisses;
            if (total == 0) return 0f;
            return (_cacheHits / (float)total) * 100f;
        }
        
        /// <summary>
        /// Get debug statistics (including lord cache)
        /// </summary>
        public static string GetDebugStats()
        {
            return $"PawnValidationCache: {_pawnCache.Count} cached, LordCache: {pawnsInRestrictedLords.Count} in restricted lords, {pawnLords.Count} total tracked";
        }
    }
    
    // ========== LEGACY COMPATIBILITY CLASS ==========
    
    /// <summary>
    /// Legacy compatibility class - redirects to PawnValidationCache
    /// This ensures existing code using LordJobCache continues to work
    /// </summary>
    public static class LordJobCache
    {
        public static bool IsInRestrictedLord(Pawn pawn) => PawnValidationCache.IsInRestrictedLord(pawn);
        public static void UpdateLordCache(Pawn pawn, Lord newLord) => PawnValidationCache.UpdateLordCache(pawn, newLord);
        public static void RemoveFromCache(Pawn pawn) => PawnValidationCache.InvalidatePawn(pawn);
        public static void CleanupDeadPawns() => PawnValidationCache.CleanupDeadPawns();
        public static void ClearAll() => PawnValidationCache.ClearLordCache();
        public static string GetDebugStats() => PawnValidationCache.GetDebugStats();
    }
    
    // ========== HARMONY PATCHES ==========
    
    /// <summary>
    /// Harmony patches to invalidate cache when relevant changes occur
    /// </summary>
    [HarmonyPatch]
    public static class PawnValidationCachePatches
    {
        // ===== STABLE PROPERTY INVALIDATION PATCHES =====
        
        // Patch faction changes
        [HarmonyPatch(typeof(Pawn), "SetFaction")]
        [HarmonyPostfix]
        public static void SetFaction_Postfix(Pawn __instance)
        {
            PawnValidationCache.InvalidatePawn(__instance);
        }
        
        // Patch guest status changes
        [HarmonyPatch(typeof(Pawn_GuestTracker), "SetGuestStatus")]
        [HarmonyPostfix]
        public static void SetGuestStatus_Postfix(Pawn ___pawn)
        {
            PawnValidationCache.InvalidatePawn(___pawn);
        }
        
        // Patch health capacity changes
        [HarmonyPatch(typeof(PawnCapacitiesHandler), "Notify_CapacityLevelsDirty")]
        [HarmonyPostfix]
        public static void Notify_CapacityLevelsDirty_Postfix(PawnCapacitiesHandler __instance)
        {
            // Get pawn from the handler
            var pawn = Traverse.Create(__instance).Field("pawn").GetValue<Pawn>();
            if (pawn != null)
            {
                PawnValidationCache.InvalidatePawn(pawn);
            }
        }
        
        // Patch work settings changes (for violence capability)
        [HarmonyPatch(typeof(Pawn_WorkSettings), "Notify_DisabledWorkTypesChanged")]
        [HarmonyPostfix]
        public static void Notify_DisabledWorkTypesChanged_Postfix(Pawn ___pawn)
        {
            PawnValidationCache.InvalidatePawn(___pawn);
        }
        
        // Patch age changes (birthdays)
        [HarmonyPatch(typeof(Pawn_AgeTracker), "BirthdayBiological")]
        [HarmonyPostfix]
        public static void BirthdayBiological_Postfix(Pawn ___pawn)
        {
            PawnValidationCache.InvalidatePawn(___pawn);
        }
        
        // ===== LORD JOB CACHE PATCHES (from LordJobCachePatches) =====
        
        /// <summary>
        /// Track when pawns join lords
        /// </summary>
        [HarmonyPatch(typeof(Lord), "AddPawn")]
        public static class Lord_AddPawn_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(Lord __instance, Pawn p)
            {
                if (AutoArmMod.settings?.modEnabled != true) return;
                
                // Update cache when pawn joins a lord
                PawnValidationCache.UpdateLordCache(p, __instance);
            }
        }
        
        /// <summary>
        /// Track when pawns leave lords
        /// </summary>
        [HarmonyPatch(typeof(Lord), "RemovePawn")]
        public static class Lord_RemovePawn_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(Pawn p)
            {
                if (AutoArmMod.settings?.modEnabled != true) return;
                
                // Clear from cache when leaving a lord
                PawnValidationCache.InvalidatePawn(p);
            }
        }
        
        /// <summary>
        /// Clean up when lord is destroyed
        /// </summary>
        [HarmonyPatch(typeof(Lord), "Cleanup")]
        public static class Lord_Cleanup_Patch
        {
            [HarmonyPrefix]
            public static void Prefix(Lord __instance)
            {
                if (AutoArmMod.settings?.modEnabled != true) return;
                
                // Clear all pawns from this lord
                if (__instance.ownedPawns != null)
                {
                    foreach (var pawn in __instance.ownedPawns)
                    {
                        PawnValidationCache.InvalidatePawn(pawn);
                    }
                }
            }
        }
        
        /// <summary>
        /// Clean up when pawn is destroyed
        /// </summary>
        [HarmonyPatch(typeof(Pawn), "Destroy")]
        public static class Pawn_Destroy_Cache_Patch
        {
            [HarmonyPrefix]
            public static void Prefix(Pawn __instance)
            {
                if (AutoArmMod.settings?.modEnabled != true) return;
                
                PawnValidationCache.InvalidatePawn(__instance);
            }
        }
        
        /// <summary>
        /// Clean up when pawn dies
        /// </summary>
        [HarmonyPatch(typeof(Pawn), "Kill")]
        public static class Pawn_Kill_Cache_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(Pawn __instance)
            {
                if (AutoArmMod.settings?.modEnabled != true) return;
                
                PawnValidationCache.InvalidatePawn(__instance);
            }
        }
        
        /// <summary>
        /// Clear cache on game load
        /// </summary>
        [HarmonyPatch(typeof(Game), "LoadGame")]
        public static class Game_LoadGame_Cache_Patch
        {
            [HarmonyPostfix]
            public static void Postfix()
            {
                // Clear all caches when loading a save
                PawnValidationCache.ClearCache();
                
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug("PawnValidationCache cleared on game load");
                }
            }
        }
        
        /// <summary>
        /// Rebuild cache after game load
        /// </summary>
        [HarmonyPatch(typeof(Map), "FinalizeLoading")]
        public static class Map_FinalizeLoading_Cache_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(Map __instance)
            {
                if (AutoArmMod.settings?.modEnabled != true) return;
                
                // Rebuild lord cache for all pawns on this map
                if (__instance.lordManager?.lords != null)
                {
                    foreach (var lord in __instance.lordManager.lords)
                    {
                        if (lord.ownedPawns != null)
                        {
                            foreach (var pawn in lord.ownedPawns)
                            {
                                if (pawn.IsColonist || pawn.IsSlaveOfColony)
                                {
                                    PawnValidationCache.UpdateLordCache(pawn, lord);
                                }
                            }
                        }
                    }
                }
                
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug($"PawnValidationCache rebuilt for map {__instance.uniqueID}: {PawnValidationCache.GetDebugStats()}");
                }
            }
        }
    }
}