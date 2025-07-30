// weapons-data.js - AutoArm Weapon Database
// This file contains all weapon data for the AutoArm balance analyzer

// Wrap everything in an IIFE to avoid redeclaration issues
(function () {
    'use strict';

    // Check if already loaded
    if (window.weaponData) {
        console.log('weapons-data.js already loaded, skipping...');
        return;
    }

    // Typical weapon data from RimWorld (base values at Normal quality)
    // AP values are calculated as damage * 0.015 for most weapons unless specified
    // Accuracy values are for touch/short/medium/long ranges
    const vanillaRangedWeapons = [
        // Early-game/Tribal
        { name: "Short Bow", dps: 4.57, range: 15.9, damage: 10, burst: 1, acc: [0.50, 0.62, 0.41, 0.22] },
        { name: "Recurve Bow", dps: 5.72, range: 19.9, damage: 13, burst: 1, acc: [0.60, 0.77, 0.64, 0.41] },
        { name: "Greatbow", dps: 6.26, range: 21.9, damage: 17, burst: 1, acc: [0.60, 0.79, 0.72, 0.49] },
        { name: "Pila", dps: 6.95, range: 11.9, damage: 16, burst: 1, acc: [0.55, 0.71, 0.41, 0.22] },

        // Industrial weapons
        { name: "Revolver", dps: 6.21, range: 25.9, damage: 12, burst: 1, acc: [0.80, 0.75, 0.45, 0.35] },
        { name: "Autopistol", dps: 9.06, range: 25.9, damage: 9, burst: 1, acc: [0.80, 0.70, 0.40, 0.30] },
        { name: "Pump Shotgun", dps: 13.88, range: 15.9, damage: 20, burst: 1, acc: [0.80, 0.87, 0.77, 0.64] },
        { name: "Machine Pistol", dps: 10.59, range: 25.9, damage: 6, burst: 3, acc: [0.60, 0.50, 0.35, 0.25] },
        { name: "Bolt-Action Rifle", dps: 7.51, range: 36.9, damage: 18, burst: 1, acc: [0.65, 0.80, 0.90, 0.80] },
        { name: "Assault Rifle", dps: 11.23, range: 30.9, damage: 11, burst: 3, acc: [0.65, 0.65, 0.65, 0.55] },
        { name: "Sniper Rifle", dps: 9.24, range: 44.9, damage: 25, burst: 1, acc: [0.50, 0.70, 0.86, 0.88] },
        { name: "Chain Shotgun", dps: 18.73, range: 12.9, damage: 15, burst: 3, acc: [0.57, 0.64, 0.55, 0.45] },
        { name: "Heavy SMG", dps: 13.26, range: 22.9, damage: 6, burst: 5, acc: [0.50, 0.45, 0.35, 0.25] },
        { name: "LMG", dps: 11.89, range: 25.9, damage: 11, burst: 6, acc: [0.35, 0.45, 0.55, 0.35] },

        // Spacer weapons
        { name: "Charge Rifle", dps: 16.46, range: 27.9, damage: 16, ap: 0.35, burst: 3, acc: [0.65, 0.70, 0.70, 0.65] }, // Updated in 1.6.4518
        { name: "Charge Lance", dps: 11.79, range: 29.9, damage: 30, ap: 0.45, burst: 1, acc: [0.65, 0.85, 0.85, 0.85] },
        { name: "Minigun", dps: 21.82, range: 30.9, damage: 5, burst: 25, acc: [0.20, 0.25, 0.30, 0.25], forcedMiss: 2.5 },

        // Special weapons (DPS values are estimates - these weapons work differently)
        { name: "Incendiary Launcher", dps: 4.5, range: 15, damage: 10, burst: 1, ap: 0.05, situational: true, acc: [0.50, 0.50, 0.50, 0.50], forcedMiss: 3.0 },
        { name: "EMP Launcher", dps: 3.5, range: 14, damage: 50, burst: 1, ap: 0.0, situational: true, acc: [0.50, 0.50, 0.50, 0.50], forcedMiss: 3.0 },
        { name: "Smoke Launcher", dps: 2, range: 14, damage: 10, burst: 1, ap: 0.0, situational: true, acc: [0.50, 0.50, 0.50, 0.50], forcedMiss: 3.0 },
        { name: "Toxbomb Launcher", dps: 3, range: 14, damage: 15, burst: 1, ap: 0.0, situational: true, acc: [0.50, 0.50, 0.50, 0.50], forcedMiss: 3.0 },
        { name: "Frag Grenades", dps: 8, range: 12, damage: 50, burst: 1, ap: 0.1, situational: true, acc: [0.50, 0.50, 0.50, 0.50], forcedMiss: 2.0 },
        { name: "Molotov Cocktails", dps: 5, range: 10, damage: 10, burst: 1, ap: 0.05, situational: true, acc: [0.50, 0.50, 0.50, 0.50], forcedMiss: 2.5 },
        { name: "EMP Grenades", dps: 6, range: 10, damage: 50, burst: 1, ap: 0.0, situational: true, acc: [0.50, 0.50, 0.50, 0.50], forcedMiss: 2.0 },

        // Ultra weapons (single-use)
        { name: "Triple Rocket Launcher", dps: 20, range: 24, damage: 50, burst: 3, ap: 0.2, situational: true, acc: [0.50, 0.50, 0.50, 0.50], forcedMiss: 3.5 },
        { name: "Doomsday Rocket Launcher", dps: 25, range: 20, damage: 80, burst: 1, ap: 0.3, situational: true, acc: [0.50, 0.50, 0.50, 0.50], forcedMiss: 4.0 },

        // DLC weapons
        { name: "Hellcat Rifle", dps: 9.57, range: 26.9, damage: 10, burst: 3, acc: [0.60, 0.70, 0.65, 0.55] }, // Anomaly DLC
        { name: "Nerve Spiker", dps: 3.55, range: 29.9, damage: 11, burst: 1, acc: [0.70, 0.78, 0.65, 0.35] }, // Anomaly DLC - stuns organic targets
        { name: "Flamebow", dps: 5, range: 16, damage: 12, burst: 1, acc: [0.50, 0.62, 0.41, 0.22] }, // Biotech DLC
        { name: "Incinerator", dps: 12, range: 12, damage: 12, burst: 1, acc: [0.75, 0.75, 0.50, 0.30], situational: true } // Fire weapon
    ];

    // MeleeWeapon_AverageDPS and MeleeWeapon_AverageArmorPenetration values at Normal quality
    const vanillaMeleeWeapons = [
        // Neolithic
        { name: "Club", dps: 6.31, ap: 0.1883, noQuality: true }, // No quality variations
        { name: "Ikwa", dps: 7.94, ap: 0.2108 },
        { name: "Knife", dps: 8.29, ap: 0.1706 },
        { name: "Gladius", dps: 8.33, ap: 0.2108 },
        { name: "Spear", dps: 11.43, ap: 0.415 },

        // Medieval
        { name: "Mace", dps: 9.76, ap: 0.2108 },
        { name: "Longsword", dps: 11.54, ap: 0.2858 },
        { name: "Warhammer", dps: 11.26, ap: 0.2583 }, // Royalty DLC
        { name: "Axe", dps: 6.68, ap: 0.1958 }, // Royalty DLC

        // Industrial
        { name: "Breach Axe", dps: 8.65, ap: 0.1136 },
        { name: "Plasteel Knife", dps: 12, ap: 0.18 },

        // Spacer
        { name: "Monosword", dps: 18.89, ap: 0.72 },
        { name: "Zeushammer", dps: 16.2, ap: 0.3833 },

        // Ultra
        { name: "Plasmasword", dps: 17.59, ap: 0.2975 },

        // DLC special weapons
        { name: "Jade Knife", dps: 6.78, ap: 0.1748 }, // Tribal scenario special
        { name: "Eltex Staff", dps: 6.9, ap: 0.165 }, // Royalty DLC psychic weapon

        // Natural weapons (can be equipped) - no quality variations
        { name: "Thrumbo Horn", dps: 9.44, ap: 0.3463, noQuality: true },
        { name: "Alpha Thrumbo Horn", dps: 20, ap: 0.48, noQuality: true }, // Alpha Animals mod
        { name: "Elephant Tusk", dps: 6.44, ap: 0.2333, noQuality: true },
        { name: "Mastodon Tusk", dps: 6.57, ap: 0.2575, noQuality: true } // Odyssey DLC
    ];

    // Modded weapons from popular mods - GREATLY EXPANDED
    // NOTE: Some modded weapon stats may be estimates or outdated. Verify critical values from:
    // - Vanilla Weapons Expanded Wiki: https://rimworld-ve.fandom.com/wiki/Vanilla_Weapons_Expanded
    // - Mod Steam pages or official documentation
    // - In-game using dev mode or weapon stat mods
    const moddedRangedWeapons = [
        // Vanilla Weapons Expanded Core (verified from official wiki)
        { name: "[VWE] Service Rifle", dps: 9.8, range: 31, damage: 10, burst: 4, mod: "VWE", acc: [0.60, 0.60, 0.55, 0.45] },
        { name: "[VWE] Battle Rifle", dps: 7.5, range: 34, damage: 12, burst: 2, mod: "VWE", acc: [0.55, 0.65, 0.75, 0.60] },
        { name: "[VWE] Compound Bow", dps: 6.4, range: 30, damage: 14, burst: 1, mod: "VWE", acc: [0.70, 0.78, 0.65, 0.35] },
        { name: "[VWE] Flintlock Rifle", dps: 6, range: 26, damage: 18, burst: 1, mod: "VWE", acc: [0.55, 0.70, 0.75, 0.60] },
        { name: "[VWE] Hand Cannon", dps: 8, range: 12, damage: 20, burst: 1, mod: "VWE", acc: [0.65, 0.60, 0.40, 0.25] },
        { name: "[VWE] Charged LMG", dps: 18, range: 23, damage: 12, burst: 6, ap: 0.30, mod: "VWE", acc: [0.40, 0.50, 0.60, 0.40] },
        { name: "[VWE] Ion Rifle", dps: 16, range: 27, damage: 14, burst: 3, ap: 0.40, mod: "VWE", acc: [0.70, 0.75, 0.75, 0.70] },
        { name: "[VWE] Crossbow", dps: 5.5, range: 20, damage: 15, burst: 1, mod: "VWE", acc: [0.65, 0.80, 0.70, 0.45] },
        { name: "[VWE] Musket", dps: 7, range: 24, damage: 22, burst: 1, mod: "VWE", acc: [0.60, 0.75, 0.80, 0.65] },
        { name: "[VWE] Marksman Rifle", dps: 9.5, range: 37, damage: 16, burst: 2, mod: "VWE", acc: [0.65, 0.70, 0.80, 0.70] },
        { name: "[VWE] Anti-Material Rifle", dps: 9.8, range: 43, damage: 56, burst: 1, ap: 0.84, mod: "VWE", acc: [0.30, 0.45, 0.75, 0.92] },
        { name: "[VWE] Grenade Launcher", dps: 10, range: 20, damage: 35, burst: 1, ap: 0.15, mod: "VWE", situational: true, acc: [0.50, 0.50, 0.50, 0.50], forcedMiss: 2.5 },
        { name: "[VWE] Charge Shotgun", dps: 18, range: 11, damage: 20, burst: 1, ap: 0.38, mod: "VWE", acc: [0.85, 0.85, 0.80, 0.70] },
        { name: "[VWE] Carbine", dps: 8.3, range: 23, damage: 11, burst: 3, mod: "VWE", acc: [0.60, 0.80, 0.75, 0.45] },
        { name: "[VWE] Throwing Rocks", dps: 3, range: 8, damage: 8, burst: 1, mod: "VWE", acc: [0.40, 0.30, 0.20, 0.10] },
        { name: "[VWE] Javelin", dps: 8, range: 14, damage: 18, burst: 1, mod: "VWE", acc: [0.60, 0.75, 0.45, 0.25] },

        // Vanilla Weapons Expanded - Quickdraw
        { name: "[VWEQ] Quickdraw Pistol", dps: 7, range: 16, damage: 12, burst: 1, mod: "VWE Quickdraw", acc: [0.82, 0.78, 0.48, 0.38] },
        { name: "[VWEQ] Quickdraw Rifle", dps: 12, range: 23, damage: 11, burst: 3, mod: "VWE Quickdraw", acc: [0.68, 0.68, 0.68, 0.58] },
        { name: "[VWEQ] Derringer", dps: 5, range: 8, damage: 14, burst: 1, mod: "VWE Quickdraw", acc: [0.85, 0.65, 0.35, 0.20] },
        { name: "[VWEQ] Bullpup Rifle", dps: 11.5, range: 22, damage: 11, burst: 3, mod: "VWE Quickdraw", acc: [0.70, 0.70, 0.65, 0.55] },

        // Vanilla Weapons Expanded - Frontier
        { name: "[VWEF] Lever-Action Rifle", dps: 9, range: 28, damage: 16, burst: 1, mod: "VWE Frontier", acc: [0.60, 0.75, 0.82, 0.72] },
        { name: "[VWEF] Double-Barrel Shotgun", dps: 16, range: 10, damage: 22, burst: 1, mod: "VWE Frontier", acc: [0.82, 0.85, 0.78, 0.65] },
        { name: "[VWEF] Peacemaker Revolver", dps: 7, range: 18, damage: 13, burst: 1, mod: "VWE Frontier", acc: [0.78, 0.72, 0.48, 0.35] },
        { name: "[VWEF] Sharps Rifle", dps: 10, range: 40, damage: 25, burst: 1, mod: "VWE Frontier", acc: [0.60, 0.78, 0.88, 0.90] },
        { name: "[VWEF] Gatling Gun", dps: 20, range: 22, damage: 8, burst: 10, mod: "VWE Frontier", acc: [0.25, 0.35, 0.45, 0.30] },
        { name: "[VWEF] Coilgun Revolver", dps: 10, range: 20, damage: 16, burst: 1, ap: 0.40, mod: "VWE Frontier", acc: [0.80, 0.78, 0.55, 0.45] },
        { name: "[VWEF] Coilgun Repeater", dps: 14, range: 32, damage: 18, burst: 2, ap: 0.45, mod: "VWE Frontier", acc: [0.70, 0.80, 0.82, 0.75] },

        // Vanilla Weapons Expanded - Heavy
        { name: "[VWEH] Autocannon", dps: 35, range: 25, damage: 25, burst: 5, ap: 0.50, mod: "VWE Heavy" },
        { name: "[VWEH] Heavy Flamethrower", dps: 28, range: 12, damage: 12, burst: 1, ap: 0.10, mod: "VWE Heavy", situational: true },
        { name: "[VWEH] Handheld Mortar", dps: 15, range: 30, damage: 50, burst: 1, ap: 0.20, mod: "VWE Heavy", situational: true },
        { name: "[VWEH] Homing Missile Launcher", dps: 30, range: 35, damage: 60, burst: 3, ap: 0.40, mod: "VWE Heavy", situational: true },
        { name: "[VWEH] Heavy Incinerator", dps: 32, range: 15, damage: 15, burst: 1, ap: 0.15, mod: "VWE Heavy", situational: true },
        { name: "[VWEH] Warbolter", dps: 40, range: 28, damage: 30, burst: 4, ap: 0.55, mod: "VWE Heavy" },

        // Vanilla Weapons Expanded - Bioferrite
        { name: "[VWEB] Hellcat Shotgun", dps: 19, range: 11, damage: 22, burst: 1, ap: 0.35, mod: "VWE Bioferrite" },
        { name: "[VWEB] Bioferrite Battle Rifle", dps: 13, range: 30, damage: 16, burst: 3, ap: 0.40, mod: "VWE Bioferrite" },
        { name: "[VWEB] Bioferrite Sniper", dps: 11, range: 42, damage: 28, burst: 1, ap: 0.45, mod: "VWE Bioferrite" },

        // Rimsenal - Core
        { name: "[RS] Molten Rifle", dps: 22.7, range: 29, damage: 25, burst: 1, ap: 0.40, mod: "Rimsenal" },
        { name: "[RS] Molten Pistol", dps: 17.4, range: 18, damage: 20, burst: 1, ap: 0.35, mod: "Rimsenal" },
        { name: "[RS] Shard Rifle", dps: 41.2, range: 21, damage: 5, burst: 5, ap: 0.15, mod: "Rimsenal" },
        { name: "[RS] Spike Rifle", dps: 18.4, range: 35, damage: 16, burst: 1, ap: 0.25, mod: "Rimsenal" },
        { name: "[RS] Kinetic Rifle", dps: 23.6, range: 25, damage: 13, burst: 3, ap: 0.30, mod: "Rimsenal" },
        { name: "[RS] Storm Cannon", dps: 29.7, range: 25, damage: 10, burst: 6, ap: 0.20, mod: "Rimsenal" },
        { name: "[RS] Siege Shotgun", dps: 32.3, range: 13, damage: 14, burst: 2, ap: 0.25, mod: "Rimsenal" },
        { name: "[RS] HV SMG", dps: 27.8, range: 16, damage: 8, burst: 5, ap: 0.18, mod: "Rimsenal" },
        { name: "[RS] Anti-Mech Rifle", dps: 18.3, range: 50, damage: 55, burst: 1, ap: 0.75, mod: "Rimsenal" },
        { name: "[RS] Suppressor Cannon", dps: 93.3, range: 19, damage: 7, burst: 25, ap: 0.15, mod: "Rimsenal" },
        { name: "[RS] Shard Pistol", dps: 24.8, range: 16, damage: 4, burst: 4, ap: 0.12, mod: "Rimsenal" },
        { name: "[RS] Dual Shard Pistols", dps: 35.2, range: 15, damage: 4, burst: 8, ap: 0.12, mod: "Rimsenal" },
        { name: "[RS] Shard Cannon", dps: 45.6, range: 18, damage: 6, burst: 8, ap: 0.18, mod: "Rimsenal" },
        { name: "[RS] Microwave Rifle", dps: 16.5, range: 26, damage: 22, burst: 1, ap: 0.60, mod: "Rimsenal" },
        { name: "[RS] Microwave Cannon", dps: 22.8, range: 24, damage: 28, burst: 1, ap: 0.70, mod: "Rimsenal" },
        { name: "[RS] Smoke Launcher", dps: 3, range: 15, damage: 10, burst: 1, ap: 0.0, mod: "Rimsenal", situational: true },
        { name: "[RS] Modular Rifle", dps: 13.5, range: 25, damage: 12, burst: 3, ap: 0.25, mod: "Rimsenal" },
        { name: "[RS] Fafnir Assault Gun", dps: 28.4, range: 23, damage: 18, burst: 3, ap: 0.35, mod: "Rimsenal" },
        { name: "[RS] Fafnir Breacher", dps: 34.5, range: 15, damage: 35, burst: 1, ap: 0.55, mod: "Rimsenal" },

        // Rimsenal - Augmented Vanilla
        { name: "[RSA] Hybrid Sword-Gun", dps: 9, range: 12, damage: 14, burst: 1, ap: 0.20, mod: "Rimsenal Augmented" },
        { name: "[RSA] Smart Rifle", dps: 14.5, range: 28, damage: 13, burst: 3, ap: 0.35, mod: "Rimsenal Augmented" },
        { name: "[RSA] Charge Carbine", dps: 13, range: 22, damage: 12, burst: 3, ap: 0.32, mod: "Rimsenal Augmented" },
        { name: "[RSA] Charge SMG", dps: 15, range: 18, damage: 10, burst: 4, ap: 0.28, mod: "Rimsenal Augmented" },
        { name: "[RSA] Charge Sniper", dps: 13.5, range: 40, damage: 32, burst: 1, ap: 0.48, mod: "Rimsenal Augmented" },

        // Vanilla Weapons Expanded - Laser
        { name: "[VWEL] Laser Rifle", dps: 14, range: 28, damage: 12, burst: 3, ap: 0.50, mod: "VWE Laser" },
        { name: "[VWEL] Laser Pistol", dps: 11, range: 20, damage: 10, burst: 2, ap: 0.45, mod: "VWE Laser" },

        // Combat Extended Guns
        { name: "[CE] SKS", dps: 11.1, range: 34, damage: 12.5, burst: 2, mod: "CE Guns" },
        { name: "[CE] AK-47", dps: 12.5, range: 29, damage: 12.5, burst: 3, mod: "CE Guns" },
        { name: "[CE] FN FAL", dps: 12.5, range: 32, damage: 12.5, burst: 3, mod: "CE Guns" },
        { name: "[CE] SVD Dragunov", dps: 10.5, range: 42, damage: 28, burst: 1, mod: "CE Guns" },
        { name: "[CE] Hecate II", dps: 15, range: 50, damage: 50, burst: 1, ap: 0.75, mod: "CE Guns" },
        { name: "[CE] RPD", dps: 14.5, range: 26, damage: 12.5, burst: 6, mod: "CE Guns" },
        { name: "[CE] PKM", dps: 15, range: 32, damage: 14, burst: 5, mod: "CE Guns" },
        { name: "[CE] M60", dps: 18, range: 30, damage: 13, burst: 7, mod: "CE Guns" },
        { name: "[CE] RPG-7", dps: 25, range: 25, damage: 80, burst: 1, ap: 0.65, mod: "CE Guns", situational: true },
        { name: "[CE] M72 LAW", dps: 20, range: 22, damage: 70, burst: 1, ap: 0.60, mod: "CE Guns", situational: true },
        { name: "[CE] Flamethrower", dps: 22, range: 10, damage: 10, burst: 1, ap: 0.08, mod: "CE Guns", situational: true },

        // Popular franchise weapon mods
        { name: "[40K] Bolter", dps: 22, range: 25, damage: 25, burst: 3, ap: 0.45, mod: "Warhammer 40k" },
        { name: "[40K] Heavy Bolter", dps: 35, range: 30, damage: 30, burst: 5, ap: 0.55, mod: "Warhammer 40k" },
        { name: "[40K] Plasma Gun", dps: 28, range: 28, damage: 35, burst: 1, ap: 0.70, mod: "Warhammer 40k" },
        { name: "[40K] Lascannon", dps: 30, range: 45, damage: 80, burst: 1, ap: 0.85, mod: "Warhammer 40k" },

        { name: "[SW] E-11 Blaster", dps: 14, range: 26, damage: 14, burst: 3, ap: 0.38, mod: "Star Wars" },
        { name: "[SW] DL-44 Pistol", dps: 10, range: 18, damage: 16, burst: 1, ap: 0.35, mod: "Star Wars" },
        { name: "[SW] A280 Rifle", dps: 16, range: 32, damage: 16, burst: 3, ap: 0.42, mod: "Star Wars" },

        { name: "[XCOM] Plasma Rifle", dps: 20, range: 30, damage: 22, burst: 3, ap: 0.55, mod: "XCOM" },
        { name: "[XCOM] Plasma Pistol", dps: 12, range: 20, damage: 18, burst: 1, ap: 0.45, mod: "XCOM" },
        { name: "[XCOM] Alloy Cannon", dps: 25, range: 12, damage: 28, burst: 1, ap: 0.50, mod: "XCOM" },

        // Misc popular weapon mods
        { name: "[TF] 40mm Tracker Cannon", dps: 38, range: 32, damage: 40, burst: 3, ap: 0.65, mod: "Titanfall" },
        { name: "[TF] Leadwall", dps: 42, range: 14, damage: 25, burst: 2, ap: 0.40, mod: "Titanfall" },
        { name: "[ME] M-8 Avenger", dps: 13, range: 28, damage: 11, burst: 4, mod: "Mass Effect" },
        { name: "[ME] M-98 Widow", dps: 18, range: 48, damage: 55, burst: 1, ap: 0.80, mod: "Mass Effect" }
    ];

    const moddedMeleeWeapons = [
        // Vanilla Weapons Expanded
        { name: "[VWE] Katana", dps: 14, ap: 0.22, mod: "VWE" },
        { name: "[VWE] Zweihander", dps: 15, ap: 0.25, mod: "VWE" },
        { name: "[VWE] Halberd", dps: 13.5, ap: 0.20, mod: "VWE" },
        { name: "[VWE] Combat Axe", dps: 12, ap: 0.18, mod: "VWE" },
        { name: "[VWE] War Axe", dps: 12.5, ap: 0.19, mod: "VWE" },
        { name: "[VWE] Broadsword", dps: 13, ap: 0.20, mod: "VWE" },
        { name: "[VWE] Flail", dps: 11.5, ap: 0.17, mod: "VWE" },
        { name: "[VWE] Morning Star", dps: 12, ap: 0.22, mod: "VWE" },
        { name: "[VWE] Rapier", dps: 11, ap: 0.16, mod: "VWE" },
        { name: "[VWE] Executioner's Axe", dps: 14.5, ap: 0.24, mod: "VWE" },

        // Weapons+ Mod
        { name: "[W+] Primitive Knife", dps: 6.5, ap: 0.10, mod: "Weapons+" },
        { name: "[W+] Kukri", dps: 9.5, ap: 0.14, mod: "Weapons+" },
        { name: "[W+] Kunai", dps: 7, ap: 0.11, mod: "Weapons+" },
        { name: "[W+] Katana", dps: 13.5, ap: 0.20, mod: "Weapons+" },
        { name: "[W+] Claymore", dps: 14, ap: 0.23, mod: "Weapons+" },
        { name: "[W+] Zweihander", dps: 15, ap: 0.25, mod: "Weapons+" },
        { name: "[W+] Sabre", dps: 11.5, ap: 0.17, mod: "Weapons+" },
        { name: "[W+] Morning Star", dps: 12.5, ap: 0.22, mod: "Weapons+" },
        { name: "[W+] Maul", dps: 13, ap: 0.24, mod: "Weapons+" },
        { name: "[W+] Chain Flail", dps: 11, ap: 0.18, mod: "Weapons+" },
        { name: "[W+] Nunchaku", dps: 10, ap: 0.15, mod: "Weapons+" },
        { name: "[W+] Bardiche", dps: 13, ap: 0.21, mod: "Weapons+" },
        { name: "[W+] Glaive", dps: 12.5, ap: 0.19, mod: "Weapons+" },
        { name: "[W+] Naginata", dps: 12, ap: 0.18, mod: "Weapons+" },
        { name: "[W+] Bo Staff", dps: 9, ap: 0.13, mod: "Weapons+" },
        { name: "[W+] Scythe", dps: 11.5, ap: 0.17, mod: "Weapons+" },
        { name: "[W+] Great Axe", dps: 14.5, ap: 0.24, mod: "Weapons+" },
        { name: "[W+] Brass Knuckles", dps: 6, ap: 0.09, mod: "Weapons+" },
        { name: "[W+] Tonfa", dps: 8.5, ap: 0.13, mod: "Weapons+" },

        // Rimsenal
        { name: "[RS] Vibrosword", dps: 11.8, ap: 0.28, mod: "Rimsenal" },
        { name: "[RS] Combat Blade", dps: 10.6, ap: 0.24, mod: "Rimsenal" },
        { name: "[RS] Assault Hammer", dps: 11.0, ap: 0.32, mod: "Rimsenal" },
        { name: "[RS] Survival Knife", dps: 8.2, ap: 0.14, mod: "Rimsenal" },
        { name: "[RS] Power Blade", dps: 16.8, ap: 0.42, mod: "Rimsenal" },
        { name: "[RS] Shock Maul", dps: 14.2, ap: 0.38, mod: "Rimsenal" },
        { name: "[RS] Plasma Edge", dps: 18.5, ap: 0.52, mod: "Rimsenal" },

        // Vanilla Weapons Expanded - Laser
        { name: "[VWEL] Laser Sword", dps: 19, ap: 0.60, mod: "VWE Laser" },

        // Popular franchise weapons
        { name: "[40K] Chainsword", dps: 16, ap: 0.35, mod: "Warhammer 40k" },
        { name: "[40K] Power Sword", dps: 18, ap: 0.48, mod: "Warhammer 40k" },
        { name: "[40K] Thunder Hammer", dps: 20, ap: 0.55, mod: "Warhammer 40k" },

        { name: "[SW] Lightsaber", dps: 21, ap: 0.65, mod: "Star Wars" },
        { name: "[SW] Vibroblade", dps: 14, ap: 0.32, mod: "Star Wars" },
        { name: "[SW] Electrostaff", dps: 12, ap: 0.28, mod: "Star Wars" },

        { name: "[BB] Saw Cleaver", dps: 13.5, ap: 0.22, mod: "Bloodborne" },
        { name: "[BB] Ludwig's Holy Blade", dps: 15.5, ap: 0.28, mod: "Bloodborne" },
        { name: "[BB] Burial Blade", dps: 14, ap: 0.25, mod: "Bloodborne" },

        { name: "[SK] Dragonbone Sword", dps: 14.5, ap: 0.26, mod: "Skyrim" },
        { name: "[SK] Daedric Mace", dps: 15, ap: 0.30, mod: "Skyrim" },
        { name: "[SK] Ebony Battleaxe", dps: 16, ap: 0.32, mod: "Skyrim" },

        // Medieval Times mod weapons
        { name: "[MT] Bastard Sword", dps: 12.8, ap: 0.19, mod: "Medieval Times" },
        { name: "[MT] Falchion", dps: 11.5, ap: 0.17, mod: "Medieval Times" },
        { name: "[MT] Viking Axe", dps: 13.2, ap: 0.21, mod: "Medieval Times" },
        { name: "[MT] Pike", dps: 10.5, ap: 0.16, mod: "Medieval Times" }
    ];

    // Export the weapon data for use in the main HTML file
    // This makes the data available globally when loaded
    window.weaponData = {
        vanillaRangedWeapons: vanillaRangedWeapons,
        vanillaMeleeWeapons: vanillaMeleeWeapons,
        moddedRangedWeapons: moddedRangedWeapons,
        moddedMeleeWeapons: moddedMeleeWeapons
    };

    // Log to console for debugging
    console.log('weapons-data.js loaded successfully!');
    console.log('Weapon counts:', {
        vanillaRanged: vanillaRangedWeapons.length,
        vanillaMelee: vanillaMeleeWeapons.length,
        moddedRanged: moddedRangedWeapons.length,
        moddedMelee: moddedMeleeWeapons.length
    });

    /* 
     * SPECIAL WEAPONS NOTE:
     * 
     * Many weapons in RimWorld have special effects beyond raw damage that make them situationally powerful:
     * 
     * - EMP weapons (EMP Launcher, EMP Grenades): Stun mechanoids and break shields - invaluable against mech clusters
     * - Fire weapons (Incendiary Launcher, Molotov Cocktails, Flamebow, Incinerator): Create area denial and heat
     * - Toxic weapons (Toxbomb Launcher, Tox Grenades): Create lasting toxic clouds - great for chokepoints
     * - Smoke weapons (Smoke Launcher): Block turret targeting and reduce shooting accuracy
     * - Explosive weapons (Frag Grenades, Rocket Launchers): Area damage and structure destruction
     * 
     * In AutoArm, these weapons receive special scoring bonuses based on the tactical situation:
     * - vs Mechanoids: EMP weapons get major score bonuses
     * - vs Infestations: Fire weapons are heavily favored 
     * - Defensive positions: Smoke/toxic weapons score higher
     * - vs Structures: Explosive weapons receive bonuses
     * 
     * The 'situational' flag on these weapons indicates they should be evaluated differently than
     * standard DPS-focused weapons. Their true value comes from utility, not raw damage output.
     */
})();