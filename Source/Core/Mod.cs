
using AutoArm.Caching;
using AutoArm.Compatibility;
using AutoArm.Definitions;
using AutoArm.Helpers;
using AutoArm.Jobs;
using AutoArm.Testing;
using AutoArm.UI;
using RimWorld;
using System;
using System.Collections.Generic;
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

        private DebugTools debugWindow;

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

        private static Vector2 settingsScroll = Vector2.zero;
        private static float settingsViewHeight = 1000f;

        private void DrawSettingsWindow(Rect inRect)
        {
            var headerListing = new Listing_Standard();
            headerListing.Begin(inRect);
            DrawHeader(headerListing, inRect);
            float headerHeight = headerListing.CurHeight;
            headerListing.End();

            var scrollRect = new Rect(inRect.x, inRect.y + headerHeight, inRect.width, inRect.height - headerHeight);
            var viewRect = new Rect(0f, 0f, scrollRect.width - 16f, settingsViewHeight);
            Widgets.BeginScrollView(scrollRect, ref settingsScroll, viewRect);

            var listing = new Listing_Standard { maxOneColumn = true };
            listing.Begin(viewRect);

            Color oldColor = GUI.color;
            bool wasEnabled = GUI.enabled;
            try
            {
                DrawGeneralTab(listing);

                if (!settings.modEnabled)
                {
                    GUI.color = Color.gray;
                    GUI.enabled = false;
                }

                DrawCompatibilityTab(listing);

                settingsViewHeight = listing.CurHeight;
            }
            finally
            {
                GUI.color = oldColor;
                GUI.enabled = wasEnabled;

                listing.End();
                Widgets.EndScrollView();
            }
        }

        private void DrawDebugRow(Listing_Standard listing)
        {
            var row = listing.GetRect(Constants.UI_LINE_HEIGHT);
            if (Mouse.IsOver(row))
                Widgets.DrawHighlight(row);

            using (new TextBlock(TextAnchor.MiddleLeft))
                Widgets.Label(new Rect(row.x, row.y, row.width - 110f, row.height), "AutoArm_Debug".Translate());

            bool hasMap = Current.Game?.CurrentMap != null;
            var buttonRect = new Rect(row.xMax - 84f, row.y + 1f, 80f, row.height - 2f);

            if (!hasMap)
                TooltipHandler.TipRegion(buttonRect, "AutoArm_DebugRequiresActiveGame".Translate());

            Color oldColor = GUI.color;
            if (!hasMap)
                GUI.color = Color.gray;
            bool clicked = Widgets.ButtonText(buttonRect, "AutoArm_DebugOpen".Translate(), true, true, hasMap);
            GUI.color = oldColor;

            if (clicked)
                OpenDebugWindow();
        }

        private void DrawHeader(Listing_Standard listing, Rect inRect)
        {
            DrawResetButton(inRect);

            var titleRect = listing.GetRect(30f);
            DrawDiscordLink(new Rect(titleRect.x, titleRect.y + 2f, 170f, 24f));

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
                0f,
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
                            EquipEligibility.Clear();
                            PawnValidation.ClearCache();
                            Messages.Message("AutoArm_SettingsReset".Translate(), MessageTypeDefOf.NeutralEvent, false);
                        }));
                }
            }
        }


        private static void DrawSectionHeader(Listing_Standard listing, string labelKey)
        {
            var rect = listing.GetRect(24f);
            using (new TextBlock(Widgets.SeparatorLabelColor))
                Widgets.Label(new Rect(rect.x, rect.y, rect.width, 20f), labelKey.Translate());
            Color oldColor = GUI.color;
            GUI.color = Widgets.SeparatorLineColor;
            Widgets.DrawLineHorizontal(rect.x, rect.yMax - 2f, rect.width);
            GUI.color = oldColor;
            listing.Gap(Constants.UI_TINY_GAP);
        }

        private void DrawCheckbox(Listing_Standard listing, string label, ref bool value, string tooltip = null, float indent = 0f, string suffix = null)
        {
            Rect fullRect = listing.GetRect(Constants.UI_LINE_HEIGHT);

            if (Mouse.IsOver(fullRect))
            {
                Widgets.DrawHighlight(fullRect);
                if (tooltip != null)
                    TooltipHandler.TipRegion(fullRect, tooltip);
            }

            float checkboxSize = Constants.UI_CHECKBOX_SIZE;

            Rect labelRect = new Rect(
                fullRect.x + indent,
                fullRect.y,
                fullRect.width - checkboxSize - 10f - indent,
                fullRect.height
            );

            using (new TextBlock(TextAnchor.MiddleLeft))
            {
                Widgets.Label(labelRect, label);
                if (suffix != null)
                {
                    float labelWidth = Text.CalcSize(label).x;
                    using (new TextBlock(UIColors.Dim))
                        Widgets.Label(new Rect(labelRect.x + labelWidth + 6f, labelRect.y,
                            labelRect.width - labelWidth - 6f, labelRect.height), suffix);
                }
            }

            bool oldValue = value;
            if (GUI.enabled)
                Widgets.ToggleInvisibleDraggable(fullRect, ref value, doMouseoverSound: true);

            Color oldColor = GUI.color;
            if (!GUI.enabled)
                GUI.color = Color.gray;
            Widgets.CheckboxDraw(fullRect.xMax - checkboxSize - 4f,
                fullRect.y + (Constants.UI_LINE_HEIGHT - checkboxSize) / 2f, value, false, checkboxSize);
            GUI.color = oldColor;

            if (oldValue != value)
            {
                bool newValue = value;
                AutoArmLogger.Debug(() => $"Setting changed: {label} = {newValue}");
            }
        }

        private void DrawSlider(Listing_Standard listing, string label, ref float value, float min, float max, string format = "P0", string tooltip = null, bool isPercentageBetter = false, bool isWeaponPreferenceMode = false)
        {
            listing.Gap(4f);
            Rect row = listing.GetRect(Constants.UI_LINE_HEIGHT);

            if (Mouse.IsOver(row))
            {
                Widgets.DrawHighlight(row);
                if (tooltip != null)
                    TooltipHandler.TipRegion(row, tooltip);
            }

            const float sliderWidth = 200f;
            const float valueWidth = 170f;

            float sliderHeight = 20f;
            Rect actualSliderRect = new Rect(row.xMax - sliderWidth - 4f, row.y + (row.height - sliderHeight) / 2f, sliderWidth, sliderHeight);

            float oldValue = value;
            float newValue = Widgets.HorizontalSlider(actualSliderRect, value, min, max);
            if (isWeaponPreferenceMode)
                newValue = Mathf.Round(newValue * 3f) / 3f;
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
                displayValue = $"+{percentBetter:F0}%";
            }
            else
            {
                displayValue = format == "P0" ? value.ToString("P0") : value.ToString(format);
            }

            value = Mathf.Clamp(value, min, max);

            Rect labelRect = new Rect(row.x, row.y, row.width - sliderWidth - valueWidth - 16f, row.height);
            using (new TextBlock(TextAnchor.MiddleLeft))
                Widgets.Label(labelRect, label);

            Rect valueRect = new Rect(labelRect.xMax + 4f, row.y, valueWidth, row.height);
            Color oldTextColor = GUI.color;
            GUI.color = valueColor;
            using (new TextBlock(TextAnchor.MiddleRight))
                Widgets.Label(valueRect, displayValue);
            GUI.color = oldTextColor;

            if (Math.Abs(oldValue - value) > 0.01f)
            {
                float v = value;
                string disp = displayValue;
                if (isWeaponPreference)
                    AutoArmLogger.Debug(() => $"Weapon preference changed to: {disp} (value: {v:F2})");
                else
                    AutoArmLogger.Debug(() => $"Setting changed: {label} = {v:F2}");
            }

            listing.Gap(4f);
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
                }
                else
                {
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
            }

            Color oldColor = GUI.color;
            bool wasEnabled = GUI.enabled;
            if (!settings.modEnabled)
            {
                GUI.color = Color.gray;
                GUI.enabled = false;
            }

            DrawCheckbox(listing, "AutoArm_ShowNotifications".Translate(), ref settings.showNotifications,
                "AutoArm_ShowNotificationsDesc".Translate());

            DrawCheckbox(listing, "AutoArm_ShowForcedLabels".Translate(), ref settings.showForcedLabels,
                "AutoArm_ShowForcedLabelsDesc".Translate());

            if (Prefs.DevMode)
                DrawDebugRow(listing);

            listing.Gap(Constants.UI_SECTION_GAP);
            DrawSectionHeader(listing, "AutoArm_SectionEquipping");

            DrawCheckbox(listing, "AutoArm_UpgradeEquippedWeapons".Translate(), ref settings.upgradeEquippedWeapons,
                "AutoArm_UpgradeEquippedWeaponsDesc".Translate());

            bool priorUpgradeEnabled = GUI.enabled;
            GUI.enabled = priorUpgradeEnabled && settings.upgradeEquippedWeapons;

            DrawSlider(listing, "AutoArm_Threshold".Translate(), ref settings.weaponUpgradeThreshold,
                Constants.WeaponUpgradeThresholdMin, Constants.WeaponUpgradeThresholdMax, "F2",
                "AutoArm_ThresholdDesc".Translate(),
                true);

            bool prevAllowForced = settings.allowForcedWeaponUpgrades;
            DrawCheckbox(listing, "AutoArm_AllowForcedWeaponUpgrades".Translate(), ref settings.allowForcedWeaponUpgrades,
                "AutoArm_AllowForcedWeaponUpgradesDesc".Translate());
            if (prevAllowForced != settings.allowForcedWeaponUpgrades)
                WeaponCache.ClearScoreCache();

            GUI.enabled = priorUpgradeEnabled;

            if (ModsConfig.RoyaltyActive)
            {
                bool oldRespectBonds = settings.respectWeaponBonds;
                DrawCheckbox(listing, "AutoArm_ForceWeapon".Translate(), ref settings.respectWeaponBonds,
                    "AutoArm_ForceWeaponDesc".Translate());

                if (!oldRespectBonds && settings.respectWeaponBonds && Current.Game != null)
                {
                    MarkBondedAsForced();
                }
            }

            DrawWeaponPreferenceSlider(listing);

            bool prevOnlyEquipStorage = settings.onlyAutoEquipFromStorage;
            DrawCheckbox(listing, "AutoArm_OnlyEquipFromStorage".Translate(), ref settings.onlyAutoEquipFromStorage,
                "AutoArm_OnlyEquipFromStorageDesc".Translate());
            if (prevOnlyEquipStorage != settings.onlyAutoEquipFromStorage)
            {
                WeaponCache.ClearScoreCache();
                EquipEligibility.Clear();
            }

            DrawCheckbox(listing, "AutoArm_DisableDuringRaids".Translate(), ref settings.disableDuringRaids,
                "AutoArm_DisableDuringRaidsDesc".Translate());

            DrawCheckbox(listing, "AutoArm_AllowTemporaryColonists".Translate(), ref settings.allowTemporaryColonists,
                "AutoArm_AllowTemporaryColonistsDesc".Translate());

            DrawAgeRestrictions(listing);

            GUI.color = oldColor;
            GUI.enabled = wasEnabled;
        }

        private void DrawCompatibilityTab(Listing_Standard listing)
        {
            bool ss = SimpleSidearmsCompat.IsLoaded;
            bool ce = CECompat.IsLoaded;
            bool ps = PocketSandCompat.IsLoaded;
            if (!ss && !ce && !ps)
                return;

            listing.Gap(Constants.UI_SECTION_GAP);
            DrawSectionHeader(listing, "AutoArm_Compatibility");

            DrawSimpleSidearmsSettings(listing);
            if (ss && (ce || ps))
                listing.Gap(Constants.UI_SMALL_GAP);
            DrawCombatExtendedSettings(listing);
            if (ce && ps)
                listing.Gap(Constants.UI_SMALL_GAP);
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
                bool priorEnabled = GUI.enabled;
                GUI.enabled = priorEnabled && settings.upgradeEquippedWeapons;

                bool tempAllowSidearmUpgrades = settings.allowSidearmUpgrades;

                DrawCheckbox(listing, "AutoArm_AllowSidearmUpgrades".Translate(), ref tempAllowSidearmUpgrades,
                    "AutoArm_AllowSidearmUpgradesDesc".Translate(), 30f, "AutoArm_Experimental".Translate());

                settings.allowSidearmUpgrades = tempAllowSidearmUpgrades;
                GUI.enabled = priorEnabled;
            }

            GUI.enabled = wasEnabled;
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
        }

        private void DrawPocketSandSettings(Listing_Standard listing)
        {
            if (!PocketSandCompat.IsLoaded) return;
            DrawCompatStatusHeader(listing, "AutoArm_PocketSand".Translate());
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
                        var underSlider = new Rect(warningRect.xMax - 204f, warningRect.y, 200f, warningRect.height);
                        Widgets.Label(underSlider, "AutoArm_WhatCouldGoWrong".Translate());
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
                            var bonded = pawn.equipment.Primary;
                            AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] Bonded weapon {AutoArmLogger.GetWeaponLabelLower(bonded)} marked as forced (setting enabled)");
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
                                    AutoArmLogger.Debug(() => $"[{AutoArmLogger.GetPawnName(pawn)}] Bonded weapon {AutoArmLogger.GetWeaponLabelLower(weapon)} in inventory marked as forced (setting enabled)");
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

    public class AutoArmSettingsDialog : Dialog_ModSettings
    {
        public AutoArmSettingsDialog(Mod mod) : base(mod)
        {
            soundAppear = null;
        }

        public override Vector2 InitialSize => new Vector2(650f, 700f);
    }

}
