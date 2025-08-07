// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: Shared constants for everything
// Purpose: Centralize threshold values for easier maintenance and testing

namespace AutoArm.Definitions
{
    /// <summary>
    /// Centralized constants for all constants
    /// </summary>
    public static class Constants
    {
        // =======================================
        // WEAPON UPGRADE THRESHOLDS
        // =======================================

        // When is a weapon worth switching to?
        public const float WeaponUpgradeThreshold = 1.05f;            // 5% better to replace weapon (default)

        public const float WeaponUpgradeThresholdMin = 1.01f;         // Minimum allowed threshold (1% better)
        public const float WeaponUpgradeThresholdMax = 1.50f;         // Maximum allowed threshold (50% better)

        // =======================================
        // WEAPON SCORING - BASE VALUES
        // =======================================

        // Quality modifiers
        public const float QualityScoreFactor = 0.05f;                // Quality modifier per level

        public const float QualityScoreBase = 0.95f;                  // Base quality multiplier

        // DPS and power scaling
        public const float PowerCreepThreshold = 30f;                 // DPS cap for diminishing returns

        public const float PowerCreepExcessMultiplier = 0.5f;         // Multiplier for DPS above threshold
        public const float SituationalWeaponCap = 80f;                // Score cap for grenades/launchers
        public const float BurstBonusMultiplier = 2.5f;               // Burst shot bonus calculation

        // =======================================
        // WEAPON SCORING - SKILL & TRAIT BONUSES
        // =======================================

        // Trait and role bonuses
        public const float BrawlerMeleeBonus = 500f;                  // Brawler trait melee bonus

        public const float HunterRangedBonus = 500f;                  // Hunter work type ranged bonus
        public const float PersonaWeaponBonus = 25f;                  // Bonus for bonded persona weapon

        // Skill scoring
        public const float SkillBonusBase = 30f;                      // Base bonus for 1 skill level difference

        public const float SkillBonusGrowthRate = 1.15f;              // Exponential growth per level
        public const float SkillBonusMax = 500f;                      // Maximum skill bonus
        public const float WrongWeaponTypePenalty = 0.5f;             // Penalty multiplier for mismatched weapon

        // Odyssey unique weapon bonuses
        public const float OdysseyUniqueBaseBonus = 50f;              // Base bonus for unique weapons

        public const float OdysseyUniqueTraitBonus = 100f;            // Bonus per beneficial trait (~2 avg)

        // =======================================
        // WEAPON SCORING - RANGE VALUES
        // =======================================

        // Range thresholds
        public const float RangeVeryShort = 10f;                      // < 10 range

        public const float RangeShort = 14f;                          // 10-14 range
        public const float RangeMediumShort = 18f;                    // 14-18 range
        public const float RangeMedium = 22f;                         // 18-22 range
        public const float RangeMediumLong = 25f;                     // 22-25 range
        public const float RangeLong = 30f;                           // 25-30 range
        public const float RangeVeryLong = 35f;                       // 30-35 range

        // Range score multipliers
        public const float RangeMultiplierVeryShort = 0.30f;

        public const float RangeMultiplierShort = 0.55f;
        public const float RangeMultiplierMediumShort = 0.65f;
        public const float RangeMultiplierMedium = 0.90f;
        public const float RangeMultiplierMediumLong = 0.95f;
        public const float RangeMultiplierLong = 1.0f;
        public const float RangeMultiplierVeryLong = 1.02f;

        // Range score bonuses
        public const float RangeBonusMedium = 2f;

        public const float RangeBonusMediumLong = 5f;
        public const float RangeBonusLong = 10f;
        public const float RangeBonusVeryLong = 19f;
        public const float RangeBonusExtreme = 22f;

        // =======================================
        // WEAPON SCORING - ARMOR PENETRATION
        // =======================================

        // Armor penetration thresholds
        public const float APMarineArmor = 0.75f;                     // Can penetrate marine armor

        public const float APFlakVest = 0.52f;                        // Can penetrate flak vest
        public const float APReconArmor = 0.40f;                      // Can penetrate recon armor
        public const float APBasicArmor = 0.20f;                      // Can penetrate basic armor
        public const float APVeryLow = 0.15f;                         // Very low AP threshold

        // Armor penetration scores
        public const float APScoreMarine = 100f;                      // Score for marine-penetrating AP

        public const float APScoreFlak = 70f;                         // Score for flak-penetrating AP
        public const float APScoreRecon = 50f;                        // Score for recon-penetrating AP
        public const float APScoreBasic = 30f;                        // Score for basic armor AP
        public const float APMultiplierVeryLow = 20f;                 // Multiplier for very low AP
        public const float APMultiplierNormal = 90f;                  // Normal AP multiplier

        // =======================================
        // SEARCH & PERFORMANCE SETTINGS
        // =======================================

        // Search parameters
        public const float DefaultSearchRadius = 60f;                 // Default weapon search radius

        public const int BaseWeaponSearchLimit = 20;                  // Base number of weapons to evaluate
        public const float RaidApproachDistance = 80f;                // Distance to consider raid approaching

        // Progressive search settings
        public const float SearchRadiusClose = 15f;                   // First search radius

        public const float SearchRadiusMedium = 30f;                  // Second search radius
        public const int CloseWeaponsNeeded = 15;                     // Weapons needed at close range
        public const int MediumWeaponsNeeded = 30;                    // Weapons needed at medium range

        // Map size thresholds
        public const int LargeMapSize = 200;                          // Large map threshold

        public const int MediumLargeMapSize = 150;                    // Medium-large map threshold
        public const int LargeMapBonusWeapons = 10;                   // Extra weapons to check on large maps
        public const int MediumMapBonusWeapons = 5;                   // Extra weapons on medium-large maps

        // Colony size search limits
        public const int LargeColonySize = 50;                        // Large colony threshold

        public const int MediumColonySize = 35;                       // Medium colony threshold
        public const int MinSearchLimitLargeColony = 15;              // Min weapons to check in large colonies
        public const int MinSearchLimitMediumColony = 20;             // Min weapons in medium colonies

        // =======================================
        // CACHING & MEMORY MANAGEMENT
        // =======================================

        // Cache durations (in ticks)
        public const int WeaponScoreCacheLifetime = 1000;             // ~16 seconds

        public const int RaidStatusCacheDuration = 600;               // 10 seconds
        public const int PawnScoreCacheLifetime = 1200;               // ~20 seconds pawn-specific cache
        public const int BaseCacheLifetime = 10000;                   // ~167 seconds base cache lifetime

        // Cache size limits
        public const int MaxWeaponCacheSize = 1000;                   // Max cached weapon scores

        public const int MaxScoreCacheEntries = 5000;                 // Max cached score entries
        public const int MaxWeaponsPerCache = 1500;                   // Max weapons to cache per map
        public const int PooledCollectionLimit = 10;                  // Max pooled collections to keep

        // Cache rebuild settings
        public const int MaxWeaponsPerRebuild = 100;                  // Weapons processed per rebuild tick

        public const int RebuildDelayTicks = 15;                      // Delay between rebuild chunks
        public const int GridCellSize = 10;                           // Spatial index grid cell size

        // Cache cleanup settings
        public const int CacheCleanupThreshold = 2;                   // Lifetime multiplier for cleanup

        public const int CacheStatsLogInterval = 6000;                // Log cache stats every 100 seconds

        // Memory cleanup intervals
        public const int MemoryCleanupInterval = 2500;                // ~42 seconds between cleanups

        public const int CleanupPerformanceWarningMs = 100;           // Warn if cleanup takes > 100ms
        public const int CleanupPerformanceLogMs = 50;                // Log if cleanup takes > 50ms

        // =======================================
        // TRACKING & COOLDOWNS
        // =======================================

        // DroppedItemTracker durations (ticks)
        public const int DefaultDropIgnoreTicks = 300;                // 5 seconds ignore after drop

        public const int LongDropCooldownTicks = 600;                 // 10 seconds for primary upgrades

        // AutoEquipTracker settings
        public const int JobRetentionTicks = 2500;                    // ~42 seconds - enough time to walk across most maps

        // =======================================
        // TICK STAGGERING INTERVALS
        // =======================================

        // Check intervals for different pawn states
        public const int EmergencyCheckInterval = 15;                     // Check every 15 ticks (4 times per second) - balanced for performance with multiple unarmed

        // Armed check interval scales with colony size for automatic performance optimization
        // Formula: colony_size * ArmedIntervalPerColonist (e.g., 10 colonists = 0.5 sec, 30 colonists = 1.5 sec)
        public const int ArmedIntervalPerColonist = 3;                    // 0.05 seconds per colonist (3 ticks)

        public const int ArmedIntervalMin = 15;                           // Minimum 0.25 seconds - same as unarmed!
        public const int ArmedIntervalMax = 150;                          // Maximum 2.5 seconds for huge colonies

        // Weapon blacklist settings
        public const int WeaponBlacklistDuration = 3600;              // 60 seconds - how long weapons stay blacklisted

        // =======================================
        // THINK TREE & JOB PRIORITIES
        // =======================================

        // Think tree priorities
        public const float EmergencyWeaponPriority = 6.9f;            // Emergency priority for unarmed pawns

        public const float WeaponUpgradePriority = 5.6f;              // Priority for weapon upgrade checks
        public const float DefaultThinkNodePriority = 0f;             // Default priority when conditions not met

        // Early game aggressive interruption
        public const int EarlyGameDuration = 18000;                   // 5 minutes (300 seconds) - aggressive idle interruption

        // Job expiry settings
        public const int EmergencyJobExpiry = -1;                     // Never expire emergency weapon pickups

        // =======================================
        // LOGGING & DEBUG INTERVALS
        // =======================================

        // Debug logging intervals (in ticks)
        public const int DebugLogInterval = 300;                      // 5 seconds - general debug logging interval

        public const int ExcludedItemReportInterval = 3600;           // 60 seconds - report excluded items

        // =======================================
        // VALIDATION & ERROR THRESHOLDS
        // =======================================

        // Pawn evaluation failure thresholds
        public const int PawnEvaluationFailureThreshold = 10;         // Max failures before skipping pawn

        public const int PawnEvaluationCriticalThreshold = 5;         // Failures before logging critical warning
        public const int PawnEvaluationExcessiveThreshold = 50;       // Excessive failures for cleanup

        // =======================================
        // CHILD AGE RESTRICTIONS (Biotech)
        // =======================================

        // Age limits for children equipping weapons
        public const int ChildMinAgeLimit = 3;                        // Minimum age slider value

        public const int ChildMaxAgeLimit = 18;                       // Maximum age slider value (adult threshold)
        public const int ChildDefaultMinAge = 13;                     // Default minimum age for weapon equipping
        public const float DefaultWeaponTypePreference = 0.11f;       // Slight ranged preference by default

        // =======================================
        // WEAPON PREFERENCE MULTIPLIERS
        // =======================================

        // Base multipliers for weapon type preferences
        public const float WeaponPreferenceRangedBase = 10f;          // Base ranged multiplier at balanced preference

        public const float WeaponPreferenceMeleeBase = 8f;            // Base melee multiplier at balanced preference
        public const float WeaponPreferenceAdjustment = 5f;           // Adjustment per preference point

        // =======================================
        // THINK TREE INJECTION
        // =======================================

        // Think tree search parameters
        public const int MaxThinkTreeSearchDepth = 20;                // Maximum depth for recursive think tree search

        public const int MinPrioritySorterNodes = 10;                 // Minimum nodes to identify priority sorter
        public const int MaxThinkTreeRetryAttempts = 3;               // Maximum attempts to inject into think tree

        // =======================================
        // SEARCH REDUCTION PARAMETERS
        // =======================================

        // Colony size search reductions
        public const int LargeColonySearchReduction = 10;             // Reduce search limit by this for large colonies

        public const int MediumColonySearchReduction = 5;             // Reduce search limit by this for medium colonies

        // =======================================
        // GENERIC CACHE SETTINGS
        // =======================================

        // Generic cache parameters
        public const int DefaultGenericCacheDuration = 600;           // 10 seconds - default duration for generic cache entries

        public const int MainCleanupInterval = 2500;                  // ~42 seconds - main cleanup cycle interval
        public const int MaxPawnRecords = 100;                        // Maximum pawn records to keep in dictionaries
        public const int MaxJobRecords = 50;                          // Maximum job records to keep

        // =======================================
        // CLEANUP THRESHOLDS
        // =======================================

        // Unusual cleanup detection thresholds
        public const int UnusualCleanupTotal = 100;                   // Total items cleaned threshold for warning

        public const int UnusualCleanupScores = 200;                  // Weapon scores cleaned threshold for warning

        // =======================================
        // PERFORMANCE OPTIMIZATIONS
        // =======================================

        // Early exit optimization
        public const int EarlyExitWeaponCount = 5;                        // Stop searching after finding this many good weapons

        public const float EarlyExitScoreThreshold = 1.2f;                // Stop if found weapon is 20% better

        // Batch processing
        // Simple dynamic scaling - armed check interval = colony_size * 0.05 seconds:
        // - 5 colonists: Each armed colonist checks every 0.25 seconds (15 ticks) - same as unarmed!
        // - 10 colonists: Each armed colonist checks every 0.5 seconds (30 ticks)
        // - 20 colonists: Each armed colonist checks every 1 second (60 ticks)
        // - 30 colonists: Each armed colonist checks every 1.5 seconds (90 ticks)
        // - 50+ colonists: Capped at 2.5 seconds max (150 ticks)
        // - Unarmed always: Check every 15 ticks, max 5 per tick = emergency response under 0.25 seconds
        // Result: Ultra-responsive weapon switching with automatic performance scaling
        public const int MaxPawnsPerTick = 2;                             // Max armed pawns to process per tick

        public const int MaxUnarmedPawnsPerTick = 5;                      // Max unarmed pawns per tick (higher priority but still limited)

        // =======================================
        // WEAPON CATEGORIZATION
        // =======================================

        // Weapon category range thresholds
        public const float CategoryRangeShort = 20f;                  // < 20 range = short range weapon

        public const float CategoryRangeMedium = 35f;                 // 20-35 range = medium range weapon
                                                                      // > 35 range = long range weapon
    }
}