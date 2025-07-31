using UnityEngine;
using Verse;
using RimWorld;
using System.Linq;
using System.Collections.Generic;
using System;
using AutoArm.Testing;

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

        private AutoArmDebugWindow debugWindow;

        private enum SettingsTab
        {
            General,
            Compatibility,
            Advanced,
            Debug
        }

        public AutoArmMod(ModContentPack content) : base(content)
        {
            settings = GetSettings<AutoArmSettings>();
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

            Text.Font = GameFont.Medium;
            var titleRect = listing.GetRect(Text.LineHeight);
            Widgets.Label(titleRect, "Settings");
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
            GUI.color = new Color(1f, 0.6f, 0.6f);

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
                currentTab = SettingsTab.Debug;

            GUI.color = originalColor;
            listing.Gap(SMALL_GAP);
        }

        private void DrawTabContent(Rect contentRect)
        {
            var innerRect = contentRect.ContractedBy(CONTENT_PADDING);

            Widgets.DrawBoxSolid(contentRect, new Color(0.1f, 0.1f, 0.1f, 0.3f));

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
                    DrawDebugTab(innerListing);
                    break;
            }

            innerListing.End();
        }

        private void DrawCheckbox(Listing_Standard listing, string label, ref bool value, string tooltip = null, float indent = 0f)
        {
            Rect fullRect = listing.GetRect(LINE_HEIGHT);
            Rect labelRect = fullRect;

            Rect checkRect = new Rect(
                fullRect.x + indent,
                fullRect.y + (LINE_HEIGHT - CHECKBOX_SIZE) / 2f,
                CHECKBOX_SIZE,
                CHECKBOX_SIZE
            );

            labelRect.x += CHECKBOX_SIZE + 5f + indent;
            labelRect.width -= CHECKBOX_SIZE + 5f + indent;

            bool oldValue = value;
            Widgets.Checkbox(checkRect.x, checkRect.y, ref value, CHECKBOX_SIZE);

            Widgets.Label(labelRect, label);

            if (tooltip != null && Mouse.IsOver(fullRect))
            {
                TooltipHandler.TipRegion(fullRect, tooltip);
            }

            if (oldValue != value)
            {
                AutoArmDebug.Log($"Setting changed: {label} = {value}");
            }
        }

        private void DrawSlider(Listing_Standard listing, string label, ref float value, float min, float max, string format = "P0", string tooltip = null, bool isPercentageBetter = false)
        {
            Rect rect = listing.GetRect(LINE_HEIGHT);
            Rect labelRect = rect.LeftPart(0.7f);
            Rect sliderRect = rect.RightPart(0.3f);

            string displayValue;
            if (isPercentageBetter)
            {
                // Convert from multiplier (1.05) to percentage improvement (5%)
                float percentBetter = (value - 1f) * 100f;
                displayValue = $"{percentBetter:F0}% Better";
            }
            else
            {
                displayValue = format == "P0" ? value.ToString("P0") : value.ToString(format);
            }

            Widgets.Label(labelRect, $"{label}: {displayValue}");

            float oldValue = value;
            value = Widgets.HorizontalSlider(sliderRect, value, min, max);

            value = Mathf.Clamp(value, min, max);

            if (tooltip != null && Mouse.IsOver(rect))
            {
                TooltipHandler.TipRegion(rect, tooltip);
            }

            if (Math.Abs(oldValue - value) > 0.01f)
            {
                AutoArmDebug.Log($"Setting changed: {label} = {value:F2}");
            }
        }

        private void DrawGeneralTab(Listing_Standard listing)
        {
            DrawCheckbox(listing, "Enable Auto Arm", ref settings.modEnabled,
                "When enabled, colonists will automatically equip better weapons based on their outfit policy. Turn this off if you just want the weapon filter.");

            listing.Gap(SMALL_GAP);

            DrawCheckbox(listing, "Show notifications", ref settings.showNotifications,
                "Shows blue notification messages when colonists equip or drop weapons.");

            listing.Gap(SMALL_GAP);

            DrawCheckbox(listing, "Allow forced weapon type upgrades", ref settings.allowForcedWeaponUpgrades,
                "When enabled, colonists can upgrade forced weapons to better quality versions of the same type (e.g., normal revolver → masterwork revolver).");

            listing.Gap(SMALL_GAP);

            DrawCheckbox(listing, "Allow temporary colonists to auto-equip", ref settings.allowTemporaryColonists,
                "When enabled, temporary colonists (quest lodgers, borrowed pawns, royal guests) can auto-equip weapons. When disabled, they keep their current equipment.");

            if (settings.allowTemporaryColonists)
            {
                Color oldColor = GUI.color;
                GUI.color = new Color(1f, 0.8f, 0.4f);
                var warningRect = listing.GetRect(Text.LineHeight);
                Widgets.Label(warningRect, "⚠ Quest colonists may leave with your weapons!");
                GUI.color = oldColor;
            }

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
            Widgets.Label(rect, $"{modName}: {(isLoaded ? "✓ Loaded" : "✗ Not found")}");
            GUI.color = oldColor;
        }

        private void DrawSimpleSidearmsSettings(Listing_Standard listing)
        {
            if (!SimpleSidearmsCompat.IsLoaded()) return;

            Text.Font = GameFont.Medium;
            var headerRect = listing.GetRect(Text.LineHeight);
            Widgets.Label(headerRect, "Simple Sidearms");
            Text.Font = GameFont.Small;

            DrawCheckbox(listing, "Auto-equip sidearms", ref settings.autoEquipSidearms,
                "Allows colonists to automatically pick up additional weapons as sidearms.");

            if (settings.autoEquipSidearms)
            {
                listing.Gap(TINY_GAP);
                listing.Indent(20f);

                Color oldColor = GUI.color;
                GUI.color = new Color(1f, 1f, 0.6f);
                DrawCheckbox(listing, "Allow sidearm upgrades - Experimental",
                    ref settings.allowSidearmUpgrades,
                    "When enabled, colonists will upgrade existing sidearms to better weapons.",
                    20f);
                GUI.color = oldColor;

                listing.Outdent(20f);

                if (settings.allowSidearmUpgrades)
                {
                    oldColor = GUI.color;
                    GUI.color = new Color(1f, 0.8f, 0.4f);
                    var warningRect = listing.GetRect(Text.LineHeight);
                    Widgets.Label(warningRect, "⚠ Sidearm upgrades can impact performance in large colonies");
                    GUI.color = oldColor;
                }
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
                AutoArmDebug.Log("CE ammo system is disabled - forcing ammo checks off");
            }
            // Auto-enable only when CE ammo system is turned on (state change from off to on)
            else if (ceAmmoSystemEnabled && !settings.lastKnownCEAmmoState && stateChanged)
            {
                settings.checkCEAmmo = true;
                AutoArmDebug.Log("CE ammo system was enabled - auto-enabling ammo checks");
            }

            // Update the tracked state
            settings.lastKnownCEAmmoState = ceAmmoSystemEnabled;

            // Show the checkbox (disabled if CE ammo is off)
            Color oldColor = GUI.color;
            if (!ceAmmoSystemEnabled)
            {
                GUI.color = Color.gray;
            }

            DrawCheckbox(listing, "Check for ammunition", ref settings.checkCEAmmo,
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
                Widgets.Label(warningRect, "⚠ CE ammo system is disabled");
                GUI.color = oldColor;
            }
        }

        private void DrawAdvancedTab(Listing_Standard listing)
        {
            Text.Font = GameFont.Medium;
            var headerRect = listing.GetRect(Text.LineHeight);
            Widgets.Label(headerRect, "Weapon Selection");
            Text.Font = GameFont.Small;
            listing.Gap(SMALL_GAP);

            DrawSlider(listing, "Weapon upgrade threshold", ref settings.weaponUpgradeThreshold,
                THRESHOLD_MIN, THRESHOLD_MAX, "P0",
                "How much better a weapon needs to be before colonists will switch. Lower values mean more frequent switching.",
                isPercentageBetter: true);

            if (settings.weaponUpgradeThreshold < 1.10f)
            {
                Color oldColor = GUI.color;
                GUI.color = new Color(1f, 0.8f, 0.4f);
                var warningRect = listing.GetRect(Text.LineHeight);
                Widgets.Label(warningRect, "⚠ Low threshold may cause frequent weapon switching");
                GUI.color = oldColor;
            }

            listing.Gap(SECTION_GAP);

            DrawAgeRestrictions(listing);
            DrawNobilitySettings(listing);
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
                var labelRect = listing.GetRect(Text.LineHeight);
                Widgets.Label(labelRect, $"Minimum age: {settings.childrenMinAge}");

                // Make the slider only 1/3 width
                var sliderRect = listing.GetRect(LINE_HEIGHT);
                var shortSliderRect = sliderRect.LeftPart(0.33f);  // Only use left third

                float tempAge = (float)settings.childrenMinAge;
                tempAge = Widgets.HorizontalSlider(shortSliderRect, tempAge, (float)CHILD_AGE_MIN, (float)CHILD_AGE_MAX);
                settings.childrenMinAge = Mathf.RoundToInt(tempAge);

                if (settings.childrenMinAge < 10)
                {
                    Color oldColor = GUI.color;
                    GUI.color = new Color(1f, 0.8f, 0.4f);
                    var warningRect = listing.GetRect(Text.LineHeight);
                    Widgets.Label(warningRect, "⚠ What could go wrong?");
                    GUI.color = oldColor;
                }
            }
        }

        private void DrawNobilitySettings(Listing_Standard listing)
        {
            if (!ModsConfig.RoyaltyActive) return;

            listing.Gap(SECTION_GAP);
            Text.Font = GameFont.Medium;
            var headerRect = listing.GetRect(Text.LineHeight);
            Widgets.Label(headerRect, "Nobility");
            Text.Font = GameFont.Small;

            DrawCheckbox(listing, "Respect conceited nobles", ref settings.respectConceitedNobles,
                "When enabled, conceited nobles won't automatically switch weapons.");
        }

        private void DrawDebugTab(Listing_Standard listing)
        {
            Color oldColor = GUI.color;
            GUI.color = new Color(1f, 0.6f, 0.6f);
            var warningRect = listing.GetRect(Text.LineHeight);
            Widgets.Label(warningRect, "⚠ Save before touching anything");
            GUI.color = oldColor;

            listing.Gap(SMALL_GAP);

            DrawCheckbox(listing, "Enable debug logging", ref settings.debugLogging,
                "Outputs detailed information to the debug log for troubleshooting.");

            if (Current.Game?.CurrentMap != null)
            {
                listing.Gap(SECTION_GAP);
                Text.Font = GameFont.Medium;
                var headerRect = listing.GetRect(Text.LineHeight);
                Widgets.Label(headerRect, "Testing Tools");
                Text.Font = GameFont.Small;

                if (listing.ButtonText("Open Debug Window"))
                {
                    OpenDebugWindow();
                }
            }
            else
            {
                listing.Gap(SECTION_GAP);
                oldColor = GUI.color;
                GUI.color = Color.gray;
                var inactiveRect = listing.GetRect(Text.LineHeight);
                Widgets.Label(inactiveRect, "Testing tools require an active game");
                GUI.color = oldColor;
            }
        }

        private void OpenDebugWindow()
        {
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

        public override void WriteSettings()
        {
            ValidateSettings();
            base.WriteSettings();
        }

        private void ValidateSettings()
        {
            settings.weaponUpgradeThreshold = Mathf.Clamp(settings.weaponUpgradeThreshold, THRESHOLD_MIN, THRESHOLD_MAX);
            settings.childrenMinAge = Mathf.Clamp(settings.childrenMinAge, CHILD_AGE_MIN, CHILD_AGE_MAX);

            AutoArmDebug.Log($"Settings validated - Threshold: {settings.weaponUpgradeThreshold:F2}, Child Age: {settings.childrenMinAge}, Allow Temp Colonists: {settings.allowTemporaryColonists}");
        }

        public override string SettingsCategory()
        {
            return "AutoArm";
        }
    }

    public class AutoArmDebugWindow : Window
    {
        private Vector2 scrollPosition;
        private string testResultsText = "";
        private TestResults lastTestResults = null;

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
            // Center the window on screen
            Vector2 size = InitialSize;
            float x = (UI.screenWidth - size.x) / 2f;
            float y = (UI.screenHeight - size.y) / 2f;
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
                else if (Event.current.keyCode == KeyCode.F6)  // F6 to clear results
                {
                    ClearTestResults();
                    Event.current.Use();
                }
            }

            var listing = new Listing_Standard();
            listing.Begin(inRect);

            Text.Font = GameFont.Medium;
            var headerRect = listing.GetRect(Text.LineHeight);
            Widgets.Label(headerRect, "AutoArm Debug Tools");
            Text.Font = GameFont.Small;

            listing.Gap(10f);

            // Show keyboard shortcuts
            Color oldColor = GUI.color;
            GUI.color = Color.gray;
            var shortcutRect = listing.GetRect(Text.LineHeight);
            Widgets.Label(shortcutRect, "Shortcuts: F5=Run Tests, F6=Clear");
            GUI.color = oldColor;

            listing.Gap(10f);

            // Add validator button
            if (listing.ButtonText("Validate Test System"))
            {
                Testing.TestValidator.ValidateTests();
                testResultsText = "Test system validation complete. Check the debug log for results.";
            }

            if (listing.ButtonText("Test Weapon Detection"))
            {
                TestWeaponDetection();
            }

            if (listing.ButtonText("Test Pawn Validation"))
            {
                TestPawnValidation();
            }

            // New button for running all tests
            GUI.color = new Color(0.6f, 0.8f, 1f);  // Light blue
            if (listing.ButtonText("Run All AutoArm Tests"))
            {
                RunAllTests();
            }
            GUI.color = Color.white;

            if (listing.ButtonText("Clear Test Results"))
            {
                ClearTestResults();
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
                lastTestResults = results;

                // Build result text
                testResultsText = "=== AutoArm Test Results ===\n\n";
                testResultsText += $"Total Tests: {results.TotalTests}\n";
                testResultsText += $"Passed: {results.PassedTests}\n";
                testResultsText += $"Failed: {results.FailedTests}\n";
                testResultsText += $"Success Rate: {results.SuccessRate:P0}\n\n";

                // Show all test results
                var allResults = results.GetAllResults();
                testResultsText += "Test Details:\n";
                testResultsText += "──────────────\n";

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
                        foreach (var data in kvp.Value.Data)
                        {
                            testResultsText += $"   └─ {data.Key}: {data.Value}\n";
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
            lastTestResults = null;
        }
    }
}