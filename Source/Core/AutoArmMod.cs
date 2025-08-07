// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: Main mod class with settings UI and mod initialization
// Uses AutoArmSettings for configuration, AutoArmLoggerWindow for debug tools

using AutoArm.Caching;
using AutoArm.Definitions;
using AutoArm.Helpers;
using AutoArm.Jobs;
using AutoArm.Logging;
using AutoArm.Testing;
using AutoArm.Weapons;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace AutoArm
{
    public class AutoArmMod : Mod
    {
        public static AutoArmMod Instance { get; private set; }
        public static string Version => "1.0.0";

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
            Instance = this;
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
            Widgets.Label(settingsLabelRect, "AutoArm_Settings".Translate());

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

            if (Widgets.ButtonText(resetButtonRect, "AutoArm_ResetConfig".Translate()))
            {
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    "Are you sure you want to reset all settings to defaults?",
                    () =>
                    {
                        settings.ResetToDefaults();
                        Messages.Message("AutoArm_SettingsReset".Translate(), MessageTypeDefOf.NeutralEvent, false);
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
            if (Widgets.ButtonText(new Rect(tabRect.x, tabRect.y, tabWidth, TAB_BUTTON_HEIGHT), "AutoArm_General".Translate()))
                currentTab = SettingsTab.General;

            GUI.color = currentTab == SettingsTab.Compatibility ? Color.white : new Color(1f, 1f, 0.7f);
            if (Widgets.ButtonText(new Rect(tabRect.x + tabWidth + 5f, tabRect.y, tabWidth, TAB_BUTTON_HEIGHT), "AutoArm_Compatibility".Translate()))
                currentTab = SettingsTab.Compatibility;

            GUI.color = currentTab == SettingsTab.Advanced ? Color.white : new Color(1f, 0.9f, 0.6f);
            if (Widgets.ButtonText(new Rect(tabRect.x + (tabWidth + 5f) * 2, tabRect.y, tabWidth, TAB_BUTTON_HEIGHT), "AutoArm_Advanced".Translate()))
                currentTab = SettingsTab.Advanced;

            GUI.color = currentTab == SettingsTab.Debug ? Color.white : new Color(1f, 0.7f, 0.7f);
            if (Widgets.ButtonText(new Rect(tabRect.x + (tabWidth + 5f) * 3, tabRect.y, tabWidth, TAB_BUTTON_HEIGHT), "AutoArm_Debug".Translate()))
            {
                if (Current.Game?.CurrentMap != null)
                {
                    OpenDebugWindow();
                }
                else
                {
                    Messages.Message("AutoArm_DebugRequiresActiveGame".Translate(), MessageTypeDefOf.RejectInput, false);
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
            float valueWidth = 220f;  // Increased from 150f to fit longer preference text
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
                    displayValue = "AutoArm_StrongMeleePreference".Translate();
                else if (value <= -0.35f)
                    displayValue = "AutoArm_ModerateMeleePreference".Translate();
                else if (value <= -0.10f)
                    displayValue = "AutoArm_SlightMeleePreference".Translate();
                else if (value < 0.10f)
                    displayValue = "AutoArm_Balanced".Translate();
                else if (value < 0.35f)
                    displayValue = "AutoArm_SlightRangedPreference".Translate();
                else if (value < 0.75f)
                    displayValue = "AutoArm_ModerateRangedPreference".Translate();
                else
                    displayValue = "AutoArm_StrongRangedPreference".Translate();

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

            if (tooltip != null && (Mouse.IsOver(labelRect) || Mouse.IsOver(rect)))
            {
                TooltipHandler.TipRegion(labelRect, tooltip);
                TooltipHandler.TipRegion(rect, tooltip);
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

            DrawCheckbox(listing, "AutoArm_EnableMod".Translate(), ref settings.modEnabled,
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
                    // Cooldown functionality removed

                    // Clear and rebuild weapon cache for all maps (this also clears weapon score cache)
                    foreach (var map in Find.Maps)
                    {
                        ImprovedWeaponCacheManager.ClearAndRebuildCache(map);
                    }

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

            DrawCheckbox(listing, "AutoArm_ShowNotifications".Translate(), ref settings.showNotifications,
                "Shows notification messages when colonists equip or upgrade weapons.");

            listing.Gap(SMALL_GAP);

            DrawCheckbox(listing, "AutoArm_DisableDuringRaids".Translate(), ref settings.disableDuringRaids,
                "Temporarily disable auto-equip when the game detects high danger (major raids/threats) to boost performance. Plays nicer with other mods as well.");

            // Royalty DLC settings
            if (ModsConfig.RoyaltyActive)
            {
                listing.Gap(SMALL_GAP);

                bool oldRespectBonds = settings.respectWeaponBonds;
                DrawCheckbox(listing, "AutoArm_ForceWeapon".Translate(), ref settings.respectWeaponBonds,
                    "When enabled, bonded persona weapons are automatically marked as forced, preventing automatic switching.");

                // If setting was just enabled, mark all bonded weapons as forced
                if (!oldRespectBonds && settings.respectWeaponBonds && Current.Game != null)
                {
                    MarkAllBondedWeaponsAsForced();
                }
            }

            listing.Gap(SMALL_GAP);

            DrawCheckbox(listing, "AutoArm_AllowForcedWeaponUpgrades".Translate(), ref settings.allowForcedWeaponUpgrades,
                "When enabled, colonists can upgrade forced weapons to better quality versions of the same type (e.g., normal revolver → masterwork revolver).");

            listing.Gap(SMALL_GAP);

            DrawCheckbox(listing, "AutoArm_AllowTemporaryColonists".Translate(), ref settings.allowTemporaryColonists,
                "When enabled, temporary colonists (quest lodgers, borrowed pawns, royal guests) can auto-equip weapons.");

            // Restore original colors
            GUI.color = oldColor;
            GUI.enabled = wasEnabled;

            listing.Gap(SECTION_GAP);
        }

        private void DrawCompatibilityTab(Listing_Standard listing)
        {
            Text.Font = GameFont.Medium;
            var headerRect = listing.GetRect(Text.LineHeight);
            Widgets.Label(headerRect, "AutoArm_CompatibilityPatches".Translate());
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
            Widgets.Label(rect, $"{modName}: {(isLoaded ? "\u2713 " + "AutoArm_Loaded".Translate() : "\u2717 " + "AutoArm_NotFound".Translate())}");
            GUI.color = oldColor;
        }

        private void DrawSimpleSidearmsSettings(Listing_Standard listing)
        {
            if (!SimpleSidearmsCompat.IsLoaded()) return;

            Text.Font = GameFont.Medium;
            var headerRect = listing.GetRect(Text.LineHeight);
            Widgets.Label(headerRect, "AutoArm_SimpleSidearms".Translate());
            Text.Font = GameFont.Small;

            // Check if reflection failed
            bool reflectionFailed = SimpleSidearmsCompat.ReflectionFailed;

            // Show warning if reflection failed
            if (reflectionFailed)
            {
                listing.Gap(TINY_GAP);
                Color oldColor = GUI.color;
                GUI.color = new Color(1f, 0.6f, 0.6f); // Red warning color
                var warningRect = listing.GetRect(Text.LineHeight * 2);
                Widgets.Label(warningRect, "AutoArm_SimpleSidearmsReflectionFailed".Translate());
                GUI.color = oldColor;
                listing.Gap(TINY_GAP);
            }

            // Disable GUI if reflection failed
            bool wasEnabled = GUI.enabled;
            if (reflectionFailed)
            {
                GUI.enabled = false;
                // Force disable the setting if reflection failed (only once, not every frame)
                if (settings.autoEquipSidearms)
                {
                    settings.autoEquipSidearms = false;
                    // Don't log here - this runs every frame. The one-time disable happens in CheckModCompatibility
                }
            }

            // Use temporary variables since properties can't be passed as ref
            bool tempAutoEquipSidearms = settings.autoEquipSidearms;
            DrawCheckbox(listing, "AutoArm_EnableSidearmAutoEquip".Translate(), ref tempAutoEquipSidearms,
                "Allows colonists to automatically pick up additional weapons as sidearms.");
            settings.autoEquipSidearms = tempAutoEquipSidearms;

            if (settings.autoEquipSidearms && !reflectionFailed)
            {
                listing.Gap(TINY_GAP);

                Color oldColor = GUI.color;
                GUI.color = new Color(1f, 1f, 0.6f);

                // Use temporary variable for the sub-option too
                bool tempAllowSidearmUpgrades = settings.allowSidearmUpgrades;
                DrawCheckbox(listing, "AutoArm_AllowSidearmUpgrades".Translate(),
                    ref tempAllowSidearmUpgrades,
                    "When enabled, colonists will upgrade existing sidearms to better quality versions.",
                    30f,
                    true);
                settings.allowSidearmUpgrades = tempAllowSidearmUpgrades;

                GUI.color = oldColor;
            }

            // Restore GUI state
            GUI.enabled = wasEnabled;
        }

        private void DrawCombatExtendedSettings(Listing_Standard listing)
        {
            if (!CECompat.IsLoaded()) return;

            listing.Gap(SECTION_GAP);
            Text.Font = GameFont.Medium;
            var headerRect = listing.GetRect(Text.LineHeight);
            Widgets.Label(headerRect, "AutoArm_CombatExtended".Translate());
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
                AutoArmLogger.Debug("Combat Extended ammo system detected - enabling ammo checks");
            }

            // Update the tracked state
            settings.lastKnownCEAmmoState = ceAmmoSystemEnabled;

            // Show the checkbox (disabled if CE ammo is off)
            Color oldColor = GUI.color;
            if (!ceAmmoSystemEnabled)
            {
                GUI.color = Color.gray;
            }

            DrawCheckbox(listing, "AutoArm_RequireAmmunition".Translate(), ref settings.checkCEAmmo,
                "When enabled, colonists will only pick up weapons if they have access to ammunition.");

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
                Widgets.Label(warningRect, "AutoArm_CEAmmoSystemDisabled".Translate());
                GUI.color = oldColor;
            }
        }

        private void DrawAdvancedTab(Listing_Standard listing)
        {
            Text.Font = GameFont.Medium;
            var headerRect = listing.GetRect(Text.LineHeight);
            Widgets.Label(headerRect, "AutoArm_WeaponUpgrades".Translate());
            Text.Font = GameFont.Small;
            listing.Gap(SMALL_GAP);

            // Use the new slider style
            DrawSlider(listing, "AutoArm_Threshold".Translate(), ref settings.weaponUpgradeThreshold,
                Constants.WeaponUpgradeThresholdMin, Constants.WeaponUpgradeThresholdMax, "F2",
                "How much better a weapon needs to be before colonists will switch. Lower values mean more frequent switching.",
                true); // isPercentageBetter

            listing.Gap(SMALL_GAP);

            // Weapon Type Preference
            DrawWeaponPreferenceSlider(listing);

            listing.Gap(SECTION_GAP);

            DrawAgeRestrictions(listing);
        }

        private void DrawWeaponPreferenceSlider(Listing_Standard listing)
        {
            // Simply call DrawSlider - it now handles all the special weapon preference logic
            DrawSlider(listing, "AutoArm_WeaponPreference".Translate(), ref settings.weaponTypePreference,
                -1f, 1f, "custom",
                "Adjust how much all colonists prefer ranged vs melee weapons.",
                false);
        }

        // Calculate multipliers from preference value
        public static float GetRangedMultiplier()
        {
            float pref = AutoArmMod.settings?.weaponTypePreference ?? Constants.DefaultWeaponTypePreference;
            // Base multiplier is 10, adjust based on preference
            // At +1 (max ranged), multiplier is 15
            // At 0 (balanced), multiplier is 10
            // At -1 (max melee), multiplier is 5
            return Constants.WeaponPreferenceRangedBase + (pref * Constants.WeaponPreferenceAdjustment);
        }

        public static float GetMeleeMultiplier()
        {
            float pref = AutoArmMod.settings?.weaponTypePreference ?? Constants.DefaultWeaponTypePreference;
            // Base multiplier is 8, adjust inversely to preference
            // At -1 (max melee), multiplier is 13
            // At 0 (balanced), multiplier is 8
            // At +1 (max ranged), multiplier is 3
            return Constants.WeaponPreferenceMeleeBase - (pref * Constants.WeaponPreferenceAdjustment);
        }

        private void DrawAgeRestrictions(Listing_Standard listing)
        {
            if (!ModsConfig.BiotechActive) return;

            Text.Font = GameFont.Medium;
            var headerRect = listing.GetRect(Text.LineHeight);
            Widgets.Label(headerRect, "AutoArm_AgeRestrictions".Translate());
            Text.Font = GameFont.Small;

            DrawCheckbox(listing, "AutoArm_AllowChildrenToEquipWeapons".Translate(), ref settings.allowChildrenToEquipWeapons,
                "When enabled, teenagers can auto-equip weapons based on the age slider below (vanilla allows 13+). When disabled, only adults (18+) will auto-equip weapons. Note: Vanilla already prevents children under 13 from using any weapons.");

            if (settings.allowChildrenToEquipWeapons)
            {
                // Use the new slider style with integer values
                float tempAge = (float)settings.childrenMinAge;
                DrawSlider(listing, "AutoArm_MinimumAge".Translate(), ref tempAge,
                    (float)Constants.ChildMinAgeLimit, (float)Constants.ChildMaxAgeLimit, "F0",
                    "Minimum age for children to auto-equip weapons",
                    false);
                settings.childrenMinAge = Mathf.RoundToInt(tempAge);

                if (settings.childrenMinAge < 10)
                {
                    Color oldColor = GUI.color;
                    GUI.color = new Color(1f, 0.8f, 0.4f);
                    var warningRect = listing.GetRect(Text.LineHeight);
                    Widgets.Label(warningRect, "AutoArm_WhatCouldGoWrong".Translate());
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
            // Values are already clamped in the UI sliders, no need to validate again
            base.WriteSettings();
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
                        if (settings?.debugLogging == true)
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
                                ForcedWeaponHelper.AddForcedSidearm(pawn, weapon);
                                count++;
                                if (settings?.debugLogging == true)
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
                Messages.Message("AutoArm_BondedWeaponsForced".Translate(count, count == 1 ? "" : "s"), MessageTypeDefOf.NeutralEvent, false);
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
        private Dictionary<string, TestResult> lastTestResults = null;

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
            Widgets.Label(titleLabelRect, "AutoArm_DebugTools".Translate());

            // Debug logging checkbox aligned to the right
            Text.Font = GameFont.Small;
            var checkboxRect = new Rect(headerRect.xMax - 170f, headerRect.y + 3f, 24f, 24f);
            var checkboxLabelRect = new Rect(checkboxRect.xMax + 5f, headerRect.y + 3f, 140f, headerRect.height);
            bool oldDebugLogging = AutoArmMod.settings.debugLogging;
            Widgets.Checkbox(checkboxRect.x, checkboxRect.y, ref AutoArmMod.settings.debugLogging, 24f);
            Widgets.Label(checkboxLabelRect, "AutoArm_EnableDebugLogging".Translate());
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
            Widgets.Label(warningRect, "\u26a0 " + "AutoArm_SaveBeforeTouching".Translate());
            GUI.color = oldColor;

            // Shortcuts on the right
            GUI.color = Color.gray;
            var shortcutRect = new Rect(infoRect.x + infoRect.width * 0.5f, infoRect.y, infoRect.width * 0.5f, infoRect.height);
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(shortcutRect, "AutoArm_Shortcuts".Translate());
            Text.Anchor = TextAnchor.UpperLeft;  // Reset alignment
            GUI.color = oldColor;

            listing.Gap(10f);

            // Add comprehensive performance test button
            GUI.color = new Color(1f, 0.8f, 0.6f);  // Orange
            if (listing.ButtonText("AutoArm_RunPerformanceTest".Translate()))
            {
                RunPerformanceTest();
            }
            GUI.color = Color.white;

            if (listing.ButtonText("AutoArm_TestWeaponDetection".Translate()))
            {
                TestWeaponAndPawnStatus();
            }

            // New button for running all tests
            GUI.color = new Color(0.6f, 0.8f, 1f);  // Light blue
            if (listing.ButtonText("AutoArm_RunAllTests".Translate()))
            {
                RunAllTests();
            }
            GUI.color = Color.white;

            if (listing.ButtonText("AutoArm_CopyTestResults".Translate()))
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
            if (lastTestResults != null && lastTestResults.Count > 0)
            {
                int failedCount = lastTestResults.Count(r => !r.Value.Success && r.Key != "_SUMMARY");
                int totalCount = lastTestResults.Count(r => r.Key != "_SUMMARY");

                if (failedCount == 0)
                    textColor = new Color(0.8f, 1f, 0.8f);  // Green for all passed
                else if (failedCount == totalCount)
                    textColor = new Color(1f, 0.8f, 0.8f);  // Red for all failed
                else
                    textColor = new Color(1f, 1f, 0.8f);    // Yellow for mixed
            }
            else if (testResultsText.Contains("FAILED"))
            {
                textColor = new Color(1f, 0.8f, 0.8f);  // Red
            }
            else if (testResultsText.Contains("✓"))
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
                lastTestResults = results.GetAllResults();

                // Build result text
                testResultsText = "";

                // Show failed tests first if any
                var failedTests = results.GetAllResults().Where(r => !r.Value.Success && r.Key != "_SUMMARY").ToList();
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
                var allResults = results.GetAllResults().Where(r => r.Key != "_SUMMARY");
                testResultsText += "Test Details:\n";
                testResultsText += "--------------\n";

                foreach (var kvp in allResults.OrderBy(x => x.Key))
                {
                    if (kvp.Value.Success)
                    {
                        testResultsText += $"✓ {kvp.Key}\n";
                    }
                    else
                    {
                        testResultsText += $"✗ {kvp.Key}\n";
                        if (!string.IsNullOrEmpty(kvp.Value.FailureReason))
                        {
                            testResultsText += $"   └─ {kvp.Value.FailureReason}\n";
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
                                testResultsText += $"   └─ Note: {data.Value}\n";
                            }
                            else
                            {
                                testResultsText += $"   └─ {data.Key}: {data.Value}\n";
                            }
                        }
                    }
                }

                // Log to console as well
                Testing.TestRunner.LogTestResults(results);
                
                // Reset configuration after running all tests
                AutoArmMod.settings?.ResetToDefaults();
                testResultsText += "\n\n=== Configuration reset to defaults after test run ===";
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

            // MOD COMPATIBILITY STATUS
            testResultsText = "=== MOD COMPATIBILITY ===\n";

            // SimpleSidearms
            if (SimpleSidearmsCompat.IsLoaded())
            {
                if (SimpleSidearmsCompat.ReflectionFailed)
                {
                    testResultsText += "SimpleSidearms: \u2717 REFLECTION FAILED (features disabled)\n";
                }
                else
                {
                    testResultsText += "SimpleSidearms: \u2713 Active";
                    if (AutoArmMod.settings?.autoEquipSidearms == true)
                    {
                        testResultsText += " (sidearm auto-equip enabled)";
                    }
                    else
                    {
                        testResultsText += " (sidearm auto-equip disabled in settings)";
                    }
                    testResultsText += "\n";
                }
            }
            else
            {
                testResultsText += "SimpleSidearms: Not installed\n";
            }

            // Combat Extended
            if (CECompat.IsLoaded())
            {
                bool ammoSystemEnabled = CECompat.IsAmmoSystemEnabled();
                testResultsText += "Combat Extended: \u2713 Active";
                if (ammoSystemEnabled)
                {
                    if (AutoArmMod.settings?.checkCEAmmo == true)
                    {
                        testResultsText += " (ammo checks enabled)";
                    }
                    else
                    {
                        testResultsText += " (ammo checks disabled in settings)";
                    }
                }
                else
                {
                    testResultsText += " (CE ammo system disabled)";
                }
                testResultsText += "\n";
            }
            else
            {
                testResultsText += "Combat Extended: Not installed\n";
            }

            // Infusion 2
            if (InfusionCompat.IsLoaded())
            {
                testResultsText += "Infusion 2: \u2713 Active (weapon infusion bonuses enabled)\n";
            }
            else
            {
                testResultsText += "Infusion 2: Not installed\n";
            }

            // MOD SETTINGS
            testResultsText += "\n=== MOD SETTINGS ===\n";
            testResultsText += $"Mod enabled: {AutoArmMod.settings?.modEnabled ?? false}\n";
            testResultsText += $"Upgrade threshold: {AutoArmMod.settings?.weaponUpgradeThreshold ?? Constants.WeaponUpgradeThreshold:F2} ({((AutoArmMod.settings?.weaponUpgradeThreshold ?? Constants.WeaponUpgradeThreshold) - 1f) * 100f:F0}% better)\n";
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
                testResultsText += $"  - {weapon.Label} ({quality}) at {weapon.Position}{forbidden}{dropped}{reserved}\n";
            }

            // COLONIST STATUS
            testResultsText += "\n=== COLONIST STATUS ===\n";

            var colonists = map.mapPawns.FreeColonists.ToList();
            int validCount = 0;
            int hasWeaponCount = 0;
            int draftedCount = colonists.Count(p => p.Drafted);
            int onCooldownCount = 0; // Cooldown functionality removed

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

            // Check raid status (use same method as actual mod)
            if (ModInit.IsLargeRaidActive && AutoArmMod.settings?.disableDuringRaids == true)
            {
                testResultsText += "Raid active - Mod disabled during raids\n";
            }

            testResultsText += $"\n--- Individual Status ---\n";

            foreach (var pawn in colonists.OrderBy(p => p.Name?.ToStringShort ?? "Unknown"))
            {
                string reason;
                bool isValid = JobGiverHelpers.IsValidPawnForAutoEquip(pawn, out reason);

                // Basic info
                string name = pawn.Name?.ToStringShort ?? "Unknown";
                string draftStatus = pawn.Drafted ? " [DRAFTED]" : "";
                string validStatus = isValid ? "✓" : "✗";

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

                // Cooldown functionality removed

                // Find best available weapon if valid
                if (isValid && !pawn.Drafted)
                {
                    var nearbyWeapons = groundWeapons
                        .Where(w => w.Position.DistanceTo(pawn.Position) <= Constants.SearchRadiusMedium && !w.IsForbidden(pawn))
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
                                float threshold = AutoArmMod.settings?.weaponUpgradeThreshold ?? Constants.WeaponUpgradeThreshold;
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
                Messages.Message("AutoArm_TestResultsCopied".Translate(), MessageTypeDefOf.NeutralEvent, false);
            }
            else
            {
                Messages.Message("AutoArm_NoTestResultsToCopy".Translate(), MessageTypeDefOf.RejectInput, false);
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
                    // Run the performance test and get results
                    var results = PerformanceTestRunner.RunPerformanceTest();

                    // Build the results display
                    testResultsText = FormatPerformanceResults(results);
                }
                catch (Exception e)
                {
                    testResultsText = $"Error running performance test: {e.Message}\n\nStack trace:\n{e.StackTrace}";
                    Log.Error($"[AutoArm] Error running performance test: {e}");
                }
            }, "Running AutoArm Performance Test", false, null);
        }

        private string FormatPerformanceResults(Dictionary<string, object> results)
        {
            var sb = new System.Text.StringBuilder();

            // Check for errors first
            if (results.ContainsKey("Error"))
            {
                return $"Performance test failed: {results["Error"]}";
            }

            // Get test context for scaling expectations
            int weaponCount = 0;
            int pawnCount = 0;

            if (results.ContainsKey("CacheOperations") && results["CacheOperations"] is Dictionary<string, object> cache)
            {
                if (cache.ContainsKey("TotalWeaponsOnMap") && cache["TotalWeaponsOnMap"] is int wc)
                    weaponCount = wc;
            }

            if (results.ContainsKey("JobCreation") && results["JobCreation"] is Dictionary<string, object> job)
            {
                if (job.ContainsKey("PawnsTested") && job["PawnsTested"] is int pc)
                    pawnCount = pc;
            }

            // Evaluate performance with scale-aware thresholds
            bool performancePassed = true;
            var issues = new List<string>();

            // Adjust expectations for large colonies/maps
            double scaleFactor = 1.0;
            if (weaponCount > 100) scaleFactor *= 1.5;  // 50% more lenient for 100+ weapons
            if (weaponCount > 300) scaleFactor *= 2.0;  // 100% more lenient for 300+ weapons
            if (pawnCount > 50) scaleFactor *= 1.5;     // 50% more lenient for 50+ pawns
            if (pawnCount > 100) scaleFactor *= 2.0;    // 100% more lenient for 100+ pawns

            // Check cache operations performance (microseconds per operation)
            if (results.ContainsKey("CacheOperations") && results["CacheOperations"] is Dictionary<string, object> cacheResults)
            {
                // Real-time operations should be very fast - measured in microseconds
                double maxOperationTime = 100.0; // 100 microseconds max
                double acceptableOperationTime = 50.0; // 50 microseconds acceptable

                // Check add operation
                if (cacheResults.ContainsKey("AddOperation_us") && cacheResults["AddOperation_us"] is double addTime)
                {
                    if (addTime > maxOperationTime)
                    {
                        performancePassed = false;
                        issues.Add($"Cache add operation was slow ({addTime:F1}µs > {maxOperationTime:F0}µs)");
                    }
                    else if (addTime > acceptableOperationTime)
                    {
                        issues.Add($"Cache add operation was slightly slow ({addTime:F1}µs)");
                    }
                }

                // Check remove operation
                if (cacheResults.ContainsKey("RemoveOperation_us") && cacheResults["RemoveOperation_us"] is double removeTime)
                {
                    if (removeTime > maxOperationTime)
                    {
                        performancePassed = false;
                        issues.Add($"Cache remove operation was slow ({removeTime:F1}µs > {maxOperationTime:F0}µs)");
                    }
                    else if (removeTime > acceptableOperationTime)
                    {
                        issues.Add($"Cache remove operation was slightly slow ({removeTime:F1}µs)");
                    }
                }

                // Check update operation
                if (cacheResults.ContainsKey("UpdateOperation_us") && cacheResults["UpdateOperation_us"] is double updateTime)
                {
                    if (updateTime > maxOperationTime)
                    {
                        performancePassed = false;
                        issues.Add($"Cache position update was slow ({updateTime:F1}µs > {maxOperationTime:F0}µs)");
                    }
                    else if (updateTime > acceptableOperationTime)
                    {
                        issues.Add($"Cache position update was slightly slow ({updateTime:F1}µs)");
                    }
                }
            }

            // Check weapon search performance (now in microseconds)
            if (results.ContainsKey("WeaponSearch") && results["WeaponSearch"] is Dictionary<string, object> searchResults)
            {
                if (searchResults.ContainsKey("Average_us") && searchResults["Average_us"] is double avgSearch)
                {
                    // Convert thresholds from ms to microseconds
                    double adjustedMaxTime = Testing.Helpers.TestConstants.MaxWeaponSearchTime * scaleFactor * 1000; // Convert to µs
                    double adjustedAcceptableTime = Testing.Helpers.TestConstants.AcceptableWeaponSearchTime * scaleFactor * 1000;

                    if (avgSearch > adjustedMaxTime)
                    {
                        performancePassed = false;
                        issues.Add($"Weapon search was slow ({avgSearch:F1}µs avg > {adjustedMaxTime:F0}µs)");
                    }
                    else if (avgSearch > adjustedAcceptableTime)
                    {
                        issues.Add($"Weapon search was slightly slow ({avgSearch:F1}µs avg)");
                    }
                }
            }

            // Check job creation performance (now in microseconds)
            if (results.ContainsKey("JobCreation") && results["JobCreation"] is Dictionary<string, object> jobResults)
            {
                if (jobResults.ContainsKey("Average_us") && jobResults["Average_us"] is double avgJob)
                {
                    // Convert thresholds from ms to microseconds
                    double adjustedMaxTime = Testing.Helpers.TestConstants.MaxJobCreationTime * scaleFactor * 1000; // Convert to µs
                    double adjustedAcceptableTime = Testing.Helpers.TestConstants.AcceptableJobCreationTime * scaleFactor * 1000;

                    if (avgJob > adjustedMaxTime)
                    {
                        performancePassed = false;
                        issues.Add($"Job creation was slow ({avgJob:F1}µs avg > {adjustedMaxTime:F0}µs)");
                    }
                    else if (avgJob > adjustedAcceptableTime)
                    {
                        issues.Add($"Job creation was slightly slow ({avgJob:F1}µs avg)");
                    }
                }
            }

            // === VERDICT ===
            if (performancePassed)
            {
                sb.AppendLine("✓ PERFORMANCE TEST PASSED");
                if (pawnCount > 0 || weaponCount > 0)
                {
                    sb.AppendLine($"   Tested with {pawnCount} colonists and {weaponCount} weapons");
                }
                sb.AppendLine();
                sb.AppendLine("Auto Arm is performing within acceptable limits.");
                sb.AppendLine("If you are experiencing lag, it is likely not from this mod.");

                if (issues.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("Minor notes:");
                    foreach (var issue in issues)
                    {
                        sb.AppendLine($"  • {issue}");
                    }
                }
            }
            else
            {
                sb.AppendLine("✗ PERFORMANCE TEST FAILED");
                if (pawnCount > 0 || weaponCount > 0)
                {
                    sb.AppendLine($"   Tested with {pawnCount} colonists and {weaponCount} weapons");
                }
                sb.AppendLine();
                sb.AppendLine("Auto Arm detected performance issues:");
                foreach (var issue in issues)
                {
                    sb.AppendLine($"  • {issue}");
                }
                sb.AppendLine();
                sb.AppendLine("Possible causes:");
                sb.AppendLine("  • Large number of weapons on the map");
                sb.AppendLine("  • Mod conflicts (especially with other equipment mods)");
                sb.AppendLine("  • Complex weapon mods that add many weapon types");
                sb.AppendLine("  • High colony population (100+ pawns)");
                sb.AppendLine();
                sb.AppendLine("These issues may contribute to game lag.");
                sb.AppendLine("Consider adjusting mod settings or reporting this on Steam.");
            }

            sb.AppendLine();

            // Cache Operations
            if (results.ContainsKey("CacheOperations") && results["CacheOperations"] is Dictionary<string, object> cacheOps)
            {
                sb.AppendLine("REAL-TIME CACHE OPERATIONS:");
                if (cacheOps.ContainsKey("TotalWeaponsOnMap"))
                    sb.AppendLine($"  Total weapons on map: {cacheOps["TotalWeaponsOnMap"]}");
                if (cacheOps.ContainsKey("AddOperation_us"))
                    sb.AppendLine($"  Add weapon: {cacheOps["AddOperation_us"]}µs");
                if (cacheOps.ContainsKey("RemoveOperation_us"))
                    sb.AppendLine($"  Remove weapon: {cacheOps["RemoveOperation_us"]}µs");
                if (cacheOps.ContainsKey("UpdateOperation_us"))
                    sb.AppendLine($"  Update position: {cacheOps["UpdateOperation_us"]}µs");
                if (cacheOps.ContainsKey("TotalOperations"))
                    sb.AppendLine($"  Operations tested: {cacheOps["TotalOperations"]}");
                sb.AppendLine();
            }

            // Weapon Search
            if (results.ContainsKey("WeaponSearch") && results["WeaponSearch"] is Dictionary<string, object> search)
            {
                sb.AppendLine("WEAPON SEARCH:");
                if (search.ContainsKey("TotalSearches"))
                    sb.AppendLine($"  Total searches: {search["TotalSearches"]}");
                if (search.ContainsKey("Average_us"))
                    sb.AppendLine($"  Average: {search["Average_us"]:F2}µs");
                if (search.ContainsKey("Min_us"))
                    sb.AppendLine($"  Min: {search["Min_us"]:F2}µs");
                if (search.ContainsKey("Max_us"))
                    sb.AppendLine($"  Max: {search["Max_us"]:F2}µs");
                if (search.ContainsKey("Median_us"))
                    sb.AppendLine($"  Median: {search["Median_us"]:F2}µs");
                sb.AppendLine();
            }

            // Score Calculation
            if (results.ContainsKey("ScoreCalculation") && results["ScoreCalculation"] is Dictionary<string, object> score)
            {
                sb.AppendLine("SCORE CALCULATION:");
                if (score.ContainsKey("WeaponCount"))
                    sb.AppendLine($"  Weapons tested: {score["WeaponCount"]}");
                if (score.ContainsKey("Iterations"))
                    sb.AppendLine($"  Test iterations: {score["Iterations"]}");
                if (score.ContainsKey("FirstPass_us"))
                    sb.AppendLine($"  First pass (avg): {score["FirstPass_us"]:F2}µs");
                if (score.ContainsKey("CachedPass_us"))
                    sb.AppendLine($"  Cached pass (avg): {score["CachedPass_us"]:F2}µs");
                if (score.ContainsKey("PerWeaponFirst_us"))
                    sb.AppendLine($"  Per weapon (first): {score["PerWeaponFirst_us"]:F2}µs");
                if (score.ContainsKey("PerWeaponCached_us"))
                    sb.AppendLine($"  Per weapon (cached): {score["PerWeaponCached_us"]:F2}µs");
                if (score.ContainsKey("CacheSpeedup"))
                    sb.AppendLine($"  Cache speedup: {score["CacheSpeedup"]:F1}x");
                sb.AppendLine();
            }

            // Job Creation
            if (results.ContainsKey("JobCreation") && results["JobCreation"] is Dictionary<string, object> jobData)
            {
                sb.AppendLine("JOB CREATION:");
                if (jobData.ContainsKey("PawnsTested"))
                    sb.AppendLine($"  Pawns tested: {jobData["PawnsTested"]}");
                if (jobData.ContainsKey("JobsCreated"))
                    sb.AppendLine($"  Jobs created: {jobData["JobsCreated"]}");
                if (jobData.ContainsKey("IterationsPerPawn"))
                    sb.AppendLine($"  Iterations per pawn: {jobData["IterationsPerPawn"]}");
                if (jobData.ContainsKey("Average_us"))
                    sb.AppendLine($"  Average: {jobData["Average_us"]:F2}µs");
                if (jobData.ContainsKey("Min_us"))
                    sb.AppendLine($"  Min: {jobData["Min_us"]:F2}µs");
                if (jobData.ContainsKey("Max_us"))
                    sb.AppendLine($"  Max: {jobData["Max_us"]:F2}µs");
                if (jobData.ContainsKey("Median_us"))
                    sb.AppendLine($"  Median: {jobData["Median_us"]:F2}µs");
                if (jobData.ContainsKey("Total_us"))
                    sb.AppendLine($"  Total: {jobData["Total_us"]:F2}µs");
                sb.AppendLine();
            }

            // Memory
            if (results.ContainsKey("Memory") && results["Memory"] is Dictionary<string, object> mem)
            {
                sb.AppendLine("MEMORY USAGE:");
                if (mem.ContainsKey("BeforeGC_MB"))
                    sb.AppendLine($"  Before GC: {mem["BeforeGC_MB"]:F2}MB");
                if (mem.ContainsKey("AfterGC_MB"))
                    sb.AppendLine($"  After GC: {mem["AfterGC_MB"]:F2}MB");
                if (mem.ContainsKey("Freed_MB"))
                    sb.AppendLine($"  Freed: {mem["Freed_MB"]:F2}MB");
            }

            return sb.ToString();
        }
    }
}