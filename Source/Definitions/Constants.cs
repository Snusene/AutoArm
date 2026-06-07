
namespace AutoArm.Definitions
{
    internal static class Constants
    {

        public const float DefaultWeaponTypePreference = 0.0f;

        public const float WeaponPreferenceRangedBase = 2.5f;

        public const float WeaponPreferenceMeleeBase = 2f;
        public const float WeaponPreferenceAdjustment = 1.25f;

        public const float WeaponUpgradeThreshold = 1.05f;

        public const float WeaponUpgradeThresholdMin = 1.01f;
        public const float WeaponUpgradeThresholdMax = 1.50f;

        public const float ScoreEpsilon = 0.5f;


        public const float CombatScoreMultiplier = 10f;


        public const float SituationalWeaponModifier = 0.3f;

        public const float DamagedWeaponPenalty = 0.95f;


        public const float HunterRangedBonus = 500f;

        public const float PersonaWeaponMultiplier = 1.2f;

        public const float SkillBonusBase = 30f;

        public const float SkillBonusGrowthRate = 1.50f;
        public const float SkillBonusMax = 500f;
        public const float SkillMismatchMultiplier = 0.70f;
        public const float OutfitFilterDisallowedPenalty = -1000f;

        public const int MaxWeaponCacheSize = 10000;



        public const int GridCellSize = 10;


        public const int MemoryCleanupInterval = 2500;

        public const int CleanupPerformanceWarningMs = 100;
        public const int CleanupPerformanceLogMs = 50;


        public const int DefaultDropIgnoreTicks = 300;

        public const int LongDropCooldownTicks = 600;
        public const int ExtendedDropCooldownTicks = 1200;
        public const int InventoryPurgeCooldownTicks = 1800;

        public const int WeaponEquipCooldownTicks = 60;

        public const int WeaponBlacklistDuration = 600;

        public const int EmergencyJobExpiry = -1;



        public const int ExcludedItemReportInterval = 3600;


        public const float ChildMinAgeLimit = 0f;

        public const float ChildMaxAgeLimit = 18f;
        public const int ChildDefaultMinAge = 13;


        public const int MaxThinkTreeSearchDepth = 20;

        public const int MinPrioritySorterNodes = 10;
        public const int MaxThinkTreeRetryAttempts = 3;


        public const int StandardCacheDuration = 2500;
        public const int ShortCacheDuration = 600;

        // SimpleSidearms integration
        public const int SSUpgradeCheckCooldown = 250;
        public const int SSMaxPawnCacheSize = 100;
        public const int SSInactivePawnTimeout = 18000;

        public const int MaxPawnRecords = 100;

        public const int MaxJobRecords = 50;


        public const int UnusualCleanupTotal = 1500;

        public const int UnusualCleanupScores = 2000;


        public const int MaxSkipEvaluationTicks = 900;


        public const float UI_LINE_HEIGHT = 30f;

        public const float UI_CHECKBOX_SIZE = 20f;
        public const float UI_TAB_BUTTON_HEIGHT = 30f;
        public const float UI_CONTENT_PADDING = 10f;
        public const float UI_SECTION_GAP = 20f;
        public const float UI_SMALL_GAP = 12f;
        public const float UI_TINY_GAP = 6f;
        public const float UI_RESET_BUTTON_WIDTH = 150f;
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
