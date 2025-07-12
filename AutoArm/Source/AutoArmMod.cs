using UnityEngine;
using Verse;
using RimWorld;
using System.Linq;
using System.Collections.Generic;
using System;

namespace AutoArm
{
    public class AutoArmMod : Mod
    {
        // UI Layout Constants
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

        // Performance thresholds
        private const float THRESHOLD_MIN = 1.01f;
        private const float THRESHOLD_MAX = 2.0f;
        private const int CHILD_AGE_MIN = 3;
        private const int CHILD_AGE_MAX = 18;

        public static AutoArmSettings settings;
        private SettingsTab currentTab = SettingsTab.General;

        // Separate debug window instance
        private AutoArmDebugWindow debugWindow;

        // Tab tracking
        private enum SettingsTab
        {
            General,
            Compatibility,
            Advanced,
            Debug
        }

        // Preset configurations
        private enum PerformancePreset
        {
            Responsive,
            Balanced,
            Performance
        }

        // Performance impact levels
        private enum PerformanceImpact
        {
            None,
            Low,
            Medium,
            High
        }

        public AutoArmMod(ModContentPack content) : base(content)
        {
            settings = GetSettings<AutoArmSettings>();
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            // Store original color to ensure proper restoration
            Color originalColor = GUI.color;

            try
            {
                DrawSettingsWindow(inRect);
            }
            finally
            {
                // Always restore original color
                GUI.color = originalColor;
            }
        }

        private void DrawSettingsWindow(Rect inRect)
        {
            var listing = new Listing_Standard();
            listing.Begin(inRect);

            DrawHeader(listing, inRect);
            DrawTabButtons(listing);

            // Content area
            var contentRect = listing.GetRect(inRect.height - listing.CurHeight - LINE_HEIGHT);
            DrawTabContent(contentRect);

            listing.End();
        }

        private void DrawHeader(Listing_Standard listing, Rect inRect)
        {
            // Reset button in top right
            DrawResetButton(inRect);

            // Title
            Text.Font = GameFont.Medium;
            listing.Label("AutoArm Settings v1.0");
            Text.Font = GameFont.Small;

            // Performance impact
            DrawPerformanceImpact(listing, "Overall Performance Impact", CalculateOverallImpact());
            listing.Gap(TINY_GAP);

            // Preset buttons
            DrawPresetButtons(listing);
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
            GUI.color = new Color(1f, 0.6f, 0.6f);

            if (Widgets.ButtonText(resetButtonRect, "Reset Config"))
            {
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    "Are you sure you want to reset all settings to defaults?",
                    () => {
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

            // Store original color
            Color originalColor = GUI.color;

            // General tab (green - essential)
            GUI.color = currentTab == SettingsTab.General ? Color.white : new Color(0.7f, 1f, 0.7f);
            if (Widgets.ButtonText(new Rect(tabRect.x, tabRect.y, tabWidth, TAB_BUTTON_HEIGHT), "General"))
                currentTab = SettingsTab.General;

            // Compatibility tab (yellow - conditional)
            GUI.color = currentTab == SettingsTab.Compatibility ? Color.white : new Color(1f, 1f, 0.7f);
            if (Widgets.ButtonText(new Rect(tabRect.x + tabWidth + 5f, tabRect.y, tabWidth, TAB_BUTTON_HEIGHT), "Compatibility"))
                currentTab = SettingsTab.Compatibility;

            // Advanced tab (orange - performance impact)
            GUI.color = currentTab == SettingsTab.Advanced ? Color.white : new Color(1f, 0.9f, 0.6f);
            if (Widgets.ButtonText(new Rect(tabRect.x + (tabWidth + 5f) * 2, tabRect.y, tabWidth, TAB_BUTTON_HEIGHT), "Advanced"))
                currentTab = SettingsTab.Advanced;

            // Debug tab (red - high impact) - Only show in dev mode
            if (Prefs.DevMode)
            {
                GUI.color = currentTab == SettingsTab.Debug ? Color.white : new Color(1f, 0.7f, 0.7f);
                if (Widgets.ButtonText(new Rect(tabRect.x + (tabWidth + 5f) * 3, tabRect.y, tabWidth, TAB_BUTTON_HEIGHT), "Debug"))
                    currentTab = SettingsTab.Debug;
            }

            GUI.color = originalColor;
            listing.Gap(SMALL_GAP);
        }

        private void DrawTabContent(Rect contentRect)
        {
            var innerRect = contentRect.ContractedBy(CONTENT_PADDING);

            // Draw semi-transparent background
            Widgets.DrawBoxSolid(contentRect, new Color(0.1f, 0.1f, 0.1f, 0.3f));

            // Draw tab content
            var innerListing = new Listing_Standard();
            innerListing.Begin(innerRect);

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
                case SettingsTab.Debug:
                    if (Prefs.DevMode)
                        DrawDebugTab(innerListing);
                    break;
            }

            innerListing.End();
        }

        // Helper method for consistent checkbox drawing
        private void DrawCheckbox(Listing_Standard listing, string label, ref bool value, string tooltip = null, float indent = 0f)
        {
            Rect labelRect = listing.GetRect(LINE_HEIGHT);
            Rect checkRect = new Rect(
                labelRect.x + LABEL_WIDTH - indent,
                labelRect.y + (LINE_HEIGHT - CHECKBOX_SIZE) / 2f,
                CHECKBOX_SIZE,
                CHECKBOX_SIZE
            );

            // Draw label with current value
            string displayLabel = tooltip != null ? $"{label} ({(value ? "Enabled" : "Disabled")})" : label;
            Widgets.Label(labelRect, displayLabel);

            // Draw checkbox
            bool oldValue = value;
            Widgets.Checkbox(checkRect.x, checkRect.y, ref value, CHECKBOX_SIZE);

            // Show tooltip
            if (tooltip != null && Mouse.IsOver(labelRect.ExpandedBy(10f)))
            {
                TooltipHandler.TipRegion(labelRect, tooltip);
            }

            // Log setting change if debug mode
            if (oldValue != value && settings.debugLogging)
            {
                Log.Message($"[AutoArm] Setting changed: {label} = {value}");
            }
        }

        // Helper for sliders with validation
        private void DrawSlider(Listing_Standard listing, string label, ref float value, float min, float max, string format = "P0", string tooltip = null)
        {
            Rect rect = listing.GetRect(LINE_HEIGHT);
            Rect labelRect = rect.LeftPart(0.7f);
            Rect sliderRect = rect.RightPart(0.3f);

            // Display formatted value
            string displayValue = format == "P0" ? value.ToString("P0") : value.ToString(format);
            Widgets.Label(labelRect, $"{label}: {displayValue}");

            // Draw slider with validation
            float oldValue = value;
            value = Widgets.HorizontalSlider(sliderRect, value, min, max);

            // Ensure value is within bounds
            value = Mathf.Clamp(value, min, max);

            // Show tooltip
            if (tooltip != null && Mouse.IsOver(rect))
            {
                TooltipHandler.TipRegion(rect, tooltip);
            }

            // Log significant changes
            if (Math.Abs(oldValue - value) > 0.01f && settings.debugLogging)
            {
                Log.Message($"[AutoArm] Setting changed: {label} = {value:F2}");
            }
        }

        private void DrawGeneralTab(Listing_Standard listing)
        {
            // Status indicator
            using (new ColorBlock(settings.modEnabled ? Color.green : Color.red))
            {
                listing.Label($"Mod Status: {(settings.modEnabled ? "ACTIVE" : "DISABLED")}");
            }
            listing.Gap(SMALL_GAP);

            // Main settings
            DrawCheckbox(listing, "Enable AutoArm", ref settings.modEnabled,
                "When enabled, colonists will automatically equip better weapons based on their outfit policy.");

            listing.Gap(SMALL_GAP);

            DrawCheckbox(listing, "Show notifications", ref settings.showNotifications,
                "Shows blue notification messages when colonists equip or drop weapons.");

            listing.Gap(SECTION_GAP);

            // Colony info
            DrawColonyInfo(listing);
        }

        private void DrawColonyInfo(Listing_Standard listing)
        {
            if (Current.Game?.CurrentMap == null) return;

            Text.Font = GameFont.Tiny;
            using (new ColorBlock(Color.gray))
            {
                int colonistCount = Find.CurrentMap.mapPawns.FreeColonistsCount;
                listing.Label($"Current colony size: {colonistCount} colonists");

                string recommended = colonistCount < 10 ? "Small Colony" :
                                   colonistCount < 20 ? "Large Colony" :
                                   "Heavily Modded";
                listing.Label($"Recommended preset: {recommended}");
            }
            Text.Font = GameFont.Small;
        }

        private void DrawCompatibilityTab(Listing_Standard listing)
        {
            Text.Font = GameFont.Medium;
            listing.Label("Detected Mods");
            Text.Font = GameFont.Small;
            listing.Gap(SMALL_GAP);

            // Mod detection with status colors
            DrawModStatus(listing, "Simple Sidearms", SimpleSidearmsCompat.IsLoaded());
            DrawModStatus(listing, "Combat Extended", CECompat.IsLoaded());
            DrawModStatus(listing, "Infusion 2", InfusionCompat.IsLoaded());

            listing.Gap(SECTION_GAP);

            // Mod-specific settings
            DrawSimpleSidearmsSettings(listing);
            DrawCombatExtendedSettings(listing);
        }

        private void DrawModStatus(Listing_Standard listing, string modName, bool isLoaded)
        {
            using (new ColorBlock(isLoaded ? Color.green : Color.gray))
            {
                listing.Label($"{modName}: {(isLoaded ? "✓ Loaded" : "✗ Not found")}");
            }
        }

        private void DrawSimpleSidearmsSettings(Listing_Standard listing)
        {
            if (!SimpleSidearmsCompat.IsLoaded()) return;

            Text.Font = GameFont.Medium;
            listing.Label("Simple Sidearms");
            Text.Font = GameFont.Small;

            DrawCheckbox(listing, "Auto-equip sidearms", ref settings.autoEquipSidearms,
                "Allows colonists to automatically pick up additional weapons as sidearms.");

            if (settings.autoEquipSidearms)
            {
                listing.Gap(TINY_GAP);
                listing.Indent(20f);

                using (new ColorBlock(new Color(1f, 1f, 0.6f))) // Yellow for experimental
                {
                    DrawCheckbox(listing, "Allow sidearm upgrades - Experimental",
                        ref settings.allowSidearmUpgrades,
                        "When enabled, colonists will upgrade existing sidearms to better weapons.",
                        20f);
                }

                listing.Outdent(20f);

                if (settings.allowSidearmUpgrades)
                {
                    using (new ColorBlock(new Color(1f, 0.8f, 0.4f)))
                    {
                        listing.Label("⚠ Sidearm upgrades can impact performance in large colonies");
                    }
                }
            }
        }

        private void DrawCombatExtendedSettings(Listing_Standard listing)
        {
            if (!CECompat.IsLoaded()) return;

            listing.Gap(SECTION_GAP);
            Text.Font = GameFont.Medium;
            listing.Label("Combat Extended");
            Text.Font = GameFont.Small;

            DrawCheckbox(listing, "Check for ammunition", ref settings.checkCEAmmo,
                "When enabled, colonists will only pick up weapons if they have access to appropriate ammunition.");
        }

        private void DrawAdvancedTab(Listing_Standard listing)
        {
            Text.Font = GameFont.Medium;
            listing.Label("Weapon Selection");
            Text.Font = GameFont.Small;
            listing.Gap(SMALL_GAP);

            // Weapon upgrade threshold
            DrawSlider(listing, "Weapon upgrade threshold", ref settings.weaponUpgradeThreshold,
                THRESHOLD_MIN, THRESHOLD_MAX, "P0",
                "How much better a weapon needs to be before colonists will switch. Lower values mean more frequent switching.");

            // Performance warning
            if (settings.weaponUpgradeThreshold < 1.10f)
            {
                using (new ColorBlock(new Color(1f, 0.8f, 0.4f)))
                {
                    listing.Label("⚠ Low threshold may cause frequent weapon switching");
                }
            }

            listing.Gap(SECTION_GAP);

            DrawAgeRestrictions(listing);
            DrawNobilitySettings(listing);
        }

        private void DrawAgeRestrictions(Listing_Standard listing)
        {
            if (!ModsConfig.BiotechActive) return;

            Text.Font = GameFont.Medium;
            listing.Label("Age Restrictions");
            Text.Font = GameFont.Small;

            DrawCheckbox(listing, "Allow children to equip weapons", ref settings.allowChildrenToEquipWeapons,
                "When enabled, child colonists can pick up and use weapons.");

            if (settings.allowChildrenToEquipWeapons)
            {
                listing.Label($"Minimum age: {settings.childrenMinAge}");

                // Fixed slider with proper float parameters
                float tempAge = (float)settings.childrenMinAge;
                tempAge = listing.Slider(tempAge, (float)CHILD_AGE_MIN, (float)CHILD_AGE_MAX);
                settings.childrenMinAge = Mathf.RoundToInt(tempAge);

                if (settings.childrenMinAge < 10)
                {
                    using (new ColorBlock(new Color(1f, 0.8f, 0.4f)))
                    {
                        listing.Label("⚠ Very young children with weapons may be controversial");
                    }
                }
            }
        }

        private void DrawNobilitySettings(Listing_Standard listing)
        {
            if (!ModsConfig.RoyaltyActive) return;

            listing.Gap(SECTION_GAP);
            Text.Font = GameFont.Medium;
            listing.Label("Nobility");
            Text.Font = GameFont.Small;

            DrawCheckbox(listing, "Respect conceited nobles", ref settings.respectConceitedNobles,
                "When enabled, conceited nobles won't automatically switch weapons.");
        }

        private void DrawDebugTab(Listing_Standard listing)
        {
            // Warning header
            using (new ColorBlock(new Color(1f, 0.6f, 0.6f)))
            {
                listing.Label("⚠ Debug features can significantly impact performance!");
            }
            listing.Gap(SMALL_GAP);

            DrawCheckbox(listing, "Enable debug logging", ref settings.debugLogging,
                "Outputs detailed information to the debug log for troubleshooting.");

            if (settings.debugLogging)
            {
                DrawPerformanceImpact(listing, "Debug Logging Impact", PerformanceImpact.High);
            }

            // Debug tools button
            if (Current.Game?.CurrentMap != null)
            {
                listing.Gap(SECTION_GAP);
                Text.Font = GameFont.Medium;
                listing.Label("Testing Tools");
                Text.Font = GameFont.Small;

                if (listing.ButtonText("Open Debug Window"))
                {
                    OpenDebugWindow();
                }
            }
            else
            {
                listing.Gap(SECTION_GAP);
                using (new ColorBlock(Color.gray))
                {
                    listing.Label("Testing tools require an active game");
                }
            }
        }

        private void OpenDebugWindow()
        {
            // Create or show debug window
            if (debugWindow == null)
            {
                debugWindow = new AutoArmDebugWindow();
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

        private void DrawPerformanceImpact(Listing_Standard listing, string label, PerformanceImpact impact)
        {
            Rect rect = listing.GetRect(22f);
            Color impactColor;
            string impactText;

            switch (impact)
            {
                case PerformanceImpact.None:
                    impactColor = Color.green;
                    impactText = "No impact";
                    break;
                case PerformanceImpact.Low:
                    impactColor = new Color(0.7f, 1f, 0.7f);
                    impactText = "Low impact";
                    break;
                case PerformanceImpact.Medium:
                    impactColor = Color.yellow;
                    impactText = "Medium impact";
                    break;
                case PerformanceImpact.High:
                    impactColor = new Color(1f, 0.6f, 0.6f);
                    impactText = "High impact";
                    break;
                default:
                    impactColor = Color.white;
                    impactText = "Unknown";
                    break;
            }

            using (new ColorBlock(impactColor))
            {
                Widgets.Label(rect, $"{label}: {impactText}");
            }
        }

        private void DrawPresetButtons(Listing_Standard listing)
        {
            Rect rect = listing.GetRect(35f);
            float buttonWidth = rect.width / 3f - 5f;

            if (Widgets.ButtonText(new Rect(rect.x, rect.y, buttonWidth, 30f), "Small Colony"))
            {
                ApplyPreset(PerformancePreset.Responsive);
                Messages.Message("Applied Small Colony preset - Most responsive settings", MessageTypeDefOf.NeutralEvent, false);
            }

            if (Widgets.ButtonText(new Rect(rect.x + buttonWidth + 5f, rect.y, buttonWidth, 30f), "Large Colony"))
            {
                ApplyPreset(PerformancePreset.Balanced);
                Messages.Message("Applied Large Colony preset - Balanced for 20+ colonists", MessageTypeDefOf.NeutralEvent, false);
            }

            if (Widgets.ButtonText(new Rect(rect.x + (buttonWidth + 5f) * 2, rect.y, buttonWidth, 30f), "Heavily Modded"))
            {
                ApplyPreset(PerformancePreset.Performance);
                Messages.Message("Applied Heavily Modded preset - Optimized for performance", MessageTypeDefOf.NeutralEvent, false);
            }
        }

        private void ApplyPreset(PerformancePreset preset)
        {
            switch (preset)
            {
                case PerformancePreset.Responsive:
                    settings.weaponUpgradeThreshold = 1.05f;
                    settings.autoEquipSidearms = true;
                    settings.allowSidearmUpgrades = true;
                    settings.checkCEAmmo = true;
                    break;

                case PerformancePreset.Balanced:
                    settings.weaponUpgradeThreshold = 1.10f;
                    settings.autoEquipSidearms = true;
                    settings.allowSidearmUpgrades = false;
                    settings.checkCEAmmo = true;
                    break;

                case PerformancePreset.Performance:
                    settings.weaponUpgradeThreshold = 1.15f;
                    settings.autoEquipSidearms = false;
                    settings.allowSidearmUpgrades = false;
                    settings.checkCEAmmo = false;
                    break;
            }
        }

        private PerformanceImpact CalculateOverallImpact()
        {
            int score = 0;

            if (settings.debugLogging) score += 3;
            if (settings.autoEquipSidearms) score += 2;
            if (settings.allowSidearmUpgrades) score += 2;
            if (settings.checkCEAmmo) score += 1;
            if (settings.weaponUpgradeThreshold < 1.10f) score += 1;
            if (settings.showNotifications) score += 1;

            if (score <= 2) return PerformanceImpact.None;
            if (score <= 5) return PerformanceImpact.Low;
            if (score <= 8) return PerformanceImpact.Medium;
            return PerformanceImpact.High;
        }

        public override void WriteSettings()
        {
            ValidateSettings();
            base.WriteSettings();
        }

        private void ValidateSettings()
        {
            // Ensure all values are within valid ranges
            settings.weaponUpgradeThreshold = Mathf.Clamp(settings.weaponUpgradeThreshold, THRESHOLD_MIN, THRESHOLD_MAX);
            settings.childrenMinAge = Mathf.Clamp(settings.childrenMinAge, CHILD_AGE_MIN, CHILD_AGE_MAX);

            // Log validation if debug mode
            if (settings.debugLogging)
            {
                Log.Message($"[AutoArm] Settings validated - Threshold: {settings.weaponUpgradeThreshold:F2}, Child Age: {settings.childrenMinAge}");
            }
        }

        public override string SettingsCategory()
        {
            return "AutoArm";
        }
    }

    // Helper class for managing color state
    public class ColorBlock : IDisposable
    {
        private readonly Color originalColor;

        public ColorBlock(Color newColor)
        {
            originalColor = GUI.color;
            GUI.color = newColor;
        }

        public void Dispose()
        {
            GUI.color = originalColor;
        }
    }

    // Separate debug window for testing
    public class AutoArmDebugWindow : Window
    {
        private Vector2 scrollPosition;
        private string testResultsText = "";

        public AutoArmDebugWindow()
        {
            doCloseX = true;
            closeOnClickedOutside = false;
            absorbInputAroundWindow = false;
            draggable = true;
            resizeable = true;
        }

        public override Vector2 InitialSize => new Vector2(600f, 400f);

        protected override void SetInitialSizeAndPosition()
        {
            windowRect = new Rect(100f, 100f, InitialSize.x, InitialSize.y);
        }

        public void SetFocus()
        {
            // Bring window to front
            Find.WindowStack.ImmediateWindow(GetHashCode(), windowRect, WindowLayer.Dialog, () => DoWindowContents(windowRect.AtZero()));
        }

        public override void DoWindowContents(Rect inRect)
        {
            var listing = new Listing_Standard();
            listing.Begin(inRect);

            Text.Font = GameFont.Medium;
            listing.Label("AutoArm Debug Tools");
            Text.Font = GameFont.Small;

            listing.Gap(20f);

            // Test buttons
            if (listing.ButtonText("Test Weapon Detection"))
            {
                TestWeaponDetection();
            }

            if (listing.ButtonText("Test Pawn Validation"))
            {
                TestPawnValidation();
            }

            if (listing.ButtonText("Clear Test Results"))
            {
                ClearTestResults();
            }

            listing.Gap(20f);

            // Results area
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

            Color textColor = testResultsText.Contains("FAILED") ?
                new Color(1f, 0.8f, 0.8f) : new Color(0.8f, 1f, 0.8f);

            using (new ColorBlock(textColor))
            {
                Widgets.Label(new Rect(0, 0, innerRect.width - 20f, textHeight), testResultsText);
            }

            Widgets.EndScrollView();
        }

        private void TestWeaponDetection()
        {
            var map = Find.CurrentMap;
            if (map == null)
            {
                testResultsText = "Error: No active map";
                return;
            }

            var weapons = map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon)
                .OfType<ThingWithComps>()
                .Take(10)
                .ToList();

            testResultsText = $"Found {weapons.Count} weapons on map:\n\n";
            foreach (var weapon in weapons)
            {
                testResultsText += $"- {weapon.Label} ({weapon.def.defName})\n";
            }
        }

        private void TestPawnValidation()
        {
            var map = Find.CurrentMap;
            if (map == null)
            {
                testResultsText = "Error: No active map";
                return;
            }

            var colonists = map.mapPawns.FreeColonists.ToList();
            int validCount = 0;

            testResultsText = $"Testing {colonists.Count} colonists:\n\n";

            foreach (var pawn in colonists)
            {
                string reason;
                bool isValid = JobGiverHelpers.IsValidPawnForAutoEquip(pawn, out reason);

                if (isValid)
                {
                    validCount++;
                    testResultsText += $"✓ {pawn.Name}: Valid\n";
                }
                else
                {
                    testResultsText += $"✗ {pawn.Name}: Invalid - {reason}\n";
                }
            }

            testResultsText += $"\n\nSummary: {validCount}/{colonists.Count} colonists valid for auto-equip";
        }

        private void ClearTestResults()
        {
            testResultsText = "";
            scrollPosition = Vector2.zero;
        }
    }
}