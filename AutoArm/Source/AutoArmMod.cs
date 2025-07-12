using UnityEngine;
using Verse;
using RimWorld;
using System.Linq;
using System.Collections.Generic;
using AutoArm.Testing;

namespace AutoArm
{
    public class AutoArmMod : Mod
    {
        public static AutoArmSettings settings;
        private Vector2 scrollPosition;
        private string testResultsText = "";
        private TestResults lastTestResults;
        private bool showTestResults = false;

        // Tab tracking
        private enum SettingsTab
        {
            General,
            Compatibility,
            Advanced,
            Debug
        }
        private SettingsTab currentTab = SettingsTab.General;

        // Preset configurations
        private enum PerformancePreset
        {
            Responsive,
            Balanced,
            Performance
        }

        public AutoArmMod(ModContentPack content) : base(content)
        {
            settings = GetSettings<AutoArmSettings>();
        }

        // Helper method for left-aligned checkboxes with tooltips showing current value
        private void DrawLeftAlignedCheckbox(Listing_Standard listing, string labelKey, ref bool value, string tooltipKey = null, float indent = 0f)
        {
            float lineHeight = 30f;
            float checkboxSize = 24f;
            float labelWidth = 250f;

            Rect labelRect = listing.GetRect(lineHeight);
            Rect checkRect = new Rect(labelRect.x + labelWidth - indent, labelRect.y + (lineHeight - checkboxSize) / 2f, checkboxSize, checkboxSize);

            // Draw label with current value
            string label = labelKey;
            if (tooltipKey != null)
            {
                label += $" ({(value ? "Enabled" : "Disabled")})";
            }

            Widgets.Label(labelRect, label);

            // Color coding for performance impact
            Color oldColor = GUI.color;
            if (labelKey.Contains("debug") || labelKey.Contains("Debug"))
                GUI.color = new Color(1f, 0.8f, 0.4f); // Yellow for debug features

            Widgets.Checkbox(checkRect.x, checkRect.y, ref value, checkboxSize);
            GUI.color = oldColor;

            if (tooltipKey != null && Mouse.IsOver(labelRect.ExpandedBy(10f)))
            {
                TooltipHandler.TipRegion(labelRect, tooltipKey);
            }
        }

        // Helper for sliders with value display and color coding
        private void DrawSliderWithLabel(Listing_Standard listing, string label, ref float value, float min, float max, string format = "P0", string tooltip = null)
        {
            Rect rect = listing.GetRect(30f);
            Rect labelRect = rect.LeftPart(0.7f);
            Rect sliderRect = rect.RightPart(0.3f);

            // Color code based on performance impact
            Color oldColor = GUI.color;
            if (value < min + (max - min) * 0.3f)
                GUI.color = Color.green; // Low values = better performance
            else if (value < min + (max - min) * 0.7f)
                GUI.color = Color.yellow; // Medium values
            else
                GUI.color = new Color(1f, 0.6f, 0.6f); // High values = worse performance

            string displayValue = format == "P0" ? value.ToString("P0") : value.ToString(format);
            Widgets.Label(labelRect, $"{label}: {displayValue}");

            value = Widgets.HorizontalSlider(sliderRect, value, min, max);
            GUI.color = oldColor;

            if (tooltip != null && Mouse.IsOver(rect))
            {
                TooltipHandler.TipRegion(rect, tooltip);
            }
        }

        // Performance impact indicator
        private void DrawPerformanceImpact(Listing_Standard listing, string label, PerformanceImpact impact)
        {
            Rect rect = listing.GetRect(22f);
            Color impactColor = Color.white;
            string impactText = "";

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
            }

            Color oldColor = GUI.color;
            GUI.color = impactColor;
            Widgets.Label(rect, $"{label}: {impactText}");
            GUI.color = oldColor;
        }

        private enum PerformanceImpact
        {
            None,
            Low,
            Medium,
            High
        }

        // Preset buttons
        private void DrawPresetButtons(Listing_Standard listing)
        {
            Rect rect = listing.GetRect(35f);
            float buttonWidth = rect.width / 3f - 5f;

            if (Widgets.ButtonText(new Rect(rect.x, rect.y, buttonWidth, 30f), "Small Colony", true, false, true))
            {
                ApplyPreset(PerformancePreset.Responsive);
                Messages.Message("Applied Small Colony preset - Most responsive settings", MessageTypeDefOf.NeutralEvent, false);
            }

            if (Widgets.ButtonText(new Rect(rect.x + buttonWidth + 5f, rect.y, buttonWidth, 30f), "Large Colony", true, false, true))
            {
                ApplyPreset(PerformancePreset.Balanced);
                Messages.Message("Applied Large Colony preset - Balanced for 20+ colonists", MessageTypeDefOf.NeutralEvent, false);
            }

            if (Widgets.ButtonText(new Rect(rect.x + (buttonWidth + 5f) * 2, rect.y, buttonWidth, 30f), "Heavily Modded", true, false, true))
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
                    // Best for small colonies
                    settings.weaponUpgradeThreshold = 1.05f;
                    settings.autoEquipSidearms = true;
                    settings.allowSidearmUpgrades = true;
                    settings.checkCEAmmo = true;
                    break;

                case PerformancePreset.Balanced:
                    // Good for medium/large colonies
                    settings.weaponUpgradeThreshold = 1.10f;
                    settings.autoEquipSidearms = true;
                    settings.allowSidearmUpgrades = false;
                    settings.checkCEAmmo = true;
                    break;

                case PerformancePreset.Performance:
                    // Best performance for huge colonies/heavy mods
                    settings.weaponUpgradeThreshold = 1.15f;
                    settings.autoEquipSidearms = false;
                    settings.allowSidearmUpgrades = false;
                    settings.checkCEAmmo = false;
                    break;
            }
        }

        // Estimated performance display
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

        public override void DoSettingsWindowContents(Rect inRect)
        {
            var listing = new Listing_Standard();
            listing.Begin(inRect);

            // Draw small reset button in top right
            float buttonWidth = 100f;
            float buttonHeight = 30f;
            Rect resetButtonRect = new Rect(inRect.width - buttonWidth - 5f, 5f, buttonWidth, buttonHeight);

            Color oldColor = GUI.color;
            GUI.color = new Color(1f, 0.6f, 0.6f);
            if (Widgets.ButtonText(resetButtonRect, "Reset Config", true, false, true))
            {
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    "Are you sure you want to reset all settings to defaults?",
                    () => {
                        settings.ResetToDefaults();
                        Messages.Message("Settings reset to defaults", MessageTypeDefOf.NeutralEvent, false);
                    }));
            }
            GUI.color = oldColor;

            // Title with version info
            Text.Font = GameFont.Medium;
            listing.Label("AutoArm Settings v1.0");
            Text.Font = GameFont.Small;

            // Overall performance impact
            DrawPerformanceImpact(listing, "Overall Performance Impact", CalculateOverallImpact());
            listing.Gap(8f);

            // Preset buttons
            DrawPresetButtons(listing);
            listing.Gap(12f);

            // Tab buttons with color coding
            var tabRect = listing.GetRect(30f);
            var tabWidth = tabRect.width / 4f - 5f;

            // General tab (green - essential)
            GUI.color = currentTab == SettingsTab.General ? Color.white : new Color(0.7f, 1f, 0.7f);
            if (Widgets.ButtonText(new Rect(tabRect.x, tabRect.y, tabWidth, 30f), "General", true, false, true))
                currentTab = SettingsTab.General;

            // Compatibility tab (yellow - conditional)
            GUI.color = currentTab == SettingsTab.Compatibility ? Color.white : new Color(1f, 1f, 0.7f);
            if (Widgets.ButtonText(new Rect(tabRect.x + tabWidth + 5f, tabRect.y, tabWidth, 30f), "Compatibility", true, false, true))
                currentTab = SettingsTab.Compatibility;

            // Advanced tab (orange - performance impact)
            GUI.color = currentTab == SettingsTab.Advanced ? Color.white : new Color(1f, 0.9f, 0.6f);
            if (Widgets.ButtonText(new Rect(tabRect.x + (tabWidth + 5f) * 2, tabRect.y, tabWidth, 30f), "Advanced", true, false, true))
                currentTab = SettingsTab.Advanced;

            // Debug tab (red - high impact)
            GUI.color = currentTab == SettingsTab.Debug ? Color.white : new Color(1f, 0.7f, 0.7f);
            if (Widgets.ButtonText(new Rect(tabRect.x + (tabWidth + 5f) * 3, tabRect.y, tabWidth, 30f), "Debug", true, false, true))
                currentTab = SettingsTab.Debug;

            GUI.color = Color.white;
            listing.Gap(12f);

            // Content area
            var contentRect = listing.GetRect(inRect.height - listing.CurHeight - 30f);
            var innerRect = contentRect.ContractedBy(10f);

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
                    DrawDebugTab(innerListing, innerRect);
                    break;
            }

            innerListing.End();
            listing.End();
        }

        private void DrawGeneralTab(Listing_Standard listing)
        {
            // Status indicator with color
            Color statusColor = settings.modEnabled ? Color.green : Color.red;
            Color oldColor = GUI.color;
            GUI.color = statusColor;

            listing.Label($"Mod Status: {(settings.modEnabled ? "ACTIVE" : "DISABLED")}");
            GUI.color = oldColor;
            listing.Gap(12f);

            // Main settings
            DrawLeftAlignedCheckbox(listing, "Enable AutoArm", ref settings.modEnabled,
                "When enabled, colonists will automatically equip better weapons based on their outfit policy. When disabled, all automatic weapon management is turned off.");

            listing.Gap(12f);

            DrawLeftAlignedCheckbox(listing, "Show notifications", ref settings.showNotifications,
                "Shows blue notification messages when colonists equip or drop weapons. Disabling this will make weapon changes silent but they will still happen.");

            listing.Gap(20f);

            // Colony info
            if (Current.Game != null && Find.CurrentMap != null)
            {
                Text.Font = GameFont.Tiny;
                GUI.color = Color.gray;
                int colonistCount = Find.CurrentMap.mapPawns.FreeColonistsCount;
                listing.Label($"Current colony size: {colonistCount} colonists");
                listing.Label($"Recommended preset: {(colonistCount < 10 ? "Small Colony" : colonistCount < 20 ? "Large Colony" : "Heavily Modded")}");
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
            }
        }

        private void DrawCompatibilityTab(Listing_Standard listing)
        {
            Text.Font = GameFont.Medium;
            listing.Label("Detected Mods");
            Text.Font = GameFont.Small;
            listing.Gap(12f);

            // Color-coded mod detection
            bool ssLoaded = SimpleSidearmsCompat.IsLoaded();
            bool ceLoaded = CECompat.IsLoaded();
            bool infusionLoaded = InfusionCompat.IsLoaded();

            GUI.color = ssLoaded ? Color.green : Color.gray;
            listing.Label($"Simple Sidearms: {(ssLoaded ? "✓ Loaded" : "✗ Not found")}");

            GUI.color = ceLoaded ? Color.green : Color.gray;
            listing.Label($"Combat Extended: {(ceLoaded ? "✓ Loaded" : "✗ Not found")}");

            GUI.color = infusionLoaded ? Color.green : Color.gray;
            listing.Label($"Infusion 2: {(infusionLoaded ? "✓ Loaded" : "✗ Not found")}");

            GUI.color = Color.white;
            listing.Gap(20f);

            // Mod-specific settings
            if (ssLoaded)
            {
                Text.Font = GameFont.Medium;
                listing.Label("Simple Sidearms");
                Text.Font = GameFont.Small;

                DrawLeftAlignedCheckbox(listing, "Auto-equip sidearms", ref settings.autoEquipSidearms,
                    "Allows colonists to automatically pick up additional weapons as sidearms. Simple Sidearms must be installed and configured. This may impact performance with many colonists.");

                if (settings.autoEquipSidearms)
                {
                    listing.Gap(6f);
                    listing.Indent(20f);

                    Color oldColor = GUI.color;
                    GUI.color = new Color(1f, 1f, 0.6f); // Yellow for experimental
                    DrawLeftAlignedCheckbox(listing, "Allow sidearm upgrades - Experimental", ref settings.allowSidearmUpgrades,
                        "When enabled, colonists will upgrade existing sidearms to better weapons. When disabled, they will only fill empty sidearm slots.", 20f);
                    GUI.color = oldColor;

                    listing.Outdent(20f);

                    // Performance warning
                    if (settings.allowSidearmUpgrades)
                    {
                        GUI.color = new Color(1f, 0.8f, 0.4f);
                        listing.Label("⚠ Sidearm upgrades can impact performance in large colonies");
                        GUI.color = Color.white;
                    }
                }
            }

            if (ceLoaded)
            {
                listing.Gap(20f);
                Text.Font = GameFont.Medium;
                listing.Label("Combat Extended");
                Text.Font = GameFont.Small;

                DrawLeftAlignedCheckbox(listing, "Check for ammunition", ref settings.checkCEAmmo,
                    "When enabled, colonists will only pick up weapons if they have access to appropriate ammunition. Disabling this may result in colonists carrying weapons they cannot use. Only affects Combat Extended games.");
            }

            if (!ssLoaded && !ceLoaded && !infusionLoaded)
            {
                GUI.color = Color.gray;
                listing.Label("No compatible mods detected - using vanilla behavior");
                GUI.color = Color.white;
            }
        }

        private void DrawAdvancedTab(Listing_Standard listing)
        {
            Text.Font = GameFont.Medium;
            listing.Label("Weapon Selection");
            Text.Font = GameFont.Small;
            listing.Gap(12f);

            // Weapon upgrade threshold with performance indicator
            DrawSliderWithLabel(listing, "Weapon upgrade threshold", ref settings.weaponUpgradeThreshold,
                1.01f, 1.50f, "P0",
                "How much better a weapon needs to be before colonists will switch. Lower values mean more frequent switching.");

            // Show performance impact
            if (settings.weaponUpgradeThreshold < 1.10f)
            {
                GUI.color = new Color(1f, 0.8f, 0.4f);
                listing.Label("⚠ Low threshold may cause frequent weapon switching");
                GUI.color = Color.white;
            }

            listing.Gap(20f);

            // Age restrictions
            if (ModsConfig.BiotechActive)
            {
                Text.Font = GameFont.Medium;
                listing.Label("Age Restrictions");
                Text.Font = GameFont.Small;

                DrawLeftAlignedCheckbox(listing, "Allow children to equip weapons", ref settings.allowChildrenToEquipWeapons,
                    "When enabled, child colonists can pick up and use weapons. What could go wrong?");

                if (settings.allowChildrenToEquipWeapons)
                {
                    listing.Label($"Minimum age: {settings.childrenMinAge}");
                    settings.childrenMinAge = (int)listing.Slider(settings.childrenMinAge, 3, 18);

                    if (settings.childrenMinAge < 10)
                    {
                        GUI.color = new Color(1f, 0.8f, 0.4f);
                        listing.Label("⚠ Very young children with weapons may be controversial");
                        GUI.color = Color.white;
                    }
                }
            }

            // Nobility
            if (ModsConfig.RoyaltyActive)
            {
                listing.Gap(20f);
                Text.Font = GameFont.Medium;
                listing.Label("Nobility");
                Text.Font = GameFont.Small;

                DrawLeftAlignedCheckbox(listing, "Respect conceited nobles", ref settings.respectConceitedNobles,
                    "When enabled, conceited nobles won't automatically switch weapons.");
            }
        }

        private void DrawDebugTab(Listing_Standard listing, Rect contentRect)
        {
            // Warning header
            GUI.color = new Color(1f, 0.6f, 0.6f);
            listing.Label("⚠ Debug features can significantly impact performance!");
            GUI.color = Color.white;
            listing.Gap(12f);

            DrawLeftAlignedCheckbox(listing, "Enable debug logging", ref settings.debugLogging,
                "Outputs detailed information to the debug log for troubleshooting. This will create many log entries and may impact performance. Only enable when reporting issues.");

            if (settings.debugLogging)
            {
                DrawPerformanceImpact(listing, "Debug Logging Impact", PerformanceImpact.High);
            }

            if (settings.debugLogging && Current.Game != null && Find.CurrentMap != null)
            {
                listing.Gap(20f);
                Text.Font = GameFont.Medium;
                listing.Label("Testing Tools");
                Text.Font = GameFont.Small;

                if (listing.ButtonText("Test Weapon Detection", null, true))
                {
                    TestWeaponDetection();
                }

                if (listing.ButtonText("Test Pawn Validation", null, true))
                {
                    TestPawnValidation();
                }

                if (listing.ButtonText("Run All Tests (Outdated)", null, true))
                {
                    RunAllTests();
                }

                // Show test results if available
                if (lastTestResults != null && showTestResults)
                {
                    listing.Gap(10f);

                    var resultsRect = new Rect(contentRect.x, listing.CurHeight + 20f, contentRect.width, contentRect.height - listing.CurHeight - 30f);

                    Widgets.DrawBoxSolid(resultsRect, new Color(0.1f, 0.1f, 0.1f, 0.8f));

                    var innerResultsRect = resultsRect.ContractedBy(10f);
                    var textHeight = Text.CalcHeight(testResultsText, innerResultsRect.width);

                    Widgets.BeginScrollView(innerResultsRect, ref scrollPosition,
                        new Rect(0, 0, innerResultsRect.width - 20f, textHeight));

                    GUI.color = testResultsText.Contains("FAILED") ? new Color(1f, 0.8f, 0.8f) : new Color(0.8f, 1f, 0.8f);
                    Widgets.Label(new Rect(0, 0, innerResultsRect.width - 20f, textHeight), testResultsText);
                    GUI.color = Color.white;

                    Widgets.EndScrollView();
                }
            }
            else if (settings.debugLogging)
            {
                listing.Gap(20f);
                GUI.color = Color.gray;
                listing.Label("Testing tools require an active game");
                GUI.color = Color.white;
            }
        }

        // Test methods
        private void TestWeaponDetection()
        {
            var map = Find.CurrentMap;
            if (map == null) return;

            var weapons = map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon)
                .OfType<ThingWithComps>()
                .Take(10)
                .ToList();

            Log.Message($"[AutoArm] Found {weapons.Count} weapons on map:");
            foreach (var weapon in weapons)
            {
                Log.Message($"  - {weapon.Label} ({weapon.def.defName})");
            }

            Messages.Message($"Found {weapons.Count} weapons. Check log for details.", MessageTypeDefOf.NeutralEvent, false);
        }

        private void TestPawnValidation()
        {
            var map = Find.CurrentMap;
            if (map == null) return;

            var colonists = map.mapPawns.FreeColonists.ToList();
            int validCount = 0;

            Log.Message($"[AutoArm] Testing {colonists.Count} colonists:");
            foreach (var pawn in colonists)
            {
                string reason;
                bool isValid = JobGiverHelpers.IsValidPawnForAutoEquip(pawn, out reason);

                if (isValid)
                {
                    validCount++;
                    Log.Message($"  - {pawn.Name}: Valid");
                }
                else
                {
                    Log.Message($"  - {pawn.Name}: Invalid - {reason}");
                }
            }

            Messages.Message($"{validCount}/{colonists.Count} colonists valid for auto-equip. Check log for details.",
                MessageTypeDefOf.NeutralEvent, false);
        }

        private void RunAllTests()
        {
            testResultsText = "Running tests...\n";
            showTestResults = true;

            try
            {
                var results = TestRunner.RunAllTests(Find.CurrentMap);
                lastTestResults = results;

                testResultsText = "";
                foreach (var test in TestRunner.GetAllTests())
                {
                    var result = results.GetResult(test.Name);
                    if (result != null)
                    {
                        testResultsText += $"{test.Name}: {(result.Success ? "PASSED" : "FAILED")}\n";
                        if (!result.Success)
                        {
                            testResultsText += $"  Reason: {result.FailureReason}\n";
                        }
                    }
                }

                testResultsText += $"\n=== Summary ===\n";
                testResultsText += $"Total: {results.TotalTests}\n";
                testResultsText += $"Passed: {results.PassedTests}\n";
                testResultsText += $"Failed: {results.FailedTests}\n";
                testResultsText += $"Success Rate: {results.SuccessRate:P0}\n";

                string msg = $"Tests complete: {results.PassedTests}/{results.TotalTests} passed";
                MessageTypeDef messageType = results.FailedTests > 0 ?
                    MessageTypeDefOf.NegativeEvent : MessageTypeDefOf.PositiveEvent;
                Messages.Message(msg, messageType, false);
            }
            catch (System.Exception e)
            {
                testResultsText += $"\nTest runner error: {e.Message}";
                Log.Error($"[AutoArm] Test runner error: {e}");
            }
        }

        public override string SettingsCategory()
        {
            return "AutoArm";
        }
    }
}