# AutoArm - RimWorld 1.6 Mod

## Overview
AutoArm is a RimWorld mod that automatically equips colonists with better weapons based on their outfit policies. The mod has been updated to use RimWorld's native JobGiver system for better integration with the game's AI.

## Features
- **Outfit-Based Weapon Management**: Add weapons to your colonists' outfit policies just like apparel
- **Smart Weapon Selection**: Colonists automatically pick up better weapons based on quality, DPS, and their skills
- **Emergency Weapon Priority**: Unarmed colonists will prioritize finding a weapon before non-critical tasks like sleeping
- **Role-Based Logic**: 
  - Hunters avoid explosive weapons, melee weapons, and flamethrowers
  - Brawlers strictly refuse ranged weapons
  - Skill-based preference when neither hunter nor brawler
- **Damage Validation**: Avoids non-damaging weapons and melee weapons worse than fists
- **Forced Weapon Support**: 
  - Player right-click equip = Forced (won't switch automatically)
  - Player drops weapon = Not forced anymore
  - Colonist loses weapon (downed/incapacitated) = Not forced anymore
- **Combat Awareness**: Reduced search radius during combat to prevent colonists from running into danger
- **Performance Optimized**: Uses randomized check intervals to spread processing load
- **Mod Compatibility**: Detects and avoids heavy weapons from other mods

## How It Works

### JobGiver System
The mod extends `JobGiver_PickUpOpportunisticWeapon` to create custom job givers:

1. **Emergency Weapon Acquisition** (`JobGiver_GetWeaponEmergency`)
   - High priority for unarmed or poorly armed colonists
   - Runs before satisfaction needs (sleep, joy) but after critical tasks
   - Larger search radius (50 tiles) for finding any weapon
   - Won't interrupt: tending, rescue, firefighting, or player-forced jobs

2. **Better Weapon Selection** (`JobGiver_PickUpBetterWeapon`)
   - Lower priority for upgrading existing weapons
   - Only runs if weapons are allowed in outfit
   - Scores weapons based on multiple factors
   - Smaller search radius in combat (8 tiles) vs normal (30 tiles)

### ThinkTree Integration
The mod patches the colonist ThinkTree to add:
- `ThinkNode_ConditionalUnarmedOrPoorlyArmed`: Checks if pawn needs a weapon urgently
- `ThinkNode_ConditionalWeaponsInOutfit`: Only runs if weapons are allowed in the outfit
- Two injection points: high priority for emergencies, normal priority for upgrades

### Weapon Scoring
Weapons are scored based on:
- **Damage capability** (must harm enemies - non-damaging weapons get -1000 score)
- **Quality** (Normal, Good, Excellent, etc.)
- **Condition** (hit points percentage)
- **DPS** (both ranged and melee)
- **Colonist skills** (Shooting/Melee level)
- **Role restrictions**:
  - Hunters: -1000 for explosive/melee/flamethrower weapons
  - Brawlers: -2000 for ranged weapons, +100 for melee
- **Skill preference**: Bonus for weapons matching higher skill
- **Melee threshold**: -100 for melee weapons below 2.0 DPS
- **Market value** (minor factor)

## Installation
1. Subscribe to the mod or download it
2. Place in your RimWorld/Mods folder
3. Enable in mod list
4. Load after Core and Harmony

## Usage
1. Open a colonist's outfit policy
2. Navigate to the Weapons category (now under Apparel)
3. Allow/disallow specific weapons or weapon categories
4. Colonists will automatically equip allowed weapons when available

### Priority System
- **Unarmed colonists** will look for weapons before sleeping or recreation
- **Armed colonists** will only upgrade weapons during downtime
- **Critical tasks** (tending, rescue, firefighting) are never interrupted

## Technical Details

### File Structure
```
AutoArm/
├── About/
│   └── About.xml
├── Assemblies/
│   └── AutoArm.dll (after building)
├── Patches/
│   └── AutoArm_Patches.xml (XML patches for ThinkTree)
└── Source/
    └── AutoArm/
        ├── WeaponAutoEquip.cs (Main JobGiver and logic)
        ├── ThinkTreePatches.cs (Harmony patches for injection)
        ├── WeaponTabInjector.cs (Weapon category UI)
        ├── WeaponThingFilterUtility.cs (Helper utilities)
        └── ModInit.cs (Initialization and patches)
```

**Important**: Do NOT create a Defs folder - all XML modifications are done through the Patches folder.

### Compatibility
- Requires Harmony
- Compatible with most weapon mods
- Respects biocodable weapons
- Works with Royalty DLC (conceited pawns)
- Works with Biotech DLC (age restrictions)

## Known Issues
- May conflict with other mods that modify weapon equipping behavior

## Changelog
### Version 1.2
- Added: Hunter role logic - hunters avoid explosive/melee/flamethrower weapons
- Added: Brawler trait enforcement - brawlers absolutely refuse ranged weapons
- Added: Damage validation - weapons must actually harm enemies
- Added: Melee weapon DPS check - avoids weapons worse than fists
- Added: Heavy weapon detection for mod compatibility
- Added: Performance optimization with randomized check intervals
- Added: Temporary colonist/guest exclusion
- Improved: Forced weapon tracking now properly clears when colonists are downed
- Improved: Skill-based weapon preference for non-specialized colonists

### Version 1.1
- Fixed: Weapons in storage containers and stockpiles are now properly detected and can be equipped
- Improved: Search now uses map's weapon lister for better performance

## Credits
- Original concept by Snues
- Updated for RimWorld 1.6 with JobGiver system