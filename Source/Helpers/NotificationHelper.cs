// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: User notification system for weapon changes
// Handles: Blue notification messages when colonists equip/drop weapons
// Uses: RimWorld's message system with proper colonist filtering
// Note: Respects user settings and PawnUtility.ShouldSendNotificationAbout

using RimWorld;
using Verse;
using AutoArm.Logging;

namespace AutoArm.Helpers
{
    /// <summary>
    /// Centralized notification handling (fixes #25)
    /// </summary>
    public static class NotificationHelper
    {
        /// <summary>
        /// Send a notification if settings allow and pawn should be notified about
        /// </summary>
        public static void SendNotification(string translationKey, Pawn pawn, params NamedArgument[] args)
        {
            if (!ShouldSendNotification(pawn))
                return;

            var message = translationKey.Translate(args);
            Messages.Message(message, new LookTargets(pawn), MessageTypeDefOf.SilentInput, false);
            
            if (AutoArmMod.settings?.debugLogging == true)
            {
                AutoArmLogger.Debug($"Notification sent: {message}");
            }
        }

        /// <summary>
        /// Check if notification should be sent
        /// </summary>
        public static bool ShouldSendNotification(Pawn pawn)
        {
            return AutoArmMod.settings?.showNotifications == true &&
                   PawnUtility.ShouldSendNotificationAbout(pawn);
        }

        /// <summary>
        /// Send weapon equipped notification
        /// </summary>
        public static void NotifyWeaponEquipped(Pawn pawn, ThingWithComps newWeapon, ThingDef previousWeapon = null)
        {
            if (!ShouldSendNotification(pawn))
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug($"Weapon equip notification suppressed for {pawn?.LabelShort ?? "null"}");
                }
                return;
            }

            string newWeaponLabel = newWeapon?.Label ?? newWeapon?.def?.label ?? "weapon";

            if (previousWeapon != null)
            {
                string previousLabel = previousWeapon.label ?? "previous weapon";
                SendNotification("AutoArm_UpgradedWeapon", pawn,
                    pawn.LabelShort.CapitalizeFirst().Named("PAWN"),
                    previousLabel.Named("OLD"),
                    newWeaponLabel.Named("NEW"));
                    
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug($"{pawn.LabelShort} upgraded weapon: {previousLabel} -> {newWeaponLabel}");
                }
            }
            else
            {
                SendNotification("AutoArm_EquippedWeapon", pawn,
                    pawn.LabelShort.CapitalizeFirst().Named("PAWN"),
                    newWeaponLabel.Named("WEAPON"));
                    
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug($"{pawn.LabelShort} equipped weapon: {newWeaponLabel}");
                }
            }
        }

        /// <summary>
        /// Send sidearm equipped notification
        /// </summary>
        public static void NotifySidearmEquipped(Pawn pawn, ThingWithComps newSidearm, ThingWithComps oldSidearm = null)
        {
            if (!ShouldSendNotification(pawn))
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug($"Sidearm equip notification suppressed for {pawn?.LabelShort ?? "null"}");
                }
                return;
            }

            string newLabel = newSidearm?.Label ?? newSidearm?.def?.label ?? "sidearm";

            if (oldSidearm != null)
            {
                string oldLabel = oldSidearm.Label ?? oldSidearm.def?.label ?? "old sidearm";
                SendNotification("AutoArm_UpgradedSidearm", pawn,
                    pawn.LabelShort.CapitalizeFirst().Named("PAWN"),
                    oldLabel.Named("OLD"),
                    newLabel.Named("NEW"));
                    
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug($"{pawn.LabelShort} upgraded sidearm: {oldLabel} -> {newLabel}");
                }
            }
            else
            {
                SendNotification("AutoArm_EquippedSidearm", pawn,
                    pawn.LabelShort.CapitalizeFirst().Named("PAWN"),
                    newLabel.Named("SIDEARM"));
                    
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug($"{pawn.LabelShort} equipped sidearm: {newLabel}");
                }
            }
        }

        /// <summary>
        /// Send weapon dropped notification (deprecated - intentionally does nothing)
        /// </summary>
        [System.Obsolete("Weapon drop notifications are disabled by design")]
        public static void NotifyWeaponDropped(Pawn pawn, ThingWithComps weapon, string reason = null)
        {
            // Intentionally empty - AutoArm doesn't notify on drops to avoid spam
        }

        /// <summary>
        /// Send a generic AutoArm notification
        /// </summary>
        public static void SendGenericNotification(Pawn pawn, string message)
        {
            if (!ShouldSendNotification(pawn))
                return;

            Messages.Message($"[AutoArm] {pawn.LabelShort}: {message}",
                new LookTargets(pawn), MessageTypeDefOf.SilentInput, false);
        }
    }
}