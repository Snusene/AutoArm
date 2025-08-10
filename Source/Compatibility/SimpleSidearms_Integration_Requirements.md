# SimpleSidearms Integration Requirements

## Overview
This document outlines the complete requirements and design goals for AutoArm's integration with SimpleSidearms mod. The integration allows AutoArm to automatically manage sidearms while respecting SimpleSidearms' authority over weapon management.

## Core Requirements

### Automatic Sidearm Management
* **We want AutoArm to automatically find and equip sidearms for colonists when better weapons become available.**
* **We want to upgrade existing sidearms to higher-quality versions of the same weapon type while still allowing to upgrade the main weapon to any type if the weapon is not forced.**
* **We must respect SimpleSidearms' weight limits, slot limits, and weapon restrictions that the player has configured, never exceeding their defined carrying capacity.**

### Player Preferences and UI Integration
* **When a player manually forces or prefers a weapon through the SimpleSidearms UI (right-click menu), we must never change that weapon to a different type, only allowing same-type quality upgrades if enabled.**
* **During sidearm swaps, we need to ensure the old weapon ends up at the new weapon's location (not randomly dropped) to mimic vanilla behavior or EquipSecondary.**
* **We need to preserve forced/preferred weapon status through upgrades, so if a player forced a poor-quality knife and we upgrade it to excellent quality, the excellent knife remains forced.**

### Technical Integration
* **We must maintain SimpleSidearms' internal memory state correctly by informing it when weapons are added or removed, preventing desynchronization between what SS thinks the pawn has and what they actually carry.**
* **The swap operation must be atomic and validated beforehand to prevent situations where a colonist drops their old weapon but can't pick up the new one due to weight/slot restrictions.**
* **We must avoid interfering with SimpleSidearms' own weapon switching logic, letting it determine which weapon should be primary after any changes we make.**
* **The AutoArm compatibility file needs to turn off in the settings if any critical reflection fails from SS.**

### AutoArm Integration
* **We must respect AutoArm's outfit policy filters - only equipping sidearms that are allowed by the colonist's current outfit policy, just like clothing and primary weapons.**
* **Sidearm selection must use AutoArm's weapon scoring system (considering DPS, accuracy, range, etc.) to determine what constitutes a "better" weapon, respecting the configurable upgrade threshold setting.**
* **We need to honor AutoArm's cooldown and blacklist systems, preventing colonists from immediately re-picking up weapons they just dropped and avoiding weapons that failed validation.**
* **The system must respect AutoArm's own settings like `allowSidearmUpgrades`, `allowForcedWeaponUpgrades`, and weapon body size requirements (unless children weapons are enabled).**
* **We should integrate with AutoArm's notification system to inform players when sidearms are equipped or upgraded, if notifications are enabled in settings.**

### Special Cases
* **Biocoded/bonded weapons need special handling - automatically forcing bonded weapons in SimpleSidearms when `respectWeaponBonds` is enabled.**
* **The system must differentiate between primary weapon upgrades (any weapon type allowed if not forced) and sidearm upgrades (typically same-type only).**
* **We need to properly mark jobs with `AutoEquipTracker` so AutoArm knows these are automatic equipment changes, not player-initiated ones.**

## Implementation Details

### Key Methods
- `FindBestSidearmJob()` - Main entry point for finding and creating sidearm jobs
- `CanPerformSidearmSwap()` - Pre-validates that a swap will succeed
- `JobDriver_SwapSidearm` - Handles the atomic swap operation
- `IsWeaponTypeForced()` - Checks SimpleSidearms forced/preferred status
- `InformOfDroppedSidearm()` / `InformOfAddedSidearm()` - Maintains SS memory state

### Critical Validation Points
1. **Pre-job creation**: Validate weapon passes outfit filter and SS restrictions
2. **Pre-swap**: Ensure inventory has space and weight capacity
3. **During swap**: Atomic operation to prevent partial state
4. **Post-swap**: Update SS memory and maintain forced status

### Settings That Affect Behavior
- `allowSidearmUpgrades` - Whether to upgrade existing sidearms
- `allowForcedWeaponUpgrades` - Whether to upgrade forced/preferred weapons
- `weaponUpgradeThreshold` - How much better a weapon must be to trigger upgrade
- `showNotifications` - Whether to notify player of sidearm changes
- `respectWeaponBonds` - Whether to auto-force bonded weapons
- `debugLogging` - Extensive logging for troubleshooting

## Testing Checklist
- [ ] New sidearm pickup when under weight/slot limit
- [ ] Sidearm upgrade (same type, better quality)
- [ ] Rejection when at weight/slot limit
- [ ] Forced weapon preservation through upgrade
- [ ] Preferred weapon preservation
- [ ] Outfit filter compliance
- [ ] Biocoded weapon handling
- [ ] Proper weapon placement when swapping
- [ ] SS memory state consistency
- [ ] Graceful handling when SS reflection fails

## Known Limitations
- Cannot bypass SimpleSidearms' weight/slot restrictions
- Forced weapons can only be changed through SS UI
- Some SS internal state changes may not be immediately visible to AutoArm
- Requires reflection to access SS internals (may break with SS updates)

## Future Improvements
- Direct API integration if SimpleSidearms exposes public methods
- Better weight calculation prediction before attempting swaps
- Support for dual-wielding compatibility (if SS adds this feature)
- More intelligent sidearm composition recommendations