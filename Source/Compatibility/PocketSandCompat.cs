// AutoArm – Pocket Sand compatibility (rev2, C# 7.3 safe, RimWorld 1.5–1.6)
// Place in: Source/Compatibility/PocketSandCompat.cs
// ------------------------------------------------------------------
// Adds a "pickup-then-equip" follow-up: when we queue TakeInventory for a
// ground weapon, we record a pending equip. On later ticks, call
// TryEquipPending(pawn, ...) to equip it from inventory without a job.
//
// No C# 8 features; uses AddEquipment (vanilla).
// ------------------------------------------------------------------
using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace AutoArm.Compat
{
    public static class PocketSandCompat
    {
        private static bool _resolved;
        private static bool _active;

        // pawn -> thingIDNumber of the weapon we intend to equip after pickup
        private static readonly Dictionary<Pawn, int> _pendingEquip = new Dictionary<Pawn, int>(64);

        public static bool Active
        {
            get
            {
                if (_resolved) return _active;
                _resolved = true;
                try
                {
                    foreach (var m in ModsConfig.ActiveModsInLoadOrder)
                    {
                        string name = m.Name ?? string.Empty;
                        string pkg = name; // fallback

                        // Try multiple properties to be safe across versions
                        var t = m.GetType();
                        try
                        {
                            var pLower = t.GetProperty("PackageIdLowerCase");
                            if (pLower != null) pkg = pLower.GetValue(m, null) as string;
                        }
                        catch { }
                        if (string.IsNullOrEmpty(pkg))
                        {
                            try
                            {
                                var p = t.GetProperty("PackageId");
                                if (p != null) pkg = p.GetValue(m, null) as string;
                            }
                            catch { }
                        }
                        if (string.IsNullOrEmpty(pkg))
                        {
                            try
                            {
                                var pFace = t.GetProperty("PackageIdPlayerFacing");
                                if (pFace != null) pkg = pFace.GetValue(m, null) as string;
                            }
                            catch { }
                        }

                        if ((name.IndexOf("Pocket Sand", StringComparison.OrdinalIgnoreCase) >= 0) ||
                            (!string.IsNullOrEmpty(pkg) && pkg.IndexOf("pocketsand", StringComparison.OrdinalIgnoreCase) >= 0))
                        {
                            _active = true;
                            break;
                        }
                    }
                }
                catch
                {
                    _active = false;
                }
                return _active;
            }
        }

        /// <summary>
        /// Attempt to handle equip now. If weapon is on ground, queue TakeInventory and
        /// remember to equip later (call TryEquipPending in your JobGiver).
        /// If weapon is in pawn's inventory, equip immediately (no job).
        /// Returns true if we queued or equipped; false if we did nothing.
        /// </summary>
        public static bool TryHandleEquip(Pawn pawn, ThingWithComps weapon, bool stashCurrentToInv)
        {
            if (!Active || pawn == null || weapon == null || pawn.Dead) return false;

            // 1) Ground -> queue pickup, then mark pending equip
            if (weapon.Spawned)
            {
                Job take = JobMaker.MakeJob(JobDefOf.TakeInventory, weapon);
                take.count = 1;
                take.playerForced = true;

                bool queued = pawn.jobs.TryTakeOrderedJob(take);
                if (queued)
                {
                    _pendingEquip[pawn] = weapon.thingIDNumber;
                }
                return queued;
            }

            // 2) Already in this pawn's inventory -> equip immediately
            if (pawn.inventory != null && pawn.inventory.innerContainer != null &&
                weapon.holdingOwner == pawn.inventory.innerContainer)
            {
                EquipFromInventory(pawn, weapon, stashCurrentToInv);
                // Clear any pending entry that matches this weapon
                int id;
                if (_pendingEquip.TryGetValue(pawn, out id) && id == weapon.thingIDNumber)
                    _pendingEquip.Remove(pawn);
                return true;
            }

            return false;
        }

        /// <summary>
        /// If we previously queued a TakeInventory for this pawn, try to finish the equip
        /// from inventory when the item arrives. Returns true if we equipped or if
        /// it turns out the pawn already has it equipped (clears pending).
        /// </summary>
        public static bool TryEquipPending(Pawn pawn, bool stashCurrentToInv)
        {
            if (pawn == null || pawn.Dead) return false;

            int id;
            if (!_pendingEquip.TryGetValue(pawn, out id)) return false;

            // If already equipped, clear and report handled
            if (pawn.equipment != null && pawn.equipment.Primary != null &&
                pawn.equipment.Primary.thingIDNumber == id)
            {
                _pendingEquip.Remove(pawn);
                return true;
            }

            // Look for the weapon in inventory
            if (pawn.inventory != null && pawn.inventory.innerContainer != null)
            {
                for (int i = 0; i < pawn.inventory.innerContainer.Count; i++)
                {
                    Thing t = pawn.inventory.innerContainer[i];
                    ThingWithComps twc = t as ThingWithComps;
                    if (twc != null && twc.thingIDNumber == id)
                    {
                        EquipFromInventory(pawn, twc, stashCurrentToInv);
                        _pendingEquip.Remove(pawn);
                        return true;
                    }
                }
            }

            // Not found (yet) — keep pending
            return false;
        }

        private static void EquipFromInventory(Pawn pawn, ThingWithComps weapon, bool stashCurrentToInv)
        {
            if (stashCurrentToInv && pawn.equipment != null && pawn.equipment.Primary != null)
            {
                ThingWithComps cur = pawn.equipment.Primary;
                pawn.equipment.Remove(cur);
                if (pawn.inventory != null && pawn.inventory.innerContainer != null && !pawn.inventory.innerContainer.Contains(cur))
                    pawn.inventory.innerContainer.TryAdd(cur, true);
            }

            if (pawn.inventory != null && pawn.inventory.innerContainer != null)
                pawn.inventory.innerContainer.Remove(weapon);

            if (pawn.equipment != null)
                pawn.equipment.AddEquipment(weapon);
        }

        /// <summary>
        /// Optional: call when pawn dies/despawns to avoid stale entries.
        /// </summary>
        public static void ClearPending(Pawn pawn)
        {
            if (pawn != null) _pendingEquip.Remove(pawn);
        }
    }
}