using UnityEngine;
using Verse;
using RimWorld;
using System.Linq;

namespace AutoArm
{
    public class AutoArmSettings : ModSettings
    {
        public bool modEnabled = true;
        public bool debugLogging = false;
        public bool autoEquipSidearms = true;
        public bool checkCEAmmo = true;
        public bool thinkTreeInjectionFailed = false;
        public bool showNotifications = true;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref modEnabled, "modEnabled", true);
            Scribe_Values.Look(ref debugLogging, "debugLogging", false);
            Scribe_Values.Look(ref autoEquipSidearms, "autoEquipSidearms", true);
            Scribe_Values.Look(ref checkCEAmmo, "checkCEAmmo", true);
            Scribe_Values.Look(ref thinkTreeInjectionFailed, "thinkTreeInjectionFailed", false);
            Scribe_Values.Look(ref showNotifications, "showNotifications", true);
            base.ExposeData();
        }

        public void ResetToDefaults()
        {
            modEnabled = true;
            debugLogging = false;
            autoEquipSidearms = true;
            checkCEAmmo = true;
            showNotifications = true;
            // Note: We don't reset thinkTreeInjectionFailed as it's a runtime state
        }
    }

    public class AutoArmMod : Mod
    {
        public static AutoArmSettings settings;

        public AutoArmMod(ModContentPack content) : base(content)
        {
            settings = GetSettings<AutoArmSettings>();
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard listingStandard = new Listing_Standard();
            listingStandard.Begin(inRect);

            // Main enable/disable
            listingStandard.CheckboxLabeled("AutoArm_EnableMod".Translate(), ref settings.modEnabled,
                "AutoArm_EnableModDesc".Translate());

            listingStandard.Gap(12f);

            // Notifications setting
            listingStandard.CheckboxLabeled("AutoArm_ShowNotifications".Translate(), ref settings.showNotifications,
                "AutoArm_ShowNotificationsDesc".Translate());

            // Show Simple Sidearms option if it's loaded
            if (SimpleSidearmsCompat.IsLoaded())
            {
                listingStandard.Gap(12f);
                listingStandard.CheckboxLabeled("AutoArm_EnableSidearmAutoEquip".Translate(), ref settings.autoEquipSidearms,
                    "AutoArm_EnableSidearmAutoEquipDesc".Translate());
            }

            // Show Combat Extended option if it's loaded
            if (CECompat.IsLoaded())
            {
                listingStandard.Gap(12f);
                listingStandard.CheckboxLabeled("AutoArm_CheckCEAmmo".Translate(), ref settings.checkCEAmmo,
                    "AutoArm_CheckCEAmmoDesc".Translate());
            }

            listingStandard.Gap(20f);

            // Detected mods section
            listingStandard.Label("AutoArm_DetectedMods".Translate() + ":");
            string detectedMods = "";

            if (SimpleSidearmsCompat.IsLoaded())
                detectedMods += "• Simple Sidearms\n";
            if (CECompat.IsLoaded())
                detectedMods += "• Combat Extended\n";
            if (InfusionCompat.IsLoaded())
                detectedMods += "• Infusion 2\n";

            if (string.IsNullOrEmpty(detectedMods))
                detectedMods = "AutoArm_NoCompatModsDetected".Translate();

            listingStandard.Label(detectedMods);

            listingStandard.Gap(20f);

            // Reset button
            if (listingStandard.ButtonText("AutoArm_ResetToDefaults".Translate()))
            {
                settings.ResetToDefaults();
                Messages.Message("AutoArm_SettingsReset".Translate(), MessageTypeDefOf.TaskCompletion);
            }

            listingStandard.Gap(20f);

            // Debug logging at the bottom
            listingStandard.CheckboxLabeled("AutoArm_EnableDebugLogging".Translate(), ref settings.debugLogging,
                "AutoArm_EnableDebugLoggingDesc".Translate());

            // Debug info when debug logging is enabled
            if (settings.debugLogging)
            {
                listingStandard.Gap(12f);
                listingStandard.Label("Debug Info:");
                listingStandard.Label($"  Think tree injection: {(settings.thinkTreeInjectionFailed ? "FAILED - using fallback" : "OK")}");

                // Show what Simple Sidearms mods are installed
                var sidearmsMods = ModLister.AllInstalledMods.Where(m =>
                    m.PackageIdPlayerFacing.ToLower().Contains("sidearms") ||
                    m.Name.ToLower().Contains("sidearm")).ToList();
                if (sidearmsMods.Any())
                {
                    listingStandard.Label($"  Found {sidearmsMods.Count} sidearms-related mod(s):");
                    foreach (var mod in sidearmsMods.Take(3))
                    {
                        listingStandard.Label($"    - {mod.Name} ({mod.PackageIdPlayerFacing})");
                    }
                }

                // Test Simple Sidearms initialization
                if (SimpleSidearmsCompat.IsLoaded())
                {
                    listingStandard.Gap(6f);
                    if (listingStandard.ButtonText("Test Simple Sidearms Initialization"))
                    {
                        // Force initialization by calling a method that uses it
                        var testPawn = Find.CurrentMap?.mapPawns?.FreeColonists?.FirstOrDefault();
                        if (testPawn != null)
                        {
                            var job = SimpleSidearmsCompat.TryGetSidearmUpgradeJob(testPawn);
                            Log.Message($"[AutoArm] Test result for {testPawn.Name}: {(job != null ? "Found job" : "No job found")}");
                        }
                        else
                        {
                            Log.Message("[AutoArm] No colonist found for testing");
                        }
                    }
                }
            }

            listingStandard.End();
            base.DoSettingsWindowContents(inRect);
        }

        public override string SettingsCategory()
        {
            return "AutoArm_SettingsCategory".Translate();
        }
    }
}