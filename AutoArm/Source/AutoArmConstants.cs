namespace AutoArm
{
    public static class AutoArmConstants
    {
        // Weapon evaluation
        public const float WeaponUpgradeThreshold = 1.05f; // 5% improvement needed
        public const float WeaponTypeSwitchThreshold = 1.5f; // 50% improvement to switch types
        public const float MajorUpgradeThreshold = 1.15f; // 15% improvement for job interruption

        // Scoring weights
        public const float QualityScoreMultiplier = 15f;
        public const float ConditionScoreMultiplier = 20f;
        public const float SkillScoreMultiplier = 3f;
        public const float TechLevelScoreMultiplier = 5f;
        public const float RangeScoreMultiplier = 0.5f;
        public const float InfusionScorePerInfusion = 25f;

        // Performance
        public const int WeaponCacheLifetimeTicks = 500;
        public const int MaxWeaponsToConsider = 200;
        public const float MaxWeaponSearchDistance = 50f;
        public const float MaxWeaponSearchDistanceSquared = MaxWeaponSearchDistance * MaxWeaponSearchDistance;

        // Timing
        public const int CleanupInterval = 10000;

        // Weapon scores
        public const float BaseWeaponScore = 100f;
        public const float PoorMeleeWeaponPenalty = -100f;
        public const float PoorMeleeWeaponThreshold = 2.0f;
        public const float BrawlerMeleeBonus = 200f;
        public const float BrawlerRangedPenalty = -2000f;
    }
}