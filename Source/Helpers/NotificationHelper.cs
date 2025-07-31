using RimWorld;
using Verse;

namespace AutoArm
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
                return;

            string newWeaponLabel = newWeapon?.Label ?? newWeapon?.def?.label ?? "weapon";

            if (previousWeapon != null)
            {
                string previousLabel = previousWeapon.label ?? "previous weapon";
                SendNotification("AutoArm_UpgradedWeapon", pawn,
                    pawn.LabelShort.CapitalizeFirst().Named("PAWN"),
                    previousLabel.Named("OLD"),
                    newWeaponLabel.Named("NEW"));
            }
            else
            {
                SendNotification("AutoArm_EquippedWeapon", pawn,
                    pawn.LabelShort.CapitalizeFirst().Named("PAWN"),
                    newWeaponLabel.Named("WEAPON"));
            }
        }

        /// <summary>
        /// Send sidearm equipped notification
        /// </summary>
        public static void NotifySidearmEquipped(Pawn pawn, ThingWithComps newSidearm, ThingWithComps oldSidearm = null)
        {
            if (!ShouldSendNotification(pawn))
                return;

            string newLabel = newSidearm?.Label ?? newSidearm?.def?.label ?? "sidearm";

            if (oldSidearm != null)
            {
                string oldLabel = oldSidearm.Label ?? oldSidearm.def?.label ?? "old sidearm";
                SendNotification("AutoArm_UpgradedSidearm", pawn,
                    pawn.LabelShort.CapitalizeFirst().Named("PAWN"),
                    oldLabel.Named("OLD"),
                    newLabel.Named("NEW"));
            }
            else
            {
                SendNotification("AutoArm_EquippedSidearm", pawn,
                    pawn.LabelShort.CapitalizeFirst().Named("PAWN"),
                    newLabel.Named("SIDEARM"));
            }
        }

        /// <summary>
        /// Send weapon dropped notification
        /// </summary>
        public static void NotifyWeaponDropped(Pawn pawn, ThingWithComps weapon, string reason = null)
        {
            // Weapon drop notifications are disabled - we don't show notifications when items are dropped
            return;
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