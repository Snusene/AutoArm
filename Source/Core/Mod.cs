
using AutoArm.Caching;
using AutoArm.Compatibility;
using AutoArm.Definitions;
using AutoArm.Helpers;
using AutoArm.Jobs;
using AutoArm.Testing;
using AutoArm.UI;
using RimWorld;
using System;
using UnityEngine;
using Verse;

namespace AutoArm
{
    [StaticConstructorOnStartup]
    internal static class AutoArmTextures
    {
        public static readonly Texture2D Discord = ContentFinder<Texture2D>.Get("AutoArm/UI/discord", reportFailure: false);
    }

    public sealed class AutoArmMod : Mod
    {
        private const string AutoArmDiscordUrl = "https://discord.gg/xp2f3YFKMY";

        public static AutoArmSettings settings;
        private SettingsTab currentTab = SettingsTab.General;

        private DebugTools debugWindow;

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
                AutoArmLogger.Debug(() => "[AutoArm] Static constructor - created default enabled settings");
            }
        }

        public AutoArmMod(ModContentPack content) : base(content)
        {
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
                    AutoArmLogger.Debug(() => "[AutoArm] Constructor during test - created new enabled settings");
                }
                else
                {
                    bool wasEnabled = settings.modEnabled;
                    settings.modEnabled = true;
                    if (!wasEnabled)
                    {
                        AutoArmLogger.Debug(() => "[AutoArm] Constructor during test - force enabled mod");
                    }
                }
                AutoArmLogger.Debug(() => $"[AutoArm] Constructor during test - modEnabled: {settings?.modEnabled}");
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

            DrawDiscordLink(new Rect(settingsLabelRect.xMax + 10f, titleRect.y + 2f, 140f, 24f));

            Text.Font = GameFont.Tiny;
            var hintColor = new Color(Constants.UI_GRAY_ALPHA, Constants.UI_GRAY_ALPHA, Constants.UI_GRAY_ALPHA, Constants.UI_TEXT_ALPHA);
            var hintRect = new Rect(settingsLabelRect.xMax + 160f, titleRect.y + 4f, 200f, titleRect.height);
            using (new TextBlock(hintColor))
                Widgets.Label(hintRect, "AutoArm_HoverHint".Translate());

            Text.Font = GameFont.Small;
            listing.Gap(Constants.UI_SMALL_GAP);
        }

        private void DrawDiscordLink(Rect rect)
        {
            Text.Font = GameFont.Small;

            const string label = "Snues's Server";
            Rect textRect = rect;

            if (AutoArmTextures.Discord != null)
            {
                Widgets.DrawTextureFitted(rect.LeftPartPixels(24f), AutoArmTextures.Discord, 1f);
                textRect = new Rect(rect.x + 28f, rect.y, rect.width - 28f, rect.height);
            }

            using (new TextBlock(Mouse.IsOver(textRect) ? UIColors.LinkHover : UIColors.LinkIdle))
                Widgets.Label(textRect, label);

            if (Widgets.ButtonInvisible(rect))
                Application.OpenURL(AutoArmDiscordUrl);

            if (Mouse.IsOver(rect))
            {
                Widgets.DrawHighlight(rect);
                TooltipHandler.TipRegion(rect, AutoArmDiscordUrl);
            }
        }

        private void DrawResetButton(Rect inRect)
        {
            Rect resetButtonRect = new Rect(
                inRect.width - Constants.UI_RESET_BUTTON_WIDTH - 5f,
                5f,
                Constants.UI_RESET_BUTTON_WIDTH,
                Constants.UI_RESET_BUTTON_HEIGHT
            );

            using (new TextBlock(UIColors.Dim))
            {
                if (Widgets.ButtonText(resetButtonRect, "AutoArm_ResetConfig".Translate()))
                {
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                        "AutoArm_ConfirmReset".Translate(),
                        () =>
                        {
                            settings.ResetToDefaults();
                            WeaponCache.ClearScoreCache();
                            Messages.Message("AutoArm_SettingsReset".Translate(), MessageTypeDefOf.NeutralEvent, false);
                        }));
                }
            }
        }

        private void DrawTabButtons(Listing_Standard listing)
        {
            var tabRect = listing.GetRect(Constants.UI_TAB_BUTTON_HEIGHT);
            var tabWidth = tabRect.width / 4f - 5f;

            DrawTabButton(tabRect, 0, tabWidth, "AutoArm_General", SettingsTab.General, () => currentTab = SettingsTab.General);
            DrawTabButton(tabRect, 1, tabWidth, "AutoArm_Compatibility", SettingsTab.Compatibility, () => currentTab = SettingsTab.Compatibility);
            DrawTabButton(tabRect, 2, tabWidth, "AutoArm_Advanced", SettingsTab.Advanced, () => currentTab = SettingsTab.Advanced);
            DrawTabButton(tabRect, 3, tabWidth, "AutoArm_Debug", SettingsTab.Debug, () =>
            {
                if (Current.Game?.CurrentMap != null)
                    OpenDebugWindow();
                else
                    Messages.Message("AutoArm_DebugRequiresActiveGame".Translate(), MessageTypeDefOf.RejectInput, false);
            });

            listing.Gap(Constants.UI_SMALL_GAP);
        }

        private void DrawTabButton(Rect tabRect, int index, float tabWidth, string labelKey, SettingsTab tab, Action onClick)
        {
            var rect = new Rect(tabRect.x + (tabWidth + 5f) * index, tabRect.y, tabWidth, Constants.UI_TAB_BUTTON_HEIGHT);
            using (new TextBlock(currentTab == tab ? Color.white : UIColors.TabInactive))
            {
                if (Widgets.ButtonText(rect, labelKey.Translate()))
                    onClick();
            }
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

        private static void DrawSectionHeader(Listing_Standard listing, string labelKey)
        {
            using (new TextBlock(GameFont.Medium))
            {
                var rect = listing.GetRect(Text.LineHeight);
                Widgets.Label(rect, labelKey.Translate());
            }
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
            Widgets.Checkbox(checkRect.x, checkRect.y, ref value, checkboxSize);

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

            if (oldValue != value)
            {
                bool newValue = value;
                AutoArmLogger.Debug(() => $"Setting changed: {label} = {newValue}");
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
            float newValue = Widgets.HorizontalSlider(actualSliderRect, value, min, max);
            if (GUI.enabled) value = newValue;

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

            if (Math.Abs(oldValue - value) > 0.01f)
            {
                float v = value;
                string disp = displayValue;
                if (isWeaponPreference)
                    AutoArmLogger.Debug(() => $"Weapon preference changed to: {disp} (value: {v:F2})");
                else
                    AutoArmLogger.Debug(() => $"Setting changed: {label} = {v:F2}");
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

                    PawnValidation.ClearCache();

                    DroppedItems.ClearAll();

                    Cleanup.ClearAllCaches();

                    foreach (var map in Find.Maps)
                    {
                        WeaponCache.RebuildCache(map);
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

            DrawCheckbox(listing, "AutoArm_ShowForcedLabels".Translate(), ref settings.showForcedLabels,
                "AutoArm_ShowForcedLabelsDesc".Translate());

            listing.Gap(Constants.UI_SMALL_GAP);

            DrawCheckbox(listing, "AutoArm_DisableDuringRaids".Translate(), ref settings.disableDuringRaids,
                "AutoArm_DisableDuringRaidsDesc".Translate());

            listing.Gap(Constants.UI_SMALL_GAP);

            bool prevOnlyEquipStorage = settings.onlyAutoEquipFromStorage;
            DrawCheckbox(listing, "AutoArm_OnlyEquipFromStorage".Translate(), ref settings.onlyAutoEquipFromStorage,
                "AutoArm_OnlyEquipFromStorageDesc".Translate());
            if (prevOnlyEquipStorage != settings.onlyAutoEquipFromStorage)
            {
                WeaponCache.ClearScoreCache();
                EquipEligibility.Clear();
            }

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

            bool prevAllowForced = settings.allowForcedWeaponUpgrades;
            DrawCheckbox(listing, "AutoArm_AllowForcedWeaponUpgrades".Translate(), ref settings.allowForcedWeaponUpgrades,
                "AutoArm_AllowForcedWeaponUpgradesDesc".Translate());
            if (prevAllowForced != settings.allowForcedWeaponUpgrades)
                WeaponCache.ClearScoreCache();

            listing.Gap(Constants.UI_SMALL_GAP);

            DrawCheckbox(listing, "AutoArm_AllowTemporaryColonists".Translate(), ref settings.allowTemporaryColonists,
                "AutoArm_AllowTemporaryColonistsDesc".Translate());

            GUI.color = oldColor;
            GUI.enabled = wasEnabled;

            listing.Gap(Constants.UI_SECTION_GAP);
        }

        private void DrawCompatibilityTab(Listing_Standard listing)
        {
            DrawSectionHeader(listing, "AutoArm_CompatibilityPatches");
            listing.Gap(Constants.UI_SECTION_GAP);

            if (!SimpleSidearmsCompat.IsLoaded && !CECompat.IsLoaded && !PocketSandCompat.IsLoaded)
            {
                using (new TextBlock(UIColors.Dim))
                    listing.Label("AutoArm_NoCompatModsDetected".Translate());
                return;
            }

            DrawSimpleSidearmsSettings(listing);
            DrawCombatExtendedSettings(listing);
            DrawPocketSandSettings(listing);
        }

        private void DrawCompatStatusHeader(Listing_Standard listing, string name)
        {
            Text.Font = GameFont.Small;
            var nameRect = listing.GetRect(Text.LineHeight);

            string fullText = $"{name}: ";
            Widgets.Label(new Rect(nameRect.x, nameRect.y, 200f, nameRect.height), fullText);

            float statusX = nameRect.x + Text.CalcSize(fullText).x;
            using (new TextBlock(UIColors.Active))
                Widgets.Label(new Rect(statusX, nameRect.y, 400f, nameRect.height), "AutoArm_Loaded".Translate());
        }

        private void DrawSimpleSidearmsSettings(Listing_Standard listing)
        {
            if (!SimpleSidearmsCompat.IsLoaded) return;
            DrawCompatStatusHeader(listing, "AutoArm_SimpleSidearms".Translate());

            bool reflectionFailed = SimpleSidearmsCompat.ReflectionFailed;

            if (reflectionFailed)
            {
                listing.Gap(Constants.UI_TINY_GAP);
                using (new TextBlock(UIColors.Warning))
                {
                    var warningRect = listing.GetRect(Text.LineHeight * 2);
                    Widgets.Label(warningRect, "AutoArm_SimpleSidearmsReflectionFailed".Translate());
                }
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

                string upgradeLabel = "AutoArm_AllowSidearmUpgrades".Translate() + " " + "AutoArm_Experimental".Translate();
                DrawCheckbox(listing, upgradeLabel, ref tempAllowSidearmUpgrades,
                    "AutoArm_AllowSidearmUpgradesDesc".Translate(), 30f);

                settings.allowSidearmUpgrades = tempAllowSidearmUpgrades;
            }

            GUI.enabled = wasEnabled;

            listing.Gap(Constants.UI_SMALL_GAP);
        }

        private void DrawCombatExtendedSettings(Listing_Standard listing)
        {
            if (!CECompat.IsLoaded) return;
            DrawCompatStatusHeader(listing, "AutoArm_CombatExtended".Translate());

            Color oldColor = GUI.color;

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

            bool prevCheckCEAmmo = settings.checkCEAmmo;
            DrawCheckbox(listing, "AutoArm_RequireAmmunition".Translate(), ref settings.checkCEAmmo,
                "AutoArm_RequireAmmunitionDesc".Translate(), 30f);

            if (!ceAmmoSystemEnabled && settings.checkCEAmmo)
            {
                settings.checkCEAmmo = false;
            }

            if (prevCheckCEAmmo != settings.checkCEAmmo)
            {
                WeaponCache.ClearScoreCache();
                EquipEligibility.Clear();
            }

            GUI.color = oldColor;

            if (!ceAmmoSystemEnabled)
            {
                listing.Gap(Constants.UI_TINY_GAP);
                using (new TextBlock(UIColors.WarningSoft))
                {
                    var warningRect = listing.GetRect(Text.LineHeight);
                    Widgets.Label(warningRect, "AutoArm_CEAmmoSystemDisabled".Translate());
                }
            }

            listing.Gap(Constants.UI_SMALL_GAP);
        }

        private void DrawPocketSandSettings(Listing_Standard listing)
        {
            if (!PocketSandCompat.IsLoaded) return;
            DrawCompatStatusHeader(listing, "AutoArm_PocketSand".Translate());
            listing.Gap(Constants.UI_SMALL_GAP);
        }

        private void DrawAdvancedTab(Listing_Standard listing)
        {
            DrawSectionHeader(listing, "AutoArm_WeaponUpgrades");
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
            float prevPref = settings.weaponTypePreference;
            DrawSlider(listing, "AutoArm_WeaponPreference".Translate(), ref settings.weaponTypePreference,
                -1f, 1f, "custom",
                "AutoArm_WeaponPreferenceDesc".Translate(),
                false, true);
            if (Math.Abs(prevPref - settings.weaponTypePreference) > 0.001f)
                WeaponCache.ClearScoreCache();
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

            DrawSectionHeader(listing, "AutoArm_AgeRestrictions");

            DrawCheckbox(listing, "AutoArm_AllowChildrenToEquipWeapons".Translate(), ref settings.allowChildrenToEquipWeapons,
                "AutoArm_AllowChildrenToEquipWeaponsDesc".Translate());

            if (settings.allowChildrenToEquipWeapons)
            {
                float tempAge = (float)settings.childrenMinAge;
                DrawSlider(listing, "AutoArm_MinimumAge".Translate(), ref tempAge,
                    Constants.ChildMinAgeLimit, Constants.ChildMaxAgeLimit, "F0",
                    "AutoArm_MinimumAgeDesc".Translate(),
                    false);
                settings.childrenMinAge = Mathf.RoundToInt(tempAge);

                if (settings.childrenMinAge <= 3)
                {
                    using (new TextBlock(UIColors.WarningSoft))
                    {
                        var warningRect = listing.GetRect(Text.LineHeight);
                        Widgets.Label(warningRect, "AutoArm_WhatCouldGoWrong".Translate());
                    }
                }
            }
        }

        private void OpenDebugWindow()
        {
            if (debugWindow == null)
            {
                debugWindow = new DebugTools();
            }

            if (!Find.WindowStack.Windows.Contains(debugWindow))
            {
                Find.WindowStack.Add(debugWindow);
            }
            else
            {
                debugWindow.SetFocus();
            }

            PerfOverlay.OpenOrBringToFront();
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


    }

}
