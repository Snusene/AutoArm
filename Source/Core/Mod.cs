
using AutoArm.Caching;
using AutoArm.Compatibility;
using AutoArm.Definitions;
using AutoArm.Helpers;
using AutoArm.Jobs;
using AutoArm.Logging;
using AutoArm.Testing;
using AutoArm.UI;
using AutoArm.Weapons;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace AutoArm
{
    public class AutoArmMod : Mod, IDisposable
    {
        public static AutoArmMod Instance { get; private set; }
        public static string Version => "1.0.0";
        private bool disposed = false;

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

        static AutoArmMod()
        {
            if (settings == null)
            {
                settings = new AutoArmSettings();
                settings.modEnabled = true;
                AutoArmLogger.Debug(() => "[AutoArmMod] Static constructor - created default enabled settings");
            }
        }

        public AutoArmMod(ModContentPack content) : base(content)
        {
            Instance = this;
            if (!TestRunner.IsRunningTests)
            {
                settings = GetSettings<AutoArmSettings>();
            }
            else
            {
                if (settings == null)
                {
                    settings = new AutoArmSettings();
                    settings.modEnabled = true;
                    AutoArmLogger.Debug(() => "[AutoArmMod] Constructor during test - created new enabled settings");
                }
                else
                {
                    bool wasEnabled = settings.modEnabled;
                    settings.modEnabled = true;
                    if (!wasEnabled)
                    {
                        AutoArmLogger.Debug(() => "[AutoArmMod] Constructor during test - force enabled mod");
                    }
                }
                AutoArmLogger.Debug(() => $"[AutoArmMod] Constructor during test - modEnabled: {settings?.modEnabled}");
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

            var contentRect = listing.GetRect(inRect.height - listing.CurHeight - Constants.UI_LINE_HEIGHT);
            DrawTabContent(contentRect);

            listing.End();
        }

        private void DrawHeader(Listing_Standard listing, Rect inRect)
        {
            DrawResetButton(inRect);

            var titleRect = listing.GetRect(Text.LineHeight * 1.5f);

            Text.Font = GameFont.Medium;
            var settingsLabelRect = new Rect(titleRect.x, titleRect.y, 100f, titleRect.height);
            Widgets.Label(settingsLabelRect, "AutoArm_Settings".Translate());

            Text.Font = GameFont.Tiny;
            Color oldColor = GUI.color;
            GUI.color = new Color(Constants.UI_GRAY_ALPHA, Constants.UI_GRAY_ALPHA, Constants.UI_GRAY_ALPHA, Constants.UI_TEXT_ALPHA);
            var hintRect = new Rect(settingsLabelRect.xMax + 10f, titleRect.y + 4f, 200f, titleRect.height);
            Widgets.Label(hintRect, "(hover over options for descriptions)");
            GUI.color = oldColor;

            Text.Font = GameFont.Small;
            listing.Gap(Constants.UI_SMALL_GAP);
        }

        private void DrawResetButton(Rect inRect)
        {
            Rect resetButtonRect = new Rect(
                inRect.width - Constants.UI_RESET_BUTTON_WIDTH - 5f,
                5f,
                Constants.UI_RESET_BUTTON_WIDTH,
                Constants.UI_RESET_BUTTON_HEIGHT
            );

            Color oldColor = GUI.color;
            GUI.color = new Color(0.65f, 0.65f, 0.65f);

            if (Widgets.ButtonText(resetButtonRect, "AutoArm_ResetConfig".Translate()))
            {
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    "AutoArm_ConfirmReset".Translate(),
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
            var tabRect = listing.GetRect(Constants.UI_TAB_BUTTON_HEIGHT);
            var tabWidth = tabRect.width / 4f - 5f;

            Color originalColor = GUI.color;

            GUI.color = currentTab == SettingsTab.General ? Color.white : new Color(0.7f, 1f, 0.7f);
            if (Widgets.ButtonText(new Rect(tabRect.x, tabRect.y, tabWidth, Constants.UI_TAB_BUTTON_HEIGHT), "AutoArm_General".Translate()))
                currentTab = SettingsTab.General;

            GUI.color = currentTab == SettingsTab.Compatibility ? Color.white : new Color(1f, 1f, 0.7f);
            if (Widgets.ButtonText(new Rect(tabRect.x + tabWidth + 5f, tabRect.y, tabWidth, Constants.UI_TAB_BUTTON_HEIGHT), "AutoArm_Compatibility".Translate()))
                currentTab = SettingsTab.Compatibility;

            GUI.color = currentTab == SettingsTab.Advanced ? Color.white : new Color(1f, 0.9f, 0.6f);
            if (Widgets.ButtonText(new Rect(tabRect.x + (tabWidth + 5f) * 2, tabRect.y, tabWidth, Constants.UI_TAB_BUTTON_HEIGHT), "AutoArm_Advanced".Translate()))
                currentTab = SettingsTab.Advanced;

            GUI.color = currentTab == SettingsTab.Debug ? Color.white : new Color(1f, 0.7f, 0.7f);
            if (Widgets.ButtonText(new Rect(tabRect.x + (tabWidth + 5f) * 3, tabRect.y, tabWidth, Constants.UI_TAB_BUTTON_HEIGHT), "AutoArm_Debug".Translate()))
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
            listing.Gap(Constants.UI_SMALL_GAP);
        }

        private void DrawTabContent(Rect contentRect)
        {
            var innerRect = contentRect.ContractedBy(Constants.UI_CONTENT_PADDING);

            Widgets.DrawBoxSolid(contentRect, new Color(0.1f, 0.1f, 0.1f, Constants.UI_BOX_ALPHA));

            var innerListing = new Listing_Standard();
            innerListing.Begin(innerRect);

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

            GUI.color = oldColor;
            GUI.enabled = wasEnabled;

            innerListing.End();
        }

        private void DrawCheckbox(Listing_Standard listing, string label, ref bool value, string tooltip = null, float indent = 0f, bool isSubOption = false)
        {
            Rect fullRect = listing.GetRect(Constants.UI_LINE_HEIGHT);

            float checkboxSize = isSubOption ? Constants.UI_CHECKBOX_SIZE * 0.8f : Constants.UI_CHECKBOX_SIZE;

            Rect checkRect = new Rect(
                fullRect.x + indent,
                fullRect.y + (Constants.UI_LINE_HEIGHT - checkboxSize) / 2f,
                checkboxSize,
                checkboxSize
            );

            float labelHeight = Text.LineHeight;
            float labelY = fullRect.y + (Constants.UI_LINE_HEIGHT - labelHeight) / 2f;

            Rect labelRect = new Rect(
                fullRect.x + checkboxSize + 5f + indent,
                labelY,
                fullRect.width - checkboxSize - 5f - indent,
                labelHeight
            );

            bool oldValue = value;

            if (GUI.enabled)
            {
                Widgets.Checkbox(checkRect.x, checkRect.y, ref value, checkboxSize);
            }
            else
            {
                bool tempValue = value;
                Widgets.Checkbox(checkRect.x, checkRect.y, ref tempValue, checkboxSize);
            }

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

        private void DrawSlider(Listing_Standard listing, string label, ref float value, float min, float max, string format = "P0", string tooltip = null, bool isPercentageBetter = false, bool isWeaponPreferenceMode = false)
        {
            Rect labelRect = listing.GetRect(Text.LineHeight);
            Widgets.Label(labelRect, label);

            Rect rect = listing.GetRect(Constants.UI_LINE_HEIGHT);

            float sliderWidth = rect.width / 3f;
            Rect sliderRect = new Rect(rect.x, rect.y, sliderWidth, rect.height);

            float valueWidth = 220f;
            Rect valueRect = new Rect(sliderRect.xMax + 10f, rect.y, valueWidth, rect.height);

            float sliderHeight = 20f;
            Rect actualSliderRect = new Rect(sliderRect.x, sliderRect.y + (rect.height - sliderHeight) / 2f, sliderRect.width, sliderHeight);

            float oldValue = value;

            if (GUI.enabled)
            {
                value = Widgets.HorizontalSlider(actualSliderRect, value, min, max);
            }
            else
            {
                float tempValue = value;
                Widgets.HorizontalSlider(actualSliderRect, tempValue, min, max);
            }

            bool isWeaponPreference = isWeaponPreferenceMode;

            if (isWeaponPreference)
            {
                Color oldColor = GUI.color;
                GUI.color = new Color(0.5f, 0.5f, 0.5f, 0.5f);
                float centerX = actualSliderRect.x + actualSliderRect.width / 2f;
                Widgets.DrawLineVertical(centerX, actualSliderRect.y, actualSliderRect.height);
                GUI.color = oldColor;
            }

            string displayValue;
            Color valueColor = GUI.color;

            if (isWeaponPreference)
            {
                if (value <= Constants.PREF_STRONG_MELEE_THRESHOLD)
                    displayValue = "AutoArm_StrongMeleePreference".Translate();
                else if (value <= Constants.PREF_MODERATE_MELEE_THRESHOLD)
                    displayValue = "AutoArm_ModerateMeleePreference".Translate();
                else if (value <= Constants.PREF_SLIGHT_MELEE_THRESHOLD)
                    displayValue = "AutoArm_SlightMeleePreference".Translate();
                else if (value < Constants.PREF_BALANCED_THRESHOLD)
                    displayValue = "AutoArm_Balanced".Translate();
                else if (value < Constants.PREF_SLIGHT_RANGED_THRESHOLD)
                    displayValue = "AutoArm_SlightRangedPreference".Translate();
                else if (value < Constants.PREF_MODERATE_RANGED_THRESHOLD)
                    displayValue = "AutoArm_ModerateRangedPreference".Translate();
                else
                    displayValue = "AutoArm_StrongRangedPreference".Translate();

                if (Math.Abs(value) < 0.10f)
                    valueColor = new Color(0.8f, 0.8f, 0.8f);
                else if (value < 0)
                    valueColor = new Color(0.8f, 0.8f, 1f);
                else
                    valueColor = new Color(1f, 0.8f, 0.8f);
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
                "AutoArm_EnableModDesc".Translate());

            if (oldModEnabled != settings.modEnabled)
            {
                AutoArmLogger.Info($"{(settings.modEnabled ? "Turning on..." : "Turning off...")}");
            }

            if (!oldModEnabled && settings.modEnabled)
            {
                if (Testing.TestRunner.IsRunningTests)
                {
                    if (settings.debugLogging)
                    {
                        AutoArmLogger.Debug(() => "[TEST] Skipping cache clear on mod re-enable during tests");
                    }
                    return;
                }

                if (settings.debugLogging)
                {
                    AutoArmLogger.Debug(() => "[SETTINGS] Mod was just re-enabled, clearing caches...");
                }

                if (Current.Game != null)
                {

                    PawnValidationCache.ClearCache();

                    DroppedItemTracker.ClearAll();

                    if (SimpleSidearmsCompat.IsLoaded)
                    {
                    }

                    Cleanup.ClearAllCaches();

                    foreach (var map in Find.Maps)
                    {
                        WeaponCacheManager.RebuildCache(map);
                    }

                    if (settings.debugLogging)
                    {
                        AutoArmLogger.Debug(() => "Mod re-enabled - cleared all caches and cooldowns, rebuilding weapon cache");
                    }
                }
                else
                {
                    Cleanup.ClearAllCaches();
                    if (settings.debugLogging)
                    {
                        AutoArmLogger.Debug(() => "Mod re-enabled in main menu - cleared settings cache only");
                    }
                }
            }

            listing.Gap(Constants.UI_SMALL_GAP);

            Color oldColor = GUI.color;
            bool wasEnabled = GUI.enabled;
            if (!settings.modEnabled)
            {
                GUI.color = Color.gray;
                GUI.enabled = false;
            }

            DrawCheckbox(listing, "AutoArm_ShowNotifications".Translate(), ref settings.showNotifications,
                "AutoArm_ShowNotificationsDesc".Translate());

            listing.Gap(Constants.UI_SMALL_GAP);

            DrawCheckbox(listing, "AutoArm_DisableDuringRaids".Translate(), ref settings.disableDuringRaids,
                "AutoArm_DisableDuringRaidsDesc".Translate());

            listing.Gap(Constants.UI_SMALL_GAP);

            DrawCheckbox(listing, "AutoArm_OnlyEquipFromStorage".Translate(), ref settings.onlyAutoEquipFromStorage,
                "AutoArm_OnlyEquipFromStorageDesc".Translate());

            if (ModsConfig.RoyaltyActive)
            {
                listing.Gap(Constants.UI_SMALL_GAP);

                bool oldRespectBonds = settings.respectWeaponBonds;
                DrawCheckbox(listing, "AutoArm_ForceWeapon".Translate(), ref settings.respectWeaponBonds,
                    "AutoArm_ForceWeaponDesc".Translate());

                if (!oldRespectBonds && settings.respectWeaponBonds && Current.Game != null)
                {
                    MarkBondedAsForced();
                }
            }

            listing.Gap(Constants.UI_SMALL_GAP);

            DrawCheckbox(listing, "AutoArm_AllowForcedWeaponUpgrades".Translate(), ref settings.allowForcedWeaponUpgrades,
                "AutoArm_AllowForcedWeaponUpgradesDesc".Translate());

            listing.Gap(Constants.UI_SMALL_GAP);

            DrawCheckbox(listing, "AutoArm_AllowTemporaryColonists".Translate(), ref settings.allowTemporaryColonists,
                "AutoArm_AllowTemporaryColonistsDesc".Translate());

            GUI.color = oldColor;
            GUI.enabled = wasEnabled;

            listing.Gap(Constants.UI_SECTION_GAP);
        }

        private void DrawCompatibilityTab(Listing_Standard listing)
        {
            Text.Font = GameFont.Medium;
            var headerRect = listing.GetRect(Text.LineHeight);
            Widgets.Label(headerRect, "AutoArm_CompatibilityPatches".Translate());
            Text.Font = GameFont.Small;
            listing.Gap(Constants.UI_SECTION_GAP);

            DrawSimpleSidearmsSettings(listing);
            DrawCombatExtendedSettings(listing);
            DrawPocketSandSettings(listing);
        }

        private void DrawSimpleSidearmsSettings(Listing_Standard listing)
        {
            Text.Font = GameFont.Small;
            var nameRect = listing.GetRect(Text.LineHeight);

            Color oldColor = GUI.color;
            string statusText = SimpleSidearmsCompat.IsLoaded ? "Active" : "Not found";
            Color statusColor = SimpleSidearmsCompat.IsLoaded ? new Color(0.7f, 1f, 0.7f) : Color.gray;

            if (!SimpleSidearmsCompat.IsLoaded)
            {
                GUI.color = Color.gray;
            }

            string fullText = $"Simple Sidearms: ";
            Widgets.Label(new Rect(nameRect.x, nameRect.y, 200f, nameRect.height), fullText);

            float statusX = nameRect.x + Text.CalcSize(fullText).x;
            GUI.color = statusColor;
            Widgets.Label(new Rect(statusX, nameRect.y, 400f, nameRect.height), statusText);
            GUI.color = oldColor;

            if (!SimpleSidearmsCompat.IsLoaded)
            {
                listing.Gap(Constants.UI_SMALL_GAP);
                return;
            }

            bool reflectionFailed = SimpleSidearmsCompat.ReflectionFailed;

            if (reflectionFailed)
            {
                listing.Gap(Constants.UI_TINY_GAP);
                Color warningColor = GUI.color;
                GUI.color = new Color(1f, 0.6f, 0.6f);
                var warningRect = listing.GetRect(Text.LineHeight * 2);
                Widgets.Label(warningRect, "AutoArm_SimpleSidearmsReflectionFailed".Translate());
                GUI.color = warningColor;
                listing.Gap(Constants.UI_TINY_GAP);
            }

            bool wasEnabled = GUI.enabled;
            if (reflectionFailed)
            {
                GUI.enabled = false;
            }

            bool tempAutoEquipSidearms = settings.autoEquipSidearms;
            DrawCheckbox(listing, "AutoArm_EnableSidearmAutoEquip".Translate(), ref tempAutoEquipSidearms,
                "AutoArm_EnableSidearmAutoEquipDesc".Translate(), 30f);
            settings.autoEquipSidearms = tempAutoEquipSidearms;

            if (SimpleSidearmsCompat.CanAutoEquipSidearms())
            {
                bool tempAllowSidearmUpgrades = settings.allowSidearmUpgrades;

                string upgradeLabel = "AutoArm_AllowSidearmUpgrades".Translate() + " (experimental)";
                DrawCheckbox(listing, upgradeLabel, ref tempAllowSidearmUpgrades,
                    "AutoArm_AllowSidearmUpgradesDesc".Translate(), 30f);

                settings.allowSidearmUpgrades = tempAllowSidearmUpgrades;
            }

            GUI.enabled = wasEnabled;

            listing.Gap(Constants.UI_SMALL_GAP);
        }

        private void DrawCombatExtendedSettings(Listing_Standard listing)
        {
            Text.Font = GameFont.Small;
            var nameRect = listing.GetRect(Text.LineHeight);

            Color oldColor = GUI.color;
            bool isLoaded = CECompat.IsLoaded();
            string statusText = isLoaded ? "Active" : "Not found";
            Color statusColor = isLoaded ? new Color(0.7f, 1f, 0.7f) : Color.gray;

            if (!isLoaded)
            {
                GUI.color = Color.gray;
            }

            string fullText = $"Combat Extended: ";
            Widgets.Label(new Rect(nameRect.x, nameRect.y, 200f, nameRect.height), fullText);

            float statusX = nameRect.x + Text.CalcSize(fullText).x;
            GUI.color = statusColor;
            Widgets.Label(new Rect(statusX, nameRect.y, 400f, nameRect.height), statusText);
            GUI.color = oldColor;

            if (!isLoaded)
            {
                listing.Gap(Constants.UI_SMALL_GAP);
                return;
            }

            bool ceAmmoSystemEnabled = CECompat.TryDetectAmmoSystemEnabled(out string detectionResult);

            bool stateChanged = ceAmmoSystemEnabled != settings.lastKnownCEAmmoState;

            if (!ceAmmoSystemEnabled && settings.checkCEAmmo)
            {
                settings.checkCEAmmo = false;
                if (settings.debugLogging)
                {
                    AutoArmLogger.Debug(() => "CE ammo system is disabled - forcing ammo checks off");
                }
            }
            else if (ceAmmoSystemEnabled && !settings.lastKnownCEAmmoState && stateChanged)
            {
                settings.checkCEAmmo = true;
                AutoArmLogger.Debug(() => "Combat Extended ammo system detected - enabling ammo checks");
            }

            settings.lastKnownCEAmmoState = ceAmmoSystemEnabled;

            if (!ceAmmoSystemEnabled)
            {
                GUI.color = Color.gray;
            }

            DrawCheckbox(listing, "AutoArm_RequireAmmunition".Translate(), ref settings.checkCEAmmo,
                "AutoArm_RequireAmmunitionDesc".Translate(), 30f);

            if (!ceAmmoSystemEnabled && settings.checkCEAmmo)
            {
                settings.checkCEAmmo = false;
            }

            GUI.color = oldColor;

            if (!ceAmmoSystemEnabled)
            {
                listing.Gap(Constants.UI_TINY_GAP);
                Color warningColor = GUI.color;
                GUI.color = new Color(1f, 0.8f, 0.4f);
                var warningRect = listing.GetRect(Text.LineHeight);
                Widgets.Label(warningRect, "AutoArm_CEAmmoSystemDisabled".Translate());
                GUI.color = warningColor;
            }

            listing.Gap(Constants.UI_SMALL_GAP);
        }

        private void DrawPocketSandSettings(Listing_Standard listing)
        {
            Text.Font = GameFont.Small;
            var nameRect = listing.GetRect(Text.LineHeight);

            Color oldColor = GUI.color;
            string statusText = PocketSandCompat.Active ? "Active" : "Not found";
            Color statusColor = PocketSandCompat.Active ? new Color(0.7f, 1f, 0.7f) : Color.gray;

            if (!PocketSandCompat.Active)
            {
                GUI.color = Color.gray;
            }

            string fullText = $"Pocket Sand: ";
            Widgets.Label(new Rect(nameRect.x, nameRect.y, 200f, nameRect.height), fullText);

            float statusX = nameRect.x + Text.CalcSize(fullText).x;
            GUI.color = statusColor;
            Widgets.Label(new Rect(statusX, nameRect.y, 400f, nameRect.height), statusText);
            GUI.color = oldColor;

            listing.Gap(Constants.UI_SMALL_GAP);
        }

        private void DrawAdvancedTab(Listing_Standard listing)
        {
            Text.Font = GameFont.Medium;
            var headerRect = listing.GetRect(Text.LineHeight);
            Widgets.Label(headerRect, "AutoArm_WeaponUpgrades".Translate());
            Text.Font = GameFont.Small;
            listing.Gap(Constants.UI_SMALL_GAP);

            DrawSlider(listing, "AutoArm_Threshold".Translate(), ref settings.weaponUpgradeThreshold,
                Constants.WeaponUpgradeThresholdMin, Constants.WeaponUpgradeThresholdMax, "F2",
                "AutoArm_ThresholdDesc".Translate(),
                true);

            listing.Gap(Constants.UI_SMALL_GAP);

            DrawWeaponPreferenceSlider(listing);

            listing.Gap(Constants.UI_SECTION_GAP);

            DrawAgeRestrictions(listing);
        }

        private void DrawWeaponPreferenceSlider(Listing_Standard listing)
        {
            DrawSlider(listing, "AutoArm_WeaponPreference".Translate(), ref settings.weaponTypePreference,
                -1f, 1f, "custom",
                "AutoArm_WeaponPreferenceDesc".Translate(),
                false, true);
        }

        public static float GetRangedMultiplier()
        {
            float pref = AutoArmMod.settings?.weaponTypePreference ?? Constants.DefaultWeaponTypePreference;
            return Constants.WeaponPreferenceRangedBase + (pref * Constants.WeaponPreferenceAdjustment);
        }

        public static float GetMeleeMultiplier()
        {
            float pref = AutoArmMod.settings?.weaponTypePreference ?? Constants.DefaultWeaponTypePreference;
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
                "AutoArm_AllowChildrenToEquipWeaponsDesc".Translate());

            if (settings.allowChildrenToEquipWeapons)
            {
                float tempAge = (float)settings.childrenMinAge;
                DrawSlider(listing, "AutoArm_MinimumAge".Translate(), ref tempAge,
                    (float)Constants.ChildMinAgeLimit, (float)Constants.ChildMaxAgeLimit, "F0",
                    "AutoArm_MinimumAgeDesc".Translate(),
                    false);
                settings.childrenMinAge = Mathf.RoundToInt(tempAge);

                if (settings.childrenMinAge <= 3)
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

            AutoArmPerfOverlayWindow.OpenOrBringToFront();
        }

        public override void WriteSettings()
        {
            base.WriteSettings();

        }

        public override string SettingsCategory()
        {
            return "AutoArm_SettingsCategory".Translate();
        }

        private static void MarkBondedAsForced()
        {
            if (Current.Game?.Maps == null)
                return;

            int count = 0;
            foreach (var map in Find.Maps)
            {
                if (map?.mapPawns?.FreeColonistsSpawned == null)
                    continue;

                foreach (var pawn in map.mapPawns.FreeColonistsSpawned)
                {
                    if (!pawn.IsColonist)
                        continue;

                    if (pawn.equipment?.Primary != null &&
                        ValidationHelper.IsWeaponBondedToPawn(pawn.equipment.Primary, pawn))
                    {
                        ForcedWeapons.SetForced(pawn, pawn.equipment.Primary);
                        count++;
                        if (settings?.debugLogging == true)
                        {
                            AutoArmLogger.LogWeapon(pawn, pawn.equipment.Primary, "Bonded weapon marked as forced (setting enabled)");
                        }
                    }

                    if (pawn.inventory?.innerContainer != null)
                    {
                        foreach (var item in pawn.inventory.innerContainer)
                        {
                            if (item is ThingWithComps weapon &&
                                weapon.def.IsWeapon &&
                                ValidationHelper.IsWeaponBondedToPawn(weapon, pawn))
                            {
                                ForcedWeapons.AddSidearm(pawn, weapon);
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

        }

        public static void MarkAllBondedWeaponsAsForcedOnLoad()
        {
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                MarkBondedAsForced();
            });
        }


        /// <summary>
        /// Cleanup resources
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    try
                    {

                        CleanupAllStaticCaches();

                        Instance = null;

                        AutoArmLogger.Debug(() => "AutoArmMod disposed successfully");

                        AutoArmLogger.Flush();
                        AutoArmLogger.Shutdown();
                    }
                    catch (Exception ex)
                    {
                        AutoArmLogger.ErrorCleanup(ex, "disposal");
                        try { AutoArmLogger.Shutdown(); } catch { }
                    }
                }

                disposed = true;
            }
        }


        private static void CleanupAllStaticCaches()
        {
            try
            {
                WeaponCacheManager.ClearAllCaches();
                WeaponScoringHelper.ClearWeaponScoreCache();

                PawnValidationCache.ClearCache();

                GenericCache.ClearAll();

                DroppedItemTracker.ClearAll();
                ForcedWeaponState.Clear();
                AutoEquipState.Cleanup();
                WeaponBlacklist.ClearAll();

                AutoArmLogger.Debug(() => "Cleared all static caches");
            }
            catch (Exception ex)
            {
                AutoArmLogger.ErrorCleanup(ex, "cache cleanup");
            }
        }

        ~AutoArmMod()
        {
            Dispose(false);
        }
    }

    public class AutoArmLoggerWindow : Window
    {
        public AutoArmLoggerWindow()
        {
            doCloseX = true;
            closeOnCancel = false;
            closeOnClickedOutside = false;
            absorbInputAroundWindow = false;
            draggable = true;
            resizeable = true;
            preventCameraMotion = false;

            StatusOverviewRenderer.OnWindowOpened();
        }

        public override Vector2 InitialSize => new Vector2(Constants.DEBUG_WINDOW_WIDTH, Constants.DEBUG_WINDOW_HEIGHT);

        protected override void SetInitialSizeAndPosition()
        {
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
            var listing = new Listing_Standard();
            listing.Begin(inRect);

            Text.Font = GameFont.Medium;
            var headerRect = listing.GetRect(Text.LineHeight);
            var titleLabelRect = new Rect(headerRect.x, headerRect.y, headerRect.width - 200f, headerRect.height);
            Widgets.Label(titleLabelRect, "AutoArm_DebugTools".Translate());

            Text.Font = GameFont.Small;
            var checkboxRect = new Rect(headerRect.xMax - 170f, headerRect.y + 3f, 24f, 24f);
            var checkboxLabelRect = new Rect(checkboxRect.xMax + 5f, headerRect.y + 3f, 140f, headerRect.height);
            bool oldDebugLogging = AutoArmMod.settings.debugLogging;
            Widgets.Checkbox(checkboxRect.x, checkboxRect.y, ref AutoArmMod.settings.debugLogging, 24f);
            Widgets.Label(checkboxLabelRect, "AutoArm_EnableDebugLogging".Translate());
            if (oldDebugLogging != AutoArmMod.settings.debugLogging)
            {
                AutoArmLogger.Info($"Debug logging {(AutoArmMod.settings.debugLogging ? "enabled" : "disabled")}");

                if (AutoArmMod.settings.debugLogging)
                {
                    AutoArmLogger.AnnounceVerboseLogging();
                }
            }
            Text.Font = GameFont.Small;

            listing.Gap(10f);

            Color oldColor = GUI.color;
            GUI.color = new Color(1f, 0.6f, 0.6f);
            var warningRect = listing.GetRect(Text.LineHeight);
            Widgets.Label(warningRect, "\u26a0 " + "AutoArm_SaveBeforeTouching".Translate());
            GUI.color = oldColor;

            listing.Gap(10f);

            var resultsRect = new Rect(0f, listing.CurHeight, inRect.width, inRect.height - listing.CurHeight);
            StatusOverviewRenderer.DrawStatusOverview(resultsRect);

            listing.End();
        }

        public override void PostClose()
        {
            base.PostClose();

            StatusOverviewRenderer.ResetState();
        }

    }
}
