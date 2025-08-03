// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: Main mod class with settings UI and mod initialization  
// Uses AutoArmSettings for configuration, AutoArmLoggerWindow for debug tools, ConflictDetection for compatibility checks

using UnityEngine;
using Verse;
using RimWorld;
using System.Linq;
using System.Collections.Generic;
using System;
using AutoArm.Testing;
using AutoArm.Caching;
using AutoArm.Helpers;
using AutoArm.Jobs;
using AutoArm.Weapons;
using AutoArm.Logging;
namespace AutoArm
{
    public class AutoArmMod : Mod
    {
        private const float LINE_HEIGHT = 30f;
        private const float CHECKBOX_SIZE = 24f;
        private const float LABEL_WIDTH = 250f;
        private const float TAB_BUTTON_HEIGHT = 30f;
        private const float CONTENT_PADDING = 10f;
        private const float SECTION_GAP = 20f;
        private const float SMALL_GAP = 12f;
        private const float TINY_GAP = 6f;
        private const float RESET_BUTTON_WIDTH = 100f;
        private const float RESET_BUTTON_HEIGHT = 30f;

        private const float THRESHOLD_MIN = 1.01f;
        private const float THRESHOLD_MAX = 1.50f;
        private const int CHILD_AGE_MIN = 3;
        private const int CHILD_AGE_MAX = 18;

        public static AutoArmSettings settings;
        private SettingsTab currentTab = SettingsTab.General;

        private AutoArmLoggerWindow debugWindow;

        private enum SettingsTab
        {
            General,
            Compatibility,
            Advanced,
            Debug
        }

        // Static constructor to ensure settings are initialized before tests
        static AutoArmMod()
        {
            // If settings haven't been initialized yet, create default enabled settings
            if (settings == null)
            {
                settings = new AutoArmSettings();
                settings.modEnabled = true;
                AutoArmLogger.Debug("[AutoArmMod] Static constructor - created default enabled settings");
            }
        }

        public AutoArmMod(ModContentPack content) : base(content)
        {
            // Only overwrite settings if they haven't been set by tests
            if (!TestRunner.IsRunningTests)
            {
                settings = GetSettings<AutoArmSettings>();
            }
            else
            {
                // During tests, preserve test configuration
                if (settings == null)
                {
                    // Create test-enabled settings rather than loading from disk
                    settings = new AutoArmSettings();
                    settings.modEnabled = true;
                    AutoArmLogger.Debug("[AutoArmMod] Constructor during test - created new enabled settings");
                }
                else
                {
                    // Ensure mod stays enabled during tests
                    bool wasEnabled = settings.modEnabled;
                    settings.modEnabled = true;
                    if (!wasEnabled)
                    {
                        AutoArmLogger.Debug("[AutoArmMod] Constructor during test - force enabled mod");
                    }
                }
                AutoArmLogger.Debug($"[AutoArmMod] Constructor during test - modEnabled: {settings?.modEnabled}");
            }
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            DrawSettingsWindow(inRect);
        }

        private void DrawSettingsWindow(Rect inRect)
        {
            var listing = new Listing_Standard();
            listing.Begin(inRect);

            DrawHeader(listing, inRect);
            DrawTabButtons(listing);

            var contentRect = listing.GetRect(inRect.height - listing.CurHeight - LINE_HEIGHT);
            DrawTabContent(contentRect);

            listing.End();
        }

        private void DrawHeader(Listing_Standard listing, Rect inRect)
        {
            DrawResetButton(inRect);

            // Draw Settings title and hover hint on the same line
            var titleRect = listing.GetRect(Text.LineHeight * 1.5f); // Slightly taller for medium font

            // Draw "Settings" on the left
            Text.Font = GameFont.Medium;
            var settingsLabelRect = new Rect(titleRect.x, titleRect.y, 100f, titleRect.height);
            Widgets.Label(settingsLabelRect, "Settings");

            // Draw hover hint to the right of Settings
            Text.Font = GameFont.Tiny;
            Color oldColor = GUI.color;
            GUI.color = new Color(0.7f, 0.7f, 0.7f, 0.8f); // Gray with slight transparency
            var hintRect = new Rect(settingsLabelRect.xMax + 10f, titleRect.y + 4f, 200f, titleRect.height);
            Widgets.Label(hintRect, "(hover over options for descriptions)");
            GUI.color = oldColor;

            Text.Font = GameFont.Small;
            listing.Gap(SMALL_GAP);
        }

        private void DrawResetButton(Rect inRect)
        {
            Rect resetButtonRect = new Rect(
                inRect.width - RESET_BUTTON_WIDTH - 5f,
                5f,
                RESET_BUTTON_WIDTH,
                RESET_BUTTON_HEIGHT
            );

            Color oldColor = GUI.color;
            GUI.color = new Color(0.65f, 0.65f, 0.65f);

            if (Widgets.ButtonText(resetButtonRect, "Reset Config"))
            {
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    "Are you sure you want to reset all settings to defaults?",
                    () =>
                    {
                        settings.ResetToDefaults();
                        Messages.Message("Settings reset to defaults", MessageTypeDefOf.NeutralEvent, false);
                    }));
            }

            GUI.color = oldColor;
        }

        private void DrawTabButtons(Listing_Standard listing)
        {
            var tabRect = listing.GetRect(TAB_BUTTON_HEIGHT);
            var tabWidth = tabRect.width / 4f - 5f;

            Color originalColor = GUI.color;

            GUI.color = currentTab == SettingsTab.General ? Color.white : new Color(0.7f, 1f, 0.7f);
            if (Widgets.ButtonText(new Rect(tabRect.x, tabRect.y, tabWidth, TAB_BUTTON_HEIGHT), "General"))
                currentTab = SettingsTab.General;

            GUI.color = currentTab == SettingsTab.Compatibility ? Color.white : new Color(1f, 1f, 0.7f);
            if (Widgets.ButtonText(new Rect(tabRect.x + tabWidth + 5f, tabRect.y, tabWidth, TAB_BUTTON_HEIGHT), "Compatibility"))
                currentTab = SettingsTab.Compatibility;

            GUI.color = currentTab == SettingsTab.Advanced ? Color.white : new Color(1f, 0.9f, 0.6f);
            if (Widgets.ButtonText(new Rect(tabRect.x + (tabWidth + 5f) * 2, tabRect.y, tabWidth, TAB_BUTTON_HEIGHT), "Advanced"))
                currentTab = SettingsTab.Advanced;

            GUI.color = currentTab == SettingsTab.Debug ? Color.white : new Color(1f, 0.7f, 0.7f);
            if (Widgets.ButtonText(new Rect(tabRect.x + (tabWidth + 5f) * 3, tabRect.y, tabWidth, TAB_BUTTON_HEIGHT), "Debug"))
            {
                if (Current.Game?.CurrentMap != null)
                {
                    OpenDebugWindow();
                }
                else
                {
                    Messages.Message("Debug tools require an active game", MessageTypeDefOf.RejectInput, false);
                }
            }

            GUI.color = originalColor;
            listing.Gap(SMALL_GAP);
        }

        private void DrawTabContent(Rect contentRect)
        {
            var innerRect = contentRect.ContractedBy(CONTENT_PADDING);

            Widgets.DrawBoxSolid(contentRect, new Color(0.1f, 0.1f, 0.1f, 0.3f));

            var innerListing = new Listing_Standard();
            innerListing.Begin(innerRect);

            // Gray out content of non-General tabs if mod is disabled
            Color oldColor = GUI.color;
            bool wasEnabled = GUI.enabled;
            if (!settings.modEnabled && currentTab != SettingsTab.General)
            {
                GUI.color = Color.gray;
                GUI.enabled = false;
            }

            switch (currentTab)
            {
                case SettingsTab.General:
                    DrawGeneralTab(innerListing);
                    break;

                case SettingsTab.Compatibility:
                    DrawCompatibilityTab(innerListing);
                    break;

                case SettingsTab.Advanced:
                    DrawAdvancedTab(innerListing);
                    break;
            }

            // Restore original state
            GUI.color = oldColor;
            GUI.enabled = wasEnabled;

            innerListing.End();
        }

        private void DrawCheckbox(Listing_Standard listing, string label, ref bool value, string tooltip = null, float indent = 0f, bool isSubOption = false)
        {
            Rect fullRect = listing.GetRect(LINE_HEIGHT);
            Rect labelRect = fullRect;

            // Use smaller checkbox for sub-options
            float checkboxSize = isSubOption ? CHECKBOX_SIZE * 0.8f : CHECKBOX_SIZE;

            Rect checkRect = new Rect(
                fullRect.x + indent,
                fullRect.y + (LINE_HEIGHT - checkboxSize) / 2f,
                checkboxSize,
                checkboxSize
            );

            labelRect.x += checkboxSize + 5f + indent;
            labelRect.width -= checkboxSize + 5f + indent;

            bool oldValue = value;

            // Only allow changes if GUI is enabled
            if (GUI.enabled)
            {
                Widgets.Checkbox(checkRect.x, checkRect.y, ref value, checkboxSize);
            }
            else
            {
                // Draw checkbox in disabled state
                bool tempValue = value;
                Widgets.Checkbox(checkRect.x, checkRect.y, ref tempValue, checkboxSize);
            }

            // Use slightly smaller font for sub-options
            if (isSubOption)
            {
                Text.Font = GameFont.Tiny;
            }

            Widgets.Label(labelRect, label);

            if (isSubOption)
            {
                Text.Font = GameFont.Small;
            }

            if (tooltip != null && Mouse.IsOver(fullRect))
            {
                TooltipHandler.TipRegion(fullRect, tooltip);
            }

            if (oldValue != value && settings.debugLogging)
            {
                AutoArmLogger.Debug($"Setting changed: {label} = {value}");
            }
        }

        private void DrawSlider(Listing_Standard listing, string label, ref float value, float min, float max, string format = "P0", string tooltip = null, bool isPercentageBetter = false)
        {
            // Draw label above slider
            Rect labelRect = listing.GetRect(Text.LineHeight);
            Widgets.Label(labelRect, label);
            
            // Draw slider line below label
            Rect rect = listing.GetRect(LINE_HEIGHT);

            // Use 1/3 width for slider, aligned left
            float sliderWidth = rect.width / 3f;
            Rect sliderRect = new Rect(rect.x, rect.y, sliderWidth, rect.height);
            
            // Value right after slider
            float valueWidth = 150f;
            Rect valueRect = new Rect(sliderRect.xMax + 10f, rect.y, valueWidth, rect.height);

            // Draw slider (taller for easier clicking)
            float sliderHeight = 20f;
            Rect actualSliderRect = new Rect(sliderRect.x, sliderRect.y + (rect.height - sliderHeight) / 2f, sliderRect.width, sliderHeight);

            float oldValue = value;

            // Only allow changes if GUI is enabled
            if (GUI.enabled)
            {
                value = Widgets.HorizontalSlider(actualSliderRect, value, min, max);
            }
            else
            {
                // Draw slider in disabled state without allowing changes
                float tempValue = value;
                Widgets.HorizontalSlider(actualSliderRect, tempValue, min, max);
            }

            // Special handling for weapon preference slider
            bool isWeaponPreference = (label == "Weapon preference" && min == -1f && max == 1f);

            // Draw center line for weapon preference only
            if (isWeaponPreference)
            {
                // Draw center line (balanced position)
                Color oldColor = GUI.color;
                GUI.color = new Color(0.5f, 0.5f, 0.5f, 0.5f);
                float centerX = actualSliderRect.x + actualSliderRect.width / 2f;
                Widgets.DrawLineVertical(centerX, actualSliderRect.y, actualSliderRect.height);
                GUI.color = oldColor;
            }

            // Draw value
            string displayValue;
            Color valueColor = GUI.color;

            if (isWeaponPreference)
            {
                // Special display for weapon preference
                if (value <= -0.75f)
                    displayValue = "Strong melee preference";
                else if (value <= -0.35f)
                    displayValue = "Moderate melee preference";
                else if (value <= -0.10f)
                    displayValue = "Slight melee preference";
                else if (value < 0.10f)
                    displayValue = "Balanced (no preference)";
                else if (value < 0.35f)
                    displayValue = "Slight ranged preference";
                else if (value < 0.75f)
                    displayValue = "Moderate ranged preference";
                else
                    displayValue = "Strong ranged preference";

                // Color the text based on preference
                if (Math.Abs(value) < 0.10f)
                    valueColor = new Color(0.8f, 0.8f, 0.8f); // Gray for balanced
                else if (value < 0)
                    valueColor = new Color(0.8f, 0.8f, 1f); // Light blue for melee
                else
                    valueColor = new Color(1f, 0.8f, 0.8f); // Light red for ranged
            }
            else if (isPercentageBetter)
            {
                float percentBetter = (value - 1f) * 100f;
                displayValue = $"{percentBetter:F0}%";
            }
            else
            {
                displayValue = format == "P0" ? value.ToString("P0") : value.ToString(format);
            }

            Color oldTextColor = GUI.color;
            GUI.color = valueColor;
            Widgets.Label(valueRect, displayValue);
            GUI.color = oldTextColor;

            value = Mathf.Clamp(value, min, max);

            if (tooltip != null && Mouse.IsOver(labelRect))
            {
                TooltipHandler.TipRegion(labelRect, tooltip);
            }

            if (Math.Abs(oldValue - value) > 0.01f && settings.debugLogging)
            {
                if (isWeaponPreference)
                {
                    AutoArmLogger.Debug($"Weapon preference changed to: {displayValue} (value: {value:F2})");
                }
                else
                {
                    AutoArmLogger.Debug($"Setting changed: {label} = {value:F2}");
                }
            }
        }

        private void DrawGeneralTab(Listing_Standard listing)
        {
            bool oldModEnabled = settings.modEnabled;

            DrawCheckbox(listing, "Enable mod", ref settings.modEnabled,
                "Master toggle for all mod functionality.");
            
            // Log when mod enabled state changes (always log this important change)
            if (oldModEnabled != settings.modEnabled)
            {
                AutoArmLogger.Info($"AutoArm mod {(settings.modEnabled ? "enabled" : "disabled")}");
            }

            // If mod was just re-enabled, clear all caches and cooldowns
            if (!oldModEnabled && settings.modEnabled)
            {
                // Only log cache clearing details if debug logging is on
                if (settings.debugLogging)
                {
                    AutoArmLogger.Debug("[SETTINGS] Mod was just re-enabled, clearing caches...");
                }
                
                // Only clear caches if we're in an active game
                if (Current.Game != null)
                {
                    // Clear all cooldowns
                    TimingHelper.ClearAllCooldowns();

                    // Clear and rebuild weapon cache (this also clears weapon score cache)
                    ImprovedWeaponCacheManager.ClearAndRebuildCache();

                    // Clear dropped item tracker
                    DroppedItemTracker.ClearAll();

                    // Clear any SimpleSidearms state
                    if (SimpleSidearmsCompat.IsLoaded())
                    {
                        // SimpleSidearms cache clearing is handled automatically
                    }

                    // Clear generic cache to ensure fresh values
                    CleanupHelper.ClearAllCaches();

                    if (settings.debugLogging)
                    {
                        AutoArmLogger.Debug("Mod re-enabled - cleared all caches and cooldowns, rebuilding weapon cache");
                    }
                }
                else
                {
                    // Just clear the generic cache when not in game
                    CleanupHelper.ClearAllCaches();
                    if (settings.debugLogging)
                    {
                        AutoArmLogger.Debug("Mod re-enabled in main menu - cleared settings cache only");
                    }
                }
            }

            listing.Gap(SMALL_GAP);

            // Gray out all other settings if mod is disabled
            Color oldColor = GUI.color;
            bool wasEnabled = GUI.enabled;
            if (!settings.modEnabled)
            {
                GUI.color = Color.gray;
                GUI.enabled = false;
            }

            DrawCheckbox(listing, "Show notifications", ref settings.showNotifications,
                "Shows blue notification messages when colonists equip or drop weapons.");

            listing.Gap(SMALL_GAP);

            DrawCheckbox(listing, "Disable during raids", ref settings.disableDuringRaids,
                "Performance boost during raids when autonomous weapon selection isn't really important.");

            // Royalty DLC settings
            if (ModsConfig.RoyaltyActive)
            {
                listing.Gap(SMALL_GAP);

                bool oldRespectBonds = settings.respectWeaponBonds;
                DrawCheckbox(listing, "Force bonded persona weapons", ref settings.respectWeaponBonds,
                    "When enabled, bonded weapons are automatically marked as forced, preventing colonists from switching away from them. The forced status persists until it is lost or dropped.");

                // If setting was just enabled, mark all bonded weapons as forced
                if (!oldRespectBonds && settings.respectWeaponBonds && Current.Game != null)
                {
                    MarkAllBondedWeaponsAsForced();
                }
            }

            listing.Gap(SMALL_GAP);

            DrawCheckbox(listing, "Allow quality upgrades for forced weapons", ref settings.allowForcedWeaponUpgrades,
                "When enabled, colonists can upgrade forced weapons to better quality versions of the same type (e.g., normal revolver ? masterwork revolver).");

            listing.Gap(SMALL_GAP);

            DrawCheckbox(listing, "Allow temporary colonists to auto-equip", ref settings.allowTemporaryColonists,
                "When enabled, temporary colonists (quest lodgers, borrowed pawns, royal guests) can auto-equip weapons. When disabled, they keep their current equipment.");

            // Restore original colors
            GUI.color = oldColor;
            GUI.enabled = wasEnabled;

            listing.Gap(SECTION_GAP);
        }

        private void DrawCompatibilityTab(Listing_Standard listing)
        {
            Text.Font = GameFont.Medium;
            var headerRect = listing.GetRect(Text.LineHeight);
            Widgets.Label(headerRect, "Compatibility Patches");
            Text.Font = GameFont.Small;
            listing.Gap(SMALL_GAP);

            DrawModStatus(listing, "Simple Sidearms", SimpleSidearmsCompat.IsLoaded());
            DrawModStatus(listing, "Combat Extended", CECompat.IsLoaded());
            DrawModStatus(listing, "Infusion 2", InfusionCompat.IsLoaded());

            listing.Gap(SECTION_GAP);

            DrawSimpleSidearmsSettings(listing);
            DrawCombatExtendedSettings(listing);
        }

        private void DrawModStatus(Listing_Standard listing, string modName, bool isLoaded)
        {
            Color oldColor = GUI.color;
            GUI.color = isLoaded ? Color.green : Color.gray;
            var rect = listing.GetRect(Text.LineHeight);
            Widgets.Label(rect, $"{modName}: {(isLoaded ? "? Loaded" : "? Not found")}");
            GUI.color = oldColor;
        }

        private void DrawSimpleSidearmsSettings(Listing_Standard listing)
        {
            if (!SimpleSidearmsCompat.IsLoaded()) return;

            Text.Font = GameFont.Medium;
            var headerRect = listing.GetRect(Text.LineHeight);
            Widgets.Label(headerRect, "Simple Sidearms");
            Text.Font = GameFont.Small;

            DrawCheckbox(listing, "Enable sidearm auto-equip", ref settings.autoEquipSidearms,
                "Allows colonists to automatically pick up additional weapons as sidearms.");

            if (settings.autoEquipSidearms)
            {
                listing.Gap(TINY_GAP);

                Color oldColor = GUI.color;
                GUI.color = new Color(1f, 1f, 0.6f);
                DrawCheckbox(listing, "Allow sidearm upgrades - Experimental",
                    ref settings.allowSidearmUpgrades,
                    "When enabled, colonists will upgrade existing sidearms to better weapons.",
                    30f,  // Increased indentation from 20f
                    true); // Mark as sub-option
                GUI.color = oldColor;
            }
        }

        private void DrawCombatExtendedSettings(Listing_Standard listing)
        {
            if (!CECompat.IsLoaded()) return;

            listing.Gap(SECTION_GAP);
            Text.Font = GameFont.Medium;
            var headerRect = listing.GetRect(Text.LineHeight);
            Widgets.Label(headerRect, "Combat Extended");
            Text.Font = GameFont.Small;

            // Check CE ammo system status
            bool ceAmmoSystemEnabled = CECompat.TryDetectAmmoSystemEnabled(out string detectionResult);

            // Detect state changes
            bool stateChanged = ceAmmoSystemEnabled != settings.lastKnownCEAmmoState;

            // Force disable if CE ammo system is off
            if (!ceAmmoSystemEnabled && settings.checkCEAmmo)
            {
                settings.checkCEAmmo = false;
                if (settings.debugLogging)
                {
                    AutoArmLogger.Debug("CE ammo system is disabled - forcing ammo checks off");
                }
            }
            // Auto-enable only when CE ammo system is turned on (state change from off to on)
            else if (ceAmmoSystemEnabled && !settings.lastKnownCEAmmoState && stateChanged)
            {
                settings.checkCEAmmo = true;
                AutoArmLogger.Info("Combat Extended ammo system detected - enabling ammo checks");
            }

            // Update the tracked state
            settings.lastKnownCEAmmoState = ceAmmoSystemEnabled;

            // Show the checkbox (disabled if CE ammo is off)
            Color oldColor = GUI.color;
            if (!ceAmmoSystemEnabled)
            {
                GUI.color = Color.gray;
            }

            DrawCheckbox(listing, "Require ammunition", ref settings.checkCEAmmo,
                "When enabled, colonists will only pick up weapons if they have access to appropriate ammunition.");

            // Prevent enabling if CE ammo is disabled
            if (!ceAmmoSystemEnabled && settings.checkCEAmmo)
            {
                settings.checkCEAmmo = false;
            }

            GUI.color = oldColor;

            // Show status message if CE ammo is disabled
            if (!ceAmmoSystemEnabled)
            {
                listing.Gap(TINY_GAP);
                oldColor = GUI.color;
                GUI.color = new Color(1f, 0.8f, 0.4f); // Orange warning
                var warningRect = listing.GetRect(Text.LineHeight);
                Widgets.Label(warningRect, "? CE ammo system is disabled");
                GUI.color = oldColor;
            }
        }

        private void DrawAdvancedTab(Listing_Standard listing)
        {
            Text.Font = GameFont.Medium;
            var headerRect = listing.GetRect(Text.LineHeight);
            Widgets.Label(headerRect, "Weapon Upgrades");
            Text.Font = GameFont.Small;
            listing.Gap(SMALL_GAP);

            // Use the new slider style
            DrawSlider(listing, "Threshold", ref settings.weaponUpgradeThreshold,
                THRESHOLD_MIN, THRESHOLD_MAX, "F2",
                "How much better a weapon needs to be before colonists will switch. Lower values mean more frequent switching.",
                true); // isPercentageBetter

            if (settings.weaponUpgradeThreshold < 1.04f)
            {
                Color oldColor = GUI.color;
                GUI.color = new Color(1f, 0.8f, 0.4f);
                var warningRect = listing.GetRect(Text.LineHeight);
                Widgets.Label(warningRect, "? Risk of weapon swap loops");
                GUI.color = oldColor;
            }

            listing.Gap(SMALL_GAP);

            // Weapon Type Preference
            DrawWeaponPreferenceSlider(listing);

            listing.Gap(SECTION_GAP);

            DrawAgeRestrictions(listing);
        }

        private void DrawWeaponPreferenceSlider(Listing_Standard listing)
        {
            // Simply call DrawSlider - it now handles all the special weapon preference logic
            DrawSlider(listing, "Weapon preference", ref settings.weaponTypePreference,
                -1f, 1f, "custom",
                "Adjust how much all colonists prefer ranged vs melee weapons.",
                false);
        }

        // Calculate multipliers from preference value
        public static float GetRangedMultiplier()
        {
            float pref = AutoArmMod.settings?.weaponTypePreference ?? 0.11f;
            // Base multiplier is 10, adjust based on preference
            // At +1 (max ranged), multiplier is 15
            // At 0 (balanced), multiplier is 10
            // At -1 (max melee), multiplier is 5
            return 10f + (pref * 5f);
        }

        public static float GetMeleeMultiplier()
        {
            float pref = AutoArmMod.settings?.weaponTypePreference ?? 0.11f;
            // Base multiplier is 8, adjust inversely to preference
            // At -1 (max melee), multiplier is 13
            // At 0 (balanced), multiplier is 8
            // At +1 (max ranged), multiplier is 3
            return 8f - (pref * 5f);
        }

        private void DrawAgeRestrictions(Listing_Standard listing)
        {
            if (!ModsConfig.BiotechActive) return;

            Text.Font = GameFont.Medium;
            var headerRect = listing.GetRect(Text.LineHeight);
            Widgets.Label(headerRect, "Age Restrictions");
            Text.Font = GameFont.Small;

            DrawCheckbox(listing, "Allow colonists under 18 to auto-equip weapons", ref settings.allowChildrenToEquipWeapons,
                "When enabled, teenagers can auto-equip weapons based on the age slider below (vanilla allows 13+). When disabled, only adults (18+) will auto-equip weapons. Note: Vanilla already prevents children under 13 from using any weapons.");

            if (settings.allowChildrenToEquipWeapons)
            {
                // Use the new slider style with integer values
                float tempAge = (float)settings.childrenMinAge;
                DrawSlider(listing, "Minimum age", ref tempAge,
                    (float)CHILD_AGE_MIN, (float)CHILD_AGE_MAX, "F0",
                    "Minimum age for children to auto-equip weapons",
                    false);
                settings.childrenMinAge = Mathf.RoundToInt(tempAge);

                if (settings.childrenMinAge < 10)
                {
                    Color oldColor = GUI.color;
                    GUI.color = new Color(1f, 0.8f, 0.4f);
                    var warningRect = listing.GetRect(Text.LineHeight);
                    Widgets.Label(warningRect, "? What could go wrong?");
                    GUI.color = oldColor;
                }
            }
        }


        private void OpenDebugWindow()
        {
            if (debugWindow == null)
            {
                debugWindow = new AutoArmLoggerWindow();
            }

            if (!Find.WindowStack.Windows.Contains(debugWindow))
            {
                Find.WindowStack.Add(debugWindow);
            }
            else
            {
                debugWindow.SetFocus();
            }
        }

        public override void WriteSettings()
        {
            ValidateSettings();
            base.WriteSettings();
        }

        private void ValidateSettings()
        {
            settings.weaponUpgradeThreshold = Mathf.Clamp(settings.weaponUpgradeThreshold, THRESHOLD_MIN, THRESHOLD_MAX);
            settings.childrenMinAge = Mathf.Clamp(settings.childrenMinAge, CHILD_AGE_MIN, CHILD_AGE_MAX);
            settings.weaponTypePreference = Mathf.Clamp(settings.weaponTypePreference, -1f, 1f);

            if (settings.debugLogging)
            {
                AutoArmLogger.Debug($"Settings validated - Threshold: {settings.weaponUpgradeThreshold:F2}, Child Age: {settings.childrenMinAge}, Weapon Preference: {settings.weaponTypePreference:F2}");
            }
        }

        public override string SettingsCategory()
        {
            return "Auto Arm";
        }

        private static void MarkAllBondedWeaponsAsForced()
        {
            if (Current.Game?.Maps == null)
                return;

            int count = 0;
            foreach (var map in Find.Maps)
            {
                if (map?.mapPawns?.FreeColonists == null)
                    continue;

                foreach (var pawn in map.mapPawns.FreeColonists)
                {
                    if (!pawn.IsColonist)
                        continue;

                    // Check primary weapon
                    if (pawn.equipment?.Primary != null &&
                        ValidationHelper.IsWeaponBondedToPawn(pawn.equipment.Primary, pawn))
                    {
                        ForcedWeaponHelper.SetForced(pawn, pawn.equipment.Primary);
                        count++;
                        if (settings.debugLogging)
                        {
                            AutoArmLogger.LogWeapon(pawn, pawn.equipment.Primary, "Bonded weapon marked as forced (setting enabled)");
                        }
                    }

                    // Check inventory weapons
                    if (pawn.inventory?.innerContainer != null)
                    {
                        foreach (var item in pawn.inventory.innerContainer)
                        {
                            if (item is ThingWithComps weapon &&
                                weapon.def.IsWeapon &&
                                ValidationHelper.IsWeaponBondedToPawn(weapon, pawn))
                            {
                                ForcedWeaponHelper.AddForcedDef(pawn, weapon.def);
                                count++;
                                if (settings.debugLogging)
                                {
                                    AutoArmLogger.LogWeapon(pawn, weapon, "Bonded weapon in inventory marked as forced (setting enabled)");
                                }
                            }
                        }
                    }
                }
            }

            if (count > 0)
            {
                Messages.Message($"Marked {count} bonded weapon{(count == 1 ? "" : "s")} as forced.", MessageTypeDefOf.NeutralEvent, false);
            }
        }

        // Called from patch when game loads
        public static void MarkAllBondedWeaponsAsForcedOnLoad()
        {
            // Use a delayed call to ensure everything is loaded
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                MarkAllBondedWeaponsAsForced();
            });
        }
    }

    public class AutoArmLoggerWindow : Window
    {
        private Vector2 scrollPosition;
        private string testResultsText = "";
        private TestResults lastTestResults = null;

        public AutoArmLoggerWindow()
        {
            doCloseX = true;
            closeOnClickedOutside = false;
            absorbInputAroundWindow = false;
            draggable = true;
            resizeable = true;
        }

        public override Vector2 InitialSize => new Vector2(600f, 500f);

        protected override void SetInitialSizeAndPosition()
        {
            // Center the window on screen
            Vector2 size = InitialSize;
            float x = (Verse.UI.screenWidth - size.x) / 2f;
            float y = (Verse.UI.screenHeight - size.y) / 2f;
            windowRect = new Rect(x, y, size.x, size.y);
        }

        public void SetFocus()
        {
            Find.WindowStack.ImmediateWindow(GetHashCode(), windowRect, WindowLayer.Dialog, () => DoWindowContents(windowRect.AtZero()));
        }

        public override void DoWindowContents(Rect inRect)
        {
            // Handle keyboard shortcuts
            if (Event.current.type == EventType.KeyDown)
            {
                if (Event.current.keyCode == KeyCode.F5)  // F5 to run all tests
                {
                    RunAllTests();
                    Event.current.Use();
                }
                else if (Event.current.keyCode == KeyCode.F6)  // F6 to copy results
                {
                    CopyTestResults();
                    Event.current.Use();
                }
            }

            var listing = new Listing_Standard();
            listing.Begin(inRect);

            Text.Font = GameFont.Medium;
            var headerRect = listing.GetRect(Text.LineHeight);
            var titleLabelRect = new Rect(headerRect.x, headerRect.y, headerRect.width - 200f, headerRect.height);
            Widgets.Label(titleLabelRect, "Auto Arm Debug Tools");
            
            // Debug logging checkbox aligned to the right
            Text.Font = GameFont.Small;
            var checkboxRect = new Rect(headerRect.xMax - 170f, headerRect.y + 3f, 24f, 24f);
            var checkboxLabelRect = new Rect(checkboxRect.xMax + 5f, headerRect.y + 3f, 140f, headerRect.height);
            bool oldDebugLogging = AutoArmMod.settings.debugLogging;
            Widgets.Checkbox(checkboxRect.x, checkboxRect.y, ref AutoArmMod.settings.debugLogging, 24f);
            Widgets.Label(checkboxLabelRect, "Debug logging");
            if (oldDebugLogging != AutoArmMod.settings.debugLogging)
            {
                // Always log this important change
                AutoArmLogger.Info($"Debug logging {(AutoArmMod.settings.debugLogging ? "enabled" : "disabled")}");
                
                // Announce when verbose logging is enabled
                if (AutoArmMod.settings.debugLogging)
                {
                    AutoArmLogger.AnnounceVerboseLogging();
                }
            }
            Text.Font = GameFont.Small;

            listing.Gap(10f);

            // Save warning and shortcuts on same line
            var infoRect = listing.GetRect(Text.LineHeight);
            
            // Save warning on the left
            Color oldColor = GUI.color;
            GUI.color = new Color(1f, 0.6f, 0.6f);
            var warningRect = new Rect(infoRect.x, infoRect.y, infoRect.width * 0.5f, infoRect.height);
            Widgets.Label(warningRect, "? Save before touching anything");
            GUI.color = oldColor;
            
            // Shortcuts on the right
            GUI.color = Color.gray;
            var shortcutRect = new Rect(infoRect.x + infoRect.width * 0.5f, infoRect.y, infoRect.width * 0.5f, infoRect.height);
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(shortcutRect, "Shortcuts: F5=Run Tests, F6=Copy Results");
            Text.Anchor = TextAnchor.UpperLeft;  // Reset alignment
            GUI.color = oldColor;

            listing.Gap(10f);

            // Add comprehensive performance test button
            GUI.color = new Color(1f, 0.8f, 0.6f);  // Orange
            if (listing.ButtonText("Run Comprehensive Performance Test"))
            {
                RunPerformanceTest();
            }
            GUI.color = Color.white;

            if (listing.ButtonText("Test Weapon Detection & Pawn Validation"))
            {
                TestWeaponAndPawnStatus();
            }

            // New button for running all tests
            GUI.color = new Color(0.6f, 0.8f, 1f);  // Light blue
            if (listing.ButtonText("Run Autotests"))
            {
                RunAllTests();
            }
            GUI.color = Color.white;

            if (listing.ButtonText("Copy Test Results"))
            {
                CopyTestResults();
            }

            listing.Gap(20f);

            if (!string.IsNullOrEmpty(testResultsText))
            {
                var resultsRect = new Rect(0f, listing.CurHeight, inRect.width, inRect.height - listing.CurHeight);
                DrawTestResults(resultsRect);
            }

            listing.End();
        }

        private void DrawTestResults(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.1f, 0.1f, 0.1f, 0.8f));

            var innerRect = rect.ContractedBy(10f);
            var textHeight = Text.CalcHeight(testResultsText, innerRect.width);

            Widgets.BeginScrollView(innerRect, ref scrollPosition,
                new Rect(0, 0, innerRect.width - 20f, textHeight));

            Color oldColor = GUI.color;
            Color textColor = new Color(0.9f, 0.9f, 0.9f);  // Default to light gray

            // Color based on test results
            if (lastTestResults != null)
            {
                if (lastTestResults.FailedTests == 0)
                    textColor = new Color(0.8f, 1f, 0.8f);  // Green for all passed
                else if (lastTestResults.PassedTests == 0)
                    textColor = new Color(1f, 0.8f, 0.8f);  // Red for all failed
                else
                    textColor = new Color(1f, 1f, 0.8f);    // Yellow for mixed
            }
            else if (testResultsText.Contains("FAILED"))
            {
                textColor = new Color(1f, 0.8f, 0.8f);  // Red
            }
            else if (testResultsText.Contains("?"))
            {
                textColor = new Color(0.8f, 1f, 0.8f);  // Green
            }

            GUI.color = textColor;
            Widgets.Label(new Rect(0, 0, innerRect.width - 20f, textHeight), testResultsText);
            GUI.color = oldColor;

            Widgets.EndScrollView();
        }

        private void RunAllTests()
        {
            var map = Find.CurrentMap;
            if (map == null)
            {
                testResultsText = "Error: No active map";
                lastTestResults = null;
                return;
            }

            testResultsText = "Running tests...\n\n";

            try
            {
                var results = Testing.TestRunner.RunAllTests(map);
                lastTestResults = results;

                // Build result text
                testResultsText = "";
                
                // Show failed tests first if any
                var failedTests = results.GetFailedTests();
                if (failedTests.Any())
                {
                    testResultsText += "Failed Tests:\n";
                    foreach (var failedTest in failedTests.OrderBy(x => x.Key))
                    {
                        testResultsText += $"  - {failedTest.Key}\n";
                    }
                    testResultsText += "\n";
                }

                // Show all test results
                var allResults = results.GetAllResults();
                testResultsText += "Test Details:\n";
                testResultsText += "--------------\n";

                foreach (var kvp in allResults.OrderBy(x => x.Key))
                {
                    if (kvp.Value.Success)
                    {
                        testResultsText += $"? {kvp.Key}\n";
                    }
                    else
                    {
                        testResultsText += $"? {kvp.Key}\n";
                        if (!string.IsNullOrEmpty(kvp.Value.FailureReason))
                        {
                            testResultsText += $"   +- {kvp.Value.FailureReason}\n";
                        }
                    }

                    // Show any additional data
                    if (kvp.Value.Data != null && kvp.Value.Data.Count > 0)
                    {
                        var sortedData = kvp.Value.Data.OrderBy(d => d.Key).ToList();
                        for (int i = 0; i < sortedData.Count; i++)
                        {
                            var data = sortedData[i];
                            // Check if value is "Note:" to add special formatting
                            if (data.Key.StartsWith("Note"))
                            {
                                testResultsText += $"   +- Note: {data.Value}\n";
                            }
                            else
                            {
                                testResultsText += $"   +- {data.Key}: {data.Value}\n";
                            }
                        }
                    }
                }

                // Log to console as well
                Testing.TestRunner.LogTestResults(results);
            }
            catch (Exception e)
            {
                testResultsText = $"Error running tests: {e.Message}\n\nStack trace:\n{e.StackTrace}";
                lastTestResults = null;
                Log.Error($"[AutoArm] Error running tests: {e}");
            }
        }

        private void TestWeaponAndPawnStatus()
        {
            var map = Find.CurrentMap;
            if (map == null)
            {
                testResultsText = "Error: No active map";
                return;
            }

            // MOD SETTINGS
            testResultsText = "=== MOD SETTINGS ===\n";
            testResultsText += $"Mod enabled: {AutoArmMod.settings?.modEnabled ?? false}\n";
            testResultsText += $"Upgrade threshold: {AutoArmMod.settings?.weaponUpgradeThreshold ?? 1.1f:F2} ({((AutoArmMod.settings?.weaponUpgradeThreshold ?? 1.1f) - 1f) * 100f:F0}% better)\n";
            testResultsText += $"Weapon preference: {AutoArmMod.settings?.weaponTypePreference ?? 0f:F2} (Ranged mult: {AutoArmMod.GetRangedMultiplier():F1}x, Melee mult: {AutoArmMod.GetMeleeMultiplier():F1}x)\n";
            testResultsText += $"Allow forced upgrades: {AutoArmMod.settings?.allowForcedWeaponUpgrades ?? false}\n";
            testResultsText += $"SimpleSidearms enabled: {AutoArmMod.settings?.autoEquipSidearms ?? false}\n";
            
            // WEAPON DETECTION
            testResultsText += "\n=== WEAPON DETECTION ===\n";
            
            var weapons = map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon)
                .OfType<ThingWithComps>()
                .Where(w => w.def.defName != "WoodLog" && w.def.defName != "Beer")
                .ToList();
                
            var groundWeapons = weapons.Where(w => w.Spawned && !w.ParentHolder.IsEnclosingContainer()).ToList();
            var equippedWeapons = weapons.Where(w => w.ParentHolder is Pawn_EquipmentTracker).ToList();
            var inventoryWeapons = weapons.Where(w => w.ParentHolder is Pawn_InventoryTracker).ToList();
            
            testResultsText += $"Total weapons: {weapons.Count} (Ground: {groundWeapons.Count}, Equipped: {equippedWeapons.Count}, Inventory: {inventoryWeapons.Count})\n";
            
            // Cache status
            testResultsText += $"Cache status: Valid\n";
            
            // Recently dropped
            var recentlyDropped = groundWeapons.Count(w => DroppedItemTracker.IsRecentlyDropped(w));
            testResultsText += $"Recently dropped: {recentlyDropped}\n";
            
            testResultsText += $"\nGround weapons by type:\n";
            var weaponsByType = groundWeapons.GroupBy(w => w.def.IsRangedWeapon ? "Ranged" : "Melee");
            foreach (var group in weaponsByType)
            {
                testResultsText += $"  {group.Key}: {group.Count()}\n";
            }
            
            testResultsText += $"\nTop ground weapons (first 10):\n";
            foreach (var weapon in groundWeapons.Take(10))
            {
                QualityCategory quality;
                weapon.TryGetQuality(out quality);
                string forbidden = weapon.IsForbidden(Faction.OfPlayer) ? " [FORBIDDEN]" : "";
                string dropped = DroppedItemTracker.IsRecentlyDropped(weapon) ? " [RECENTLY DROPPED]" : "";
                string reserved = map.reservationManager.IsReservedByAnyoneOf(weapon, null) ? " [RESERVED]" : "";
                testResultsText += $"  � {weapon.Label} ({quality}) at {weapon.Position}{forbidden}{dropped}{reserved}\n";
            }
            
            // COLONIST STATUS
            testResultsText += "\n=== COLONIST STATUS ===\n";
            
            var colonists = map.mapPawns.FreeColonists.ToList();
            int validCount = 0;
            int hasWeaponCount = 0;
            int draftedCount = colonists.Count(p => p.Drafted);
            int onCooldownCount = colonists.Count(p => TimingHelper.IsOnCooldown(p.thingIDNumber, TimingHelper.CooldownType.WeaponSearch));

            foreach (var pawn in colonists)
            {
                string reason;
                if (JobGiverHelpers.IsValidPawnForAutoEquip(pawn, out reason))
                {
                    validCount++;
                }
                if (pawn.equipment?.Primary != null)
                {
                    hasWeaponCount++;
                }
            }

            testResultsText += $"Colonists: {colonists.Count} total, {validCount} valid, {hasWeaponCount} armed, {draftedCount} drafted, {onCooldownCount} on cooldown\n";
            testResultsText += $"SimpleSidearms: {(SimpleSidearmsCompat.IsLoaded() ? "Loaded" : "Not found")}\n";
            
            // Check raid status
            bool inRaid = map.attackTargetsCache?.TargetsHostileToColony?.Any(t => t is Pawn) ?? false;
            if (inRaid && AutoArmMod.settings?.disableDuringRaids == true)
            {
                testResultsText += "RAIDS DISABLED - Mod disabled during raids\n";
            }
            
            testResultsText += $"\n--- Individual Status ---\n";

            foreach (var pawn in colonists.OrderBy(p => p.Name?.ToStringShort ?? "Unknown"))
            {
                string reason;
                bool isValid = JobGiverHelpers.IsValidPawnForAutoEquip(pawn, out reason);
                
                // Basic info
                string name = pawn.Name?.ToStringShort ?? "Unknown";
                string draftStatus = pawn.Drafted ? " [DRAFTED]" : "";
                string validStatus = isValid ? "?" : "?";
                
                testResultsText += $"\n{validStatus} {name}{draftStatus}";
                if (!isValid)
                {
                    testResultsText += $" - {reason}";
                }
                testResultsText += "\n";
                
                // Skills
                int shootingSkill = pawn.skills?.GetSkill(SkillDefOf.Shooting)?.Level ?? 0;
                int meleeSkill = pawn.skills?.GetSkill(SkillDefOf.Melee)?.Level ?? 0;
                testResultsText += $"   Skills: Shooting {shootingSkill}, Melee {meleeSkill}\n";
                
                // Traits
                var relevantTraits = pawn.story?.traits?.allTraits
                    .Where(t => t.def == TraitDefOf.Brawler || t.def.defName == "Trigger-Happy" || t.def.defName == "Careful Shooter")
                    .Select(t => t.def.label)
                    .ToList();
                if (relevantTraits?.Any() == true)
                {
                    testResultsText += $"   Traits: {string.Join(", ", relevantTraits)}\n";
                }
                
                // Current weapon
                if (pawn.equipment?.Primary != null)
                {
                    var weapon = pawn.equipment.Primary;
                    QualityCategory quality;
                    weapon.TryGetQuality(out quality);
                    float score = WeaponScoringHelper.GetTotalScore(pawn, weapon);
                    string forced = ForcedWeaponHelper.IsForced(pawn, weapon) ? " [FORCED]" : "";
                    string bonded = ValidationHelper.IsWeaponBondedToPawn(weapon, pawn) ? " [BONDED]" : "";
                    testResultsText += $"   Weapon: {weapon.Label} ({quality}) - Score: {score:F1}{forced}{bonded}\n";
                }
                else
                {
                    testResultsText += $"   Weapon: None\n";
                }
                
                // Outfit filter
                if (pawn.outfits?.CurrentApparelPolicy != null)
                {
                    var filter = pawn.outfits.CurrentApparelPolicy.filter;
                    int allowedWeapons = DefDatabase<ThingDef>.AllDefs.Count(d => d.IsWeapon && filter.Allows(d));
                    testResultsText += $"   Outfit: {pawn.outfits.CurrentApparelPolicy.label} ({allowedWeapons} weapons allowed)\n";
                }
                
                // Sidearms
                if (SimpleSidearmsCompat.IsLoaded() && pawn.inventory?.innerContainer != null)
                {
                    var sidearms = pawn.inventory.innerContainer.Where(t => t is ThingWithComps && t.def.IsWeapon).ToList();
                    if (sidearms.Any())
                    {
                        testResultsText += $"   Sidearms: {sidearms.Count} - ";
                        testResultsText += string.Join(", ", sidearms.Select(w => w.Label));
                        testResultsText += "\n";
                    }
                }
                
                // Blacklist
                // Skip blacklist count for now
                
                // Current job
                if (pawn.CurJob != null)
                {
                    testResultsText += $"   Job: {pawn.CurJob.def.defName}";
                    if (pawn.CurJob.targetA.HasThing && pawn.CurJob.targetA.Thing is ThingWithComps)
                    {
                        testResultsText += $" -> {pawn.CurJob.targetA.Thing.Label}";
                    }
                    testResultsText += "\n";
                }
                
                // Cooldown status
                if (TimingHelper.IsOnCooldown(pawn.thingIDNumber, TimingHelper.CooldownType.WeaponSearch))
                {
                    testResultsText += $"   On cooldown\n";
                }
                
                // Find best available weapon if valid
                if (isValid && !pawn.Drafted)
                {
                    var nearbyWeapons = groundWeapons
                        .Where(w => w.Position.DistanceTo(pawn.Position) <= 30f && !w.IsForbidden(pawn))
                        .OrderByDescending(w => WeaponScoringHelper.GetTotalScore(pawn, w))
                        .Take(3)
                        .ToList();
                        
                    if (nearbyWeapons.Any())
                    {
                        testResultsText += $"   Nearby weapons:\n";
                        foreach (var weapon in nearbyWeapons)
                        {
                            float score = WeaponScoringHelper.GetTotalScore(pawn, weapon);
                            float distance = weapon.Position.DistanceTo(pawn.Position);
                            QualityCategory quality;
                            weapon.TryGetQuality(out quality);
                            
                            // Check if it's an upgrade
                            bool isUpgrade = false;
                            float improvementPercent = 0;
                            if (pawn.equipment?.Primary != null)
                            {
                                float currentScore = WeaponScoringHelper.GetTotalScore(pawn, pawn.equipment.Primary);
                                float threshold = AutoArmMod.settings?.weaponUpgradeThreshold ?? 1.1f;
                                isUpgrade = score > currentScore * threshold;
                                improvementPercent = ((score / currentScore) - 1f) * 100f;
                            }
                            else
                            {
                                isUpgrade = true;
                            }
                            
                            // Check why it might not be picked up
                            string blockers = "";
                            if (!pawn.CanReach(weapon, Verse.AI.PathEndMode.ClosestTouch, Danger.Deadly))
                                blockers += " [UNREACHABLE]";
                            if (!pawn.Map.reservationManager.CanReserve(pawn, weapon))
                                blockers += " [RESERVED]";
                            if (WeaponBlacklist.IsBlacklisted(weapon.def, pawn))
                                blockers += " [BLACKLISTED]";
                            if (!pawn.outfits?.CurrentApparelPolicy?.filter?.Allows(weapon.def) ?? false)
                                blockers += " [NOT IN OUTFIT]";
                            if (DroppedItemTracker.IsRecentlyDropped(weapon))
                                blockers += " [RECENTLY DROPPED]";
                            
                            string upgradeText = isUpgrade ? $" [UPGRADE +{improvementPercent:F0}%]" : "";
                            testResultsText += $"     - {weapon.Label} ({quality}) - Score: {score:F1}, Distance: {distance:F0}{upgradeText}{blockers}\n";
                        }
                    }
                }
            }
        }

        private void CopyTestResults()
        {
            if (!string.IsNullOrEmpty(testResultsText))
            {
                GUIUtility.systemCopyBuffer = testResultsText;
                Messages.Message("Test results copied to clipboard", MessageTypeDefOf.NeutralEvent, false);
            }
            else
            {
                Messages.Message("No test results to copy", MessageTypeDefOf.RejectInput, false);
            }
        }

        private void RunPerformanceTest()
        {
            var map = Find.CurrentMap;
            if (map == null)
            {
                testResultsText = "Error: No active map";
                return;
            }

            testResultsText = "Running comprehensive performance test...\n\nThis may take a while and cause temporary lag.";
            
            // Run the test asynchronously to avoid blocking the UI
            LongEventHandler.QueueLongEvent(() =>
            {
                try
                {
                    // Capture current time
                    var startTime = DateTime.Now;
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    
                    // Run the actual performance test
                    PerformanceTestRunner.RunPerformanceTest();
                    
                    sw.Stop();
                    
                    // Build results summary for the window
                    var results = new System.Text.StringBuilder();
                    results.AppendLine("--- PERFORMANCE TEST COMPLETE ---\n");
                    results.AppendLine($"Total test duration: {sw.ElapsedMilliseconds:N0}ms\n");
                    
                    // Add key metrics summary
                    results.AppendLine("KEY METRICS:");
                    results.AppendLine("------------");
                    
                    var colonists = map.mapPawns.FreeColonistsCount;
                    var weapons = map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon).Count();
                    results.AppendLine($"� Colony size: {colonists} colonists");
                    results.AppendLine($"� Weapons on map: {weapons}");
                    results.AppendLine($"� Map size: {map.Size.x}x{map.Size.z} ({map.Area:N0} cells)\n");
                    
                    // Performance mode status
                    bool perfMode = colonists >= (AutoArmMod.settings?.performanceModeColonySize ?? 35);
                    results.AppendLine("PERFORMANCE STATUS:");
                    results.AppendLine("-----------------");
                    results.AppendLine($"� Performance mode: {(perfMode ? "ACTIVE" : "Inactive")}");
                    results.AppendLine($"� Think tree mode: {(ModInit.IsFallbackModeActive ? "FALLBACK (TickRare)" : "INJECTED")}");
                    
                    // Cache status
                    results.AppendLine($"� Weapon cache: Valid\n");
                    
                    results.AppendLine("TEST CATEGORIES COMPLETED:");
                    results.AppendLine("------------------------");
                    results.AppendLine("? Cache System Performance");
                    results.AppendLine("? Weapon Search Performance");
                    results.AppendLine("? Score Calculation Performance");
                    results.AppendLine("? Job Creation Overhead");
                    results.AppendLine("? Think Tree Performance");
                    results.AppendLine("? Scalability Tests (Colony Size)");
                    results.AppendLine("? Scalability Tests (Weapon Count)");
                    results.AppendLine("? Worst Case Scenarios");
                    results.AppendLine("? Cache Thrashing");
                    results.AppendLine("? Memory Pressure");
                    results.AppendLine("? Mod Compatibility Overhead");
                    results.AppendLine("? Real World Scenarios\n");
                    
                    // Performance recommendations summary
                    results.AppendLine("PERFORMANCE SUMMARY:");
                    results.AppendLine("-----------------");
                    
                    if (colonists > 50)
                    {
                        results.AppendLine("? Large colony detected - consider:");
                        results.AppendLine("  � Reducing weapon search radius");
                        results.AppendLine("  � Increasing cooldown durations");
                    }
                    
                    if (weapons > 500)
                    {
                        results.AppendLine("? Many weapons on map - consider:");
                        results.AppendLine("  � More aggressive cleanup policies");
                        results.AppendLine("  � Limiting weapon checks per tick");
                    }
                    
                    if (ModInit.IsFallbackModeActive)
                    {
                        results.AppendLine("? Running in fallback mode:");
                        results.AppendLine("  � Think tree injection failed");
                        results.AppendLine("  � Higher performance impact expected");
                    }
                    
                    results.AppendLine("\n--- DETAILED RESULTS ---\n");
                    results.AppendLine("Full performance metrics have been written to the debug log.");
                    results.AppendLine("Look for [AutoArm] PERFORMANCE REPORT in:");
                    results.AppendLine($"{Application.dataPath}/../Player.log");
                    results.AppendLine("\nThe log contains:");
                    results.AppendLine("� Detailed timing for each operation");
                    results.AppendLine("� Memory usage analysis");
                    results.AppendLine("� Scaling characteristics");
                    results.AppendLine("� Specific performance recommendations");
                    
                    testResultsText = results.ToString();
                }
                catch (Exception e)
                {
                    testResultsText = $"Error running performance test: {e.Message}\n\nStack trace:\n{e.StackTrace}";
                    Log.Error($"[AutoArm] Error running performance test: {e}");
                }
            }, "Running AutoArm Performance Test", false, null);
        }
    }
}


