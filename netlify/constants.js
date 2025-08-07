// Auto-generated from WeaponConstants.cs - 2025-08-07 02:19
// Run extract-constants.ps1 to regenerate

window.C = {
    // Core multipliers
    POWER_CREEP_THRESHOLD: 30,
    POWER_CREEP_EXCESS_MULTIPLIER: 0.5,
    RANGED_MULTIPLIER: 10,  // From AutoArmMod.GetRangedMultiplier()
    MELEE_MULTIPLIER: 8,    // From AutoArmMod.GetMeleeMultiplier()
    
    // Trait/role bonuses
    brawlerBonus: 500,
    hunterBonus: 500,
    
    // Persona weapons
    personaWeaponBonus: 25,
    personaWeaponReject: -1000,
    
    // Skill scoring
    skillBaseBonus: 30,
    skillGrowthRate: 1.15,
    skillCap: 500,
    skillPenaltyMultiplier: 0.5,
    
    // Situational weapons
    situationalWeaponCap: 80,
    
    // Burst bonus
    burstBonusMultiplier: 2.5,
    
    // Quality scoring
    qualityScoreFactor: 0.05,
    qualityScoreBase: 0.95,
    
    // Sidearm bonuses
    firstRangedBonus: 1.5,
    firstMeleeBonus: 1.5,
    duplicateWeaponPenalty: 0.1,
    
    // Odyssey unique weapons
    odysseyUniqueBaseBonus: 50,
    odysseyUniqueTraitBonus: 100,
    
    // Range scoring (multiplier, bonus)
    rangeScores: [
    { maxRange: 10, multiplier: 0.30, bonus: 0 },
    { maxRange: 14, multiplier: 0.55, bonus: 0 },
    { maxRange: 18, multiplier: 0.65, bonus: 0 },
    { maxRange: 22, multiplier: 0.90, bonus: 2 },
    { maxRange: 25, multiplier: 0.95, bonus: 5 },
    { maxRange: 30, multiplier: 1.0, bonus: 10 },
    { maxRange: 35, multiplier: 1.0, bonus: 19 },
    { maxRange: 999, multiplier: 1.02, bonus: 22 }
    ],
    
    // Armor penetration thresholds
    apThresholds: [
    { threshold: 0.75, score: 100 },
    { threshold: 0.52, score: 70 },
    { threshold: 0.40, score: 50 },
    { threshold: 0.20, score: 30 },
    { threshold: 0.15, multiplier: 20, isLowAP: true },
    { threshold: 0, multiplier: 90, isDefault: true }
    ],
    
    // DPS calculation constants
    ticksPerSecond: 60  // RimWorld constant
};

// Helper functions matching C# logic
window.C.getCombinedRangeScore = function(range) {
    for (const score of C.rangeScores) {
        if (range < score.maxRange) {
            return { multiplier: score.multiplier, bonus: score.bonus };
        }
    }
    // Return last entry (highest range)
    const last = C.rangeScores[C.rangeScores.length - 1];
    return { multiplier: last.multiplier, bonus: last.bonus };
};

window.C.calculateArmorPenetrationScore = function(ap) {
    for (const threshold of C.apThresholds) {
        if (threshold.isLowAP && ap < threshold.threshold) {
            return ap * threshold.multiplier;
        } else if (threshold.isDefault) {
            return ap * threshold.multiplier;
        } else if (ap >= threshold.threshold) {
            return threshold.score;
        }
    }
    return 0;
};

// Calculate ranged DPS matching C# GetRangedDPS
window.C.calculateRangedDPS = function(damage, warmup, cooldown, burstCount, burstDelay) {
    const timePerBurst = warmup + cooldown + (burstCount - 1) * burstDelay;
    return (damage * burstCount) / timePerBurst;
};

// Power creep adjustment matching C# logic
window.C.adjustForPowerCreep = function(dps) {
    if (dps <= C.POWER_CREEP_THRESHOLD) {
        return dps;
    }
    const excess = dps - C.POWER_CREEP_THRESHOLD;
    return C.POWER_CREEP_THRESHOLD + (excess * C.POWER_CREEP_EXCESS_MULTIPLIER);
};

// Quality score calculation
window.C.getQualityMultiplier = function(qualityLevel) {
    // qualityLevel: 0=Awful, 1=Poor, 2=Normal, 3=Good, 4=Excellent, 5=Masterwork, 6=Legendary
    return C.qualityScoreBase + (qualityLevel * C.qualityScoreFactor);
};
