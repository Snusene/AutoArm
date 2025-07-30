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
    const vanillaRangedWeapons = [
        // Early-game/Tribal
        { name: "Short Bow", dps: 4.5, range: 16, damage: 10, burst: 1 },
        { name: "Recurve Bow", dps: 5.5, range: 20, damage: 13, burst: 1 },
        { name: "Greatbow", dps: 6.5, range: 22, damage: 17, burst: 1 },
        { name: "Pila", dps: 7, range: 12, damage: 16, burst: 1 },

        // Industrial weapons
        { name: "Revolver", dps: 6.5, range: 16, damage: 12, burst: 1 },
        { name: "Autopistol", dps: 9, range: 16, damage: 10, burst: 1 },
        { name: "Pump Shotgun", dps: 14, range: 9, damage: 18, burst: 1 },
        { name: "Machine Pistol", dps: 10.5, range: 15, damage: 7, burst: 3 },
        { name: "Bolt-Action Rifle", dps: 8, range: 30, damage: 18, burst: 1 },
        { name: "Assault Rifle", dps: 11.5, range: 23, damage: 11, burst: 3 },
        { name: "Sniper Rifle", dps: 9.5, range: 37, damage: 25, burst: 1 },
        { name: "Chain Shotgun", dps: 16, range: 9, damage: 13, burst: 1 },
        { name: "Heavy SMG", dps: 13, range: 16, damage: 12, burst: 5 },
        { name: "LMG", dps: 12, range: 19, damage: 11, burst: 6 },

        // Spacer weapons
        { name: "Charge Rifle", dps: 15.5, range: 25, damage: 15, ap: 0.35, burst: 3 },
        { name: "Charge Lance", dps: 12, range: 30, damage: 30, ap: 0.45, burst: 1 },
        { name: "Minigun", dps: 25, range: 20, damage: 5, burst: 25 },

        // Special weapons (DPS values are estimates - these weapons work differently)
        { name: "Incendiary Launcher", dps: 4.5, range: 15, damage: 10, burst: 1, ap: 0.05, situational: true },
        { name: "EMP Launcher", dps: 3.5, range: 14, damage: 50, burst: 1, ap: 0.0, situational: true },
        { name: "Smoke Launcher", dps: 2, range: 14, damage: 10, burst: 1, ap: 0.0, situational: true },
        { name: "Frag Grenades", dps: 8, range: 12, damage: 50, burst: 1, ap: 0.1, situational: true },
        { name: "Molotov Cocktails", dps: 5, range: 10, damage: 10, burst: 1, ap: 0.05, situational: true },
        { name: "EMP Grenades", dps: 6, range: 10, damage: 50, burst: 1, ap: 0.0, situational: true },

        // Ultra weapons (single-use)
        { name: "Triple Rocket Launcher", dps: 20, range: 24, damage: 50, burst: 3, ap: 0.2, situational: true },
        { name: "Doomsday Rocket Launcher", dps: 25, range: 20, damage: 80, burst: 1, ap: 0.3, situational: true }
    ];

    // MeleeWeapon_AverageDPS and MeleeWeapon_AverageArmorPenetration values at Normal quality
    const vanillaMeleeWeapons = [
        // Improvised
        { name: "Fists", dps: 4.5, ap: 0.07 },
        { name: "Beer", dps: 5.5, ap: 0.08 },

        // Neolithic
        { name: "Club", dps: 7.5, ap: 0.112 },
        { name: "Ikwa", dps: 9, ap: 0.135 },
        { name: "Knife", dps: 8.5, ap: 0.128 },
        { name: "Gladius", dps: 10.5, ap: 0.158 },
        { name: "Spear", dps: 9.5, ap: 0.143 },

        // Medieval
        { name: "Mace", dps: 11, ap: 0.165 },
        { name: "Longsword", dps: 12.5, ap: 0.188 },
        { name: "Warhammer", dps: 13, ap: 0.195 },

        // Industrial
        { name: "Breach Axe", dps: 11.5, ap: 0.173 },
        { name: "Plasteel Knife", dps: 12, ap: 0.18 },

        // Spacer
        { name: "Monosword", dps: 20, ap: 0.5 },
        { name: "Zeushammer", dps: 18, ap: 0.35 },

        // Ultra
        { name: "Plasmasword", dps: 22, ap: 0.55 }
    ];

    // Modded weapons from popular mods - GREATLY EXPANDED
    const moddedRangedWeapons = [
        // Vanilla Weapons Expanded Core
        { name: "[VWE] Service Rifle", dps: 12, range: 26, damage: 12, burst: 3, mod: "VWE" },
        { name: "[VWE] Battle Rifle", dps: 10.5, range: 32, damage: 16, burst: 2, mod: "VWE" },
        { name: "[VWE] Compound Bow", dps: 7, range: 22, damage: 18, burst: 1, mod: "VWE" },
        { name: "[VWE] Flintlock Rifle", dps: 6, range: 26, damage: 18, burst: 1, mod: "VWE" },
        { name: "[VWE] Hand Cannon", dps: 8, range: 12, damage: 20, burst: 1, mod: "VWE" },
        { name: "[VWE] Charged LMG", dps: 18, range: 23, damage: 12, burst: 6, ap: 0.30, mod: "VWE" },
        { name: "[VWE] Ion Rifle", dps: 16, range: 27, damage: 14, burst: 3, ap: 0.40, mod: "VWE" },
        { name: "[VWE] Crossbow", dps: 5.5, range: 20, damage: 15, burst: 1, mod: "VWE" },
        { name: "[VWE] Musket", dps: 7, range: 24, damage: 22, burst: 1, mod: "VWE" },
        { name: "[VWE] Designated Marksman Rifle", dps: 11, range: 35, damage: 20, burst: 1, mod: "VWE" },
        { name: "[VWE] Anti-Material Rifle", dps: 14, range: 45, damage: 40, burst: 1, ap: 0.60, mod: "VWE" },
        { name: "[VWE] Grenade Launcher", dps: 10, range: 20, damage: 35, burst: 1, ap: 0.15, mod: "VWE" },
        { name: "[VWE] Charge Shotgun", dps: 18, range: 11, damage: 20, burst: 1, ap: 0.38, mod: "VWE" },
        { name: "[VWE] Carbine", dps: 10, range: 20, damage: 10, burst: 3, mod: "VWE" },
        { name: "[VWE] Throwing Rocks", dps: 3, range: 8, damage: 8, burst: 1, mod: "VWE" },
        { name: "[VWE] Javelin", dps: 8, range: 14, damage: 18, burst: 1, mod: "VWE" },

        // Vanilla Weapons Expanded - Quickdraw
        { name: "[VWEQ] Quickdraw Pistol", dps: 7, range: 16, damage: 12, burst: 1, mod: "VWE Quickdraw" },
        { name: "[VWEQ] Quickdraw Rifle", dps: 12, range: 23, damage: 11, burst: 3, mod: "VWE Quickdraw" },
        { name: "[VWEQ] Derringer", dps: 5, range: 8, damage: 14, burst: 1, mod: "VWE Quickdraw" },
        { name: "[VWEQ] Bullpup Rifle", dps: 11.5, range: 22, damage: 11, burst: 3, mod: "VWE Quickdraw" },

        // Vanilla Weapons Expanded - Frontier
        { name: "[VWEF] Lever-Action Rifle", dps: 9, range: 28, damage: 16, burst: 1, mod: "VWE Frontier" },
        { name: "[VWEF] Double-Barrel Shotgun", dps: 16, range: 10, damage: 22, burst: 1, mod: "VWE Frontier" },
        { name: "[VWEF] Peacemaker Revolver", dps: 7, range: 18, damage: 13, burst: 1, mod: "VWE Frontier" },
        { name: "[VWEF] Sharps Rifle", dps: 10, range: 40, damage: 25, burst: 1, mod: "VWE Frontier" },
        { name: "[VWEF] Gatling Gun", dps: 20, range: 22, damage: 8, burst: 10, mod: "VWE Frontier" },
        { name: "[VWEF] Coilgun Revolver", dps: 10, range: 20, damage: 16, burst: 1, ap: 0.40, mod: "VWE Frontier" },
        { name: "[VWEF] Coilgun Repeater", dps: 14, range: 32, damage: 18, burst: 2, ap: 0.45, mod: "VWE Frontier" },

        // Vanilla Weapons Expanded - Heavy
        { name: "[VWEH] Autocannon", dps: 35, range: 25, damage: 25, burst: 5, ap: 0.50, mod: "VWE Heavy" },
        { name: "[VWEH] Heavy Flamethrower", dps: 28, range: 12, damage: 12, burst: 1, ap: 0.10, mod: "VWE Heavy" },
        { name: "[VWEH] Handheld Mortar", dps: 15, range: 30, damage: 50, burst: 1, ap: 0.20, mod: "VWE Heavy" },
        { name: "[VWEH] Homing Missile Launcher", dps: 30, range: 35, damage: 60, burst: 3, ap: 0.40, mod: "VWE Heavy" },
        { name: "[VWEH] Heavy Incinerator", dps: 32, range: 15, damage: 15, burst: 1, ap: 0.15, mod: "VWE Heavy" },
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
        { name: "[RS] Smoke Launcher", dps: 3, range: 15, damage: 10, burst: 1, ap: 0.0, mod: "Rimsenal" },
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
        { name: "[CE] SKS", dps: 10, range: 32, damage: 18, burst: 1, mod: "CE Guns" },
        { name: "[CE] AK-47", dps: 12.5, range: 28, damage: 11, burst: 3, mod: "CE Guns" },
        { name: "[CE] FN FAL", dps: 11, range: 35, damage: 20, burst: 1, mod: "CE Guns" },
        { name: "[CE] SVD Dragunov", dps: 10.5, range: 42, damage: 28, burst: 1, mod: "CE Guns" },
        { name: "[CE] Hecate II", dps: 15, range: 50, damage: 50, burst: 1, ap: 0.75, mod: "CE Guns" },
        { name: "[CE] RPD", dps: 14, range: 25, damage: 11, burst: 5, mod: "CE Guns" },
        { name: "[CE] PKM", dps: 16, range: 28, damage: 12, burst: 6, mod: "CE Guns" },
        { name: "[CE] M60", dps: 18, range: 30, damage: 13, burst: 7, mod: "CE Guns" },
        { name: "[CE] RPG-7", dps: 25, range: 25, damage: 80, burst: 1, ap: 0.65, mod: "CE Guns" },
        { name: "[CE] M72 LAW", dps: 20, range: 22, damage: 70, burst: 1, ap: 0.60, mod: "CE Guns" },
        { name: "[CE] Flamethrower", dps: 22, range: 10, damage: 10, burst: 1, ap: 0.08, mod: "CE Guns" },

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
})();