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
        public const float OutfitFilterDisallowedPenalty = -1000f;    // Score penalty for weapons disallowed in outfit filter

        // HP threshold penalties (asymmetric filter behavior)
        public const float HPBelowMinimumBasePenalty = -30f;          // Base penalty for weapons below minimum HP
        public const float HPDeficitScalingFactor = 150f;             // Scaling factor for HP deficit (deficit * this factor)
        public const float HPBelowMinimumMaxPenalty = -60f;           // Maximum penalty cap for very damaged weapons

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
        public const int EmergencyCheckInterval = 60;                     // Check every 60 ticks (1 per second) - balanced for performance

        // Armed check interval scales with colony size for automatic performance optimization
        // Formula: colony_size * ArmedIntervalPerColonist (e.g., 10 colonists = 0.5 sec, 30 colonists = 1.5 sec)
        public const int ArmedIntervalPerColonist = 3;                    // 0.05 seconds per colonist (3 ticks)

        public const int ArmedIntervalMin = 15;                           // Minimum 0.25 seconds - same as unarmed!
        public const int ArmedIntervalMax = 150;                          // Maximum 2.5 seconds for huge colonies

        // Weapon equip cooldown - prevents flip-flopping
        public const int WeaponEquipCooldownTicks = 300;                  // 5 seconds - enough to walk away and start nearby job

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
        // CHILD AGE RESTRICTIONS (Biotech)
        // =======================================

        // Age limits for children equipping weapons
        public const int ChildMinAgeLimit = 0;                        // Minimum age slider value

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
        // VALIDATION & ERROR THRESHOLDS
        // =======================================

        // Pawn evaluation failure thresholds
        public const int PawnEvaluationFailureThreshold = 10;         // Max failures before skipping pawn

        public const int PawnEvaluationCriticalThreshold = 5;         // Failures before logging critical warning
        public const int PawnEvaluationExcessiveThreshold = 50;       // Excessive failures for cleanup

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

        // Batch processing
        public const int MaxPawnsPerTick = 1;                         // Max pawns to process per tick

        public const int ProcessEveryNthTick = 2;                     // Process weapons every Nth tick

        // =======================================
        // WEAPON CATEGORIZATION
        // =======================================

        // Weapon category range thresholds
        public const float CategoryRangeShort = 20f;                  // < 20 range = short range weapon

        public const float CategoryRangeMedium = 35f;                 // 20-35 range = medium range weapon
                                                                      // > 35 range = long range weapon

        // =======================================
        // UI LAYOUT CONSTANTS
        // =======================================

        // Main UI layout
        public const float UI_LINE_HEIGHT = 30f;                      // Standard line height for UI elements

        public const float UI_CHECKBOX_SIZE = 24f;                    // Standard checkbox size
        public const float UI_LABEL_WIDTH = 250f;                     // Standard label width
        public const float UI_TAB_BUTTON_HEIGHT = 30f;                // Height for tab buttons
        public const float UI_CONTENT_PADDING = 10f;                  // Content area padding
        public const float UI_SECTION_GAP = 20f;                      // Gap between sections
        public const float UI_SMALL_GAP = 12f;                        // Small gap between elements
        public const float UI_TINY_GAP = 6f;                          // Tiny gap between elements
        public const float UI_RESET_BUTTON_WIDTH = 100f;              // Reset button width
        public const float UI_RESET_BUTTON_HEIGHT = 30f;              // Reset button height

        // Debug window
        public const float DEBUG_WINDOW_WIDTH = 600f;                 // Debug window initial width

        public const float DEBUG_WINDOW_HEIGHT = 500f;                // Debug window initial height

        // Color values
        public const float UI_GRAY_ALPHA = 0.7f;                      // Gray text alpha value

        public const float UI_BOX_ALPHA = 0.3f;                       // Box background alpha
        public const float UI_TEXT_ALPHA = 0.8f;                      // Hint text alpha

        // =======================================
        // WEAPON PREFERENCE DISPLAY THRESHOLDS
        // =======================================

        public const float PREF_STRONG_MELEE_THRESHOLD = -0.75f;      // <= -0.75 = Strong melee preference
        public const float PREF_MODERATE_MELEE_THRESHOLD = -0.35f;    // <= -0.35 = Moderate melee preference
        public const float PREF_SLIGHT_MELEE_THRESHOLD = -0.10f;      // <= -0.10 = Slight melee preference
        public const float PREF_BALANCED_THRESHOLD = 0.10f;           // < 0.10 = Balanced
        public const float PREF_SLIGHT_RANGED_THRESHOLD = 0.35f;      // < 0.35 = Slight ranged preference
        public const float PREF_MODERATE_RANGED_THRESHOLD = 0.75f;    // < 0.75 = Moderate ranged preference
                                                                      // >= 0.75 = Strong ranged preference

        // =======================================
        // TESTING & DEBUG
        // =======================================

        public const int TEST_RESULT_PREVIEW_COUNT = 10;              // Number of items to show in test previews
        public const int TEST_WEAPON_NEARBY_COUNT = 3;                // Number of nearby weapons to show in tests
        public const float TEST_COLOR_GREEN_R = 0.8f;                 // Green color R component
        public const float TEST_COLOR_GREEN_G = 1.0f;                 // Green color G component
        public const float TEST_COLOR_GREEN_B = 0.8f;                 // Green color B component
    }
}