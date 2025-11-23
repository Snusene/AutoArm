
namespace AutoArm.Definitions
{
    /// <summary>
    /// Constants
    /// </summary>
    public static class Constants
    {

        public const float DefaultWeaponTypePreference = 0.0f;

        public const float WeaponPreferenceRangedBase = 2.5f;

        public const float WeaponPreferenceMeleeBase = 2f;
        public const float WeaponPreferenceAdjustment = 1.25f;

        public const float WeaponUpgradeThreshold = 1.05f;

        public const float WeaponUpgradeThresholdMin = 1.01f;
        public const float WeaponUpgradeThresholdMax = 1.50f;


        public const float MarketValueCubeRootMultiplier = 21f;

        public const float WeaponFallbackMarketValue = 50f;


        public const float RoughScoreSameTypeThreshold = 0.9f;

        public const float RoughScoreDifferentTypeThreshold = 0.7f;


        public const float WarmupVerySlowModifier = 0.6f;

        public const float WarmupSlowModifier = 0.8f;
        public const float WarmupVerySlowThreshold = 3.0f;
        public const float WarmupSlowThreshold = 2.0f;

        public const float BurstShotBonus = 0.015f;

        public const float BurstShotBonusMax = 0.15f;

        public const float RangeVeryShortModifier = 0.85f;

        public const float RangeShortModifier = 0.85f;
        public const float RangeLongModifier = 0.9f;
        public const float RangeVeryShortThreshold = 15f;
        public const float RangeShortThreshold = 20f;
        public const float RangeLongThreshold = 35f;

        public const float SituationalWeaponModifier = 0.5f;

        public const float MeleeBaseModifier = 1.0f;

        public const float MeleeFastAttackModifier = 1.1f;
        public const float MeleeHighAPModifier = 1.1f;
        public const float MeleeHighAPThreshold = 0.2f;
        public const float MeleeFastCooldownThreshold = 1.5f;

        public const float RangedHighAPModifier = 1.15f;

        public const float RangedMediumAPModifier = 1.08f;
        public const float RangedHighAPThreshold = 0.35f;
        public const float RangedMediumAPThreshold = 0.25f;

        public const float LowDamagePerShotModifier = 0.95f;

        public const float LowDamagePerShotThreshold = 11f;

        public const float PistolWarmupThreshold = 0.6f;
        public const float PistolRangeThreshold = 25f;
        public const float PistolCategoryModifier = 0.75f;


        public const float HunterRangedBonus = 500f;

        public const float PersonaWeaponBonus = 50f;

        public const float SkillBonusBase = 30f;

        public const float SkillBonusGrowthRate = 1.50f;
        public const float SkillBonusMax = 500f;
        public const float SkillMismatchMultiplier = 0.85f;
        public const float WrongWeaponTypePenalty = 0.5f;
        public const float OutfitFilterDisallowedPenalty = -1000f;

        public const float HPBelowMinimumBasePenalty = -10f;

        public const float HPDeficitScalingFactor = 40f;
        public const float HPBelowMinimumMaxPenalty = -30f;

        public const float OdysseyUniqueBaseBonus = 50f;

        public const float OdysseyUniqueTraitBonus = 25f;


        public const int WeaponScoreCacheLifetime = int.MaxValue;


        public const int MaxWeaponCacheSize = 10000;



        public const int GridCellSize = 10;


        public const int MemoryCleanupInterval = 2500;

        public const int CleanupPerformanceWarningMs = 100;
        public const int CleanupPerformanceLogMs = 50;


        public const int DefaultDropIgnoreTicks = 300;

        public const int LongDropCooldownTicks = 600;

        public const int SwapDropCooldownTicks = 600;

        public const int JobRetentionTicks = 2500;

        public const int WeaponEquipCooldownTicks = 60;

        public const int WeaponBlacklistDuration = 600;


        public const float EmergencyWeaponPriority = 6.9f;

        public const float WeaponUpgradePriority = 5.6f;
        public const float DefaultThinkNodePriority = 0f;


        public const int EmergencyJobExpiry = -1;



        public const int ExcludedItemReportInterval = 3600;


        public const int ChildMinAgeLimit = 0;

        public const int ChildMaxAgeLimit = 18;
        public const int ChildDefaultMinAge = 13;


        public const int PawnEvaluationFailureThreshold = 10;

        public const int PawnEvaluationCriticalThreshold = 5;
        public const int PawnEvaluationExcessiveThreshold = 50;


        public const int MaxThinkTreeSearchDepth = 20;

        public const int MinPrioritySorterNodes = 10;
        public const int MaxThinkTreeRetryAttempts = 3;


        public const int StandardCacheDuration = 2500;
        public const int ShortCacheDuration = 600;

        public const int DefaultGenericCacheDuration = 600;

        public const int MaxPawnRecords = 100;

        public const int MaxJobRecords = 50;


        public const int UnusualCleanupTotal = 100;

        public const int UnusualCleanupScores = 200;


        public const int MaxPawnsPerTick = 1;

        public const int MaxSkipEvaluationTicks = 900;


        public const float UI_LINE_HEIGHT = 30f;

        public const float UI_CHECKBOX_SIZE = 20f;
        public const float UI_LABEL_WIDTH = 250f;
        public const float UI_TAB_BUTTON_HEIGHT = 30f;
        public const float UI_CONTENT_PADDING = 10f;
        public const float UI_SECTION_GAP = 20f;
        public const float UI_SMALL_GAP = 12f;
        public const float UI_TINY_GAP = 6f;
        public const float UI_RESET_BUTTON_WIDTH = 100f;
        public const float UI_RESET_BUTTON_HEIGHT = 30f;

        public const float DEBUG_WINDOW_WIDTH = 600f;

        public const float DEBUG_WINDOW_HEIGHT = 500f;

        public const float UI_GRAY_ALPHA = 0.7f;

        public const float UI_BOX_ALPHA = 0.3f;
        public const float UI_TEXT_ALPHA = 0.8f;


        public const float PREF_STRONG_MELEE_THRESHOLD = -0.75f;
        public const float PREF_MODERATE_MELEE_THRESHOLD = -0.35f;
        public const float PREF_SLIGHT_MELEE_THRESHOLD = -0.10f;
        public const float PREF_BALANCED_THRESHOLD = 0.10f;
        public const float PREF_SLIGHT_RANGED_THRESHOLD = 0.35f;
        public const float PREF_MODERATE_RANGED_THRESHOLD = 0.75f;
    }
}
