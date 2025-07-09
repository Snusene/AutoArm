# AutoArm Priority System

## Think Tree Priority Order (Simplified)

The mod injects weapon acquisition at two priority levels:

### High Priority (Emergency Weapon Acquisition)
Runs **before** satisfaction needs but **after** critical tasks:

1. **Self-preservation** (flee from danger, extinguish self)
2. **Critical medical** (tend self if bleeding badly)
3. **→ EMERGENCY WEAPON ACQUISITION ←** (if unarmed or very poorly armed)
4. **Basic needs** (sleep when exhausted, eat when starving)
5. **Recreation/Joy** (when mood is very low)

### Normal Priority (Weapon Upgrades)
Runs during regular work time:

1. **Forced work** (player drafted/forced jobs)
2. **Lord duties** (ceremonies, rituals)
3. **Self-tend** (minor injuries)
4. **→ WEAPON UPGRADES ←** (if outfit allows weapons)
5. **Regular work** (based on priorities)
6. **Opportunistic tasks** (hauling, cleaning)

## Examples

### Scenario 1: Unarmed Colonist
- Sarah has no weapon
- She's tired (sleep need at 30%)
- There's a pistol 40 tiles away
- **Result**: Sarah will get the pistol BEFORE going to sleep

### Scenario 2: Poorly Armed During Raid
- Bob has a wooden club
- Raiders are attacking (but 50+ tiles away)
- There's a rifle nearby
- **Result**: Bob will grab the rifle unless enemies are within 15 tiles

### Scenario 3: Well-Armed Colonist
- Alice has a good quality assault rifle
- There's an excellent quality sniper rifle nearby
- She's scheduled for cooking
- **Result**: Alice will only consider the sniper rifle if:
  - It's allowed in her outfit
  - She has downtime from cooking
  - The sniper rifle scores 5% better than her current weapon

### What "Poorly Armed" Means
The mod considers a colonist poorly armed if they have:
- No weapon at all
- A ranged weapon with less than 3 DPS
- A neolithic melee weapon when guns are available
- Improvised weapons (clubs, gladius) when better options exist

### Never Interrupted Tasks
The emergency weapon system will NOT interrupt:
- Tending patients
- Rescue operations  
- Firefighting (self or environment)
- Doctor surgery
- Player-forced jobs
- Active combat
- Sowing/Harvesting (lets them finish)

This ensures colonists prioritize survival and getting armed, but won't abandon critical tasks to do so.