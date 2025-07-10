using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Verse;


public static class ThreadSafeForcedWeaponTracker
{
    private static readonly ReaderWriterLockSlim weaponLock = new ReaderWriterLockSlim();
    private static readonly Dictionary<Pawn, ThingDef> forcedWeaponsByDef = new Dictionary<Pawn, ThingDef>();

    private static readonly ReaderWriterLockSlim sidearmLock = new ReaderWriterLockSlim();
    private static readonly Dictionary<Pawn, HashSet<ThingDef>> forcedSidearmsByDef = new Dictionary<Pawn, HashSet<ThingDef>>();

    public static void SetForced(Pawn pawn, ThingWithComps weapon)
    {
        if (pawn == null || weapon == null) return;

        weaponLock.EnterWriteLock();
        try
        {
            forcedWeaponsByDef[pawn] = weapon.def;
        }
        finally
        {
            weaponLock.ExitWriteLock();
        }
    }

    public static void ClearForced(Pawn pawn)
    {
        if (pawn == null) return;

        weaponLock.EnterWriteLock();
        try
        {
            forcedWeaponsByDef.Remove(pawn);
        }
        finally
        {
            weaponLock.ExitWriteLock();
        }
    }

    public static ThingDef GetForcedWeaponDef(Pawn pawn)
    {
        if (pawn == null) return null;

        weaponLock.EnterReadLock();
        try
        {
            forcedWeaponsByDef.TryGetValue(pawn, out var def);
            return def;
        }
        finally
        {
            weaponLock.ExitReadLock();
        }
    }

    public static bool IsForced(Pawn pawn, ThingWithComps weapon)
    {
        if (pawn == null || weapon == null) return false;

        weaponLock.EnterReadLock();
        try
        {
            return forcedWeaponsByDef.TryGetValue(pawn, out var def) && def == weapon.def;
        }
        finally
        {
            weaponLock.ExitReadLock();
        }
    }

    public static void SetForcedSidearm(Pawn pawn, ThingDef weaponDef)
    {
        if (pawn == null || weaponDef == null) return;

        sidearmLock.EnterWriteLock();
        try
        {
            if (!forcedSidearmsByDef.ContainsKey(pawn))
                forcedSidearmsByDef[pawn] = new HashSet<ThingDef>();

            forcedSidearmsByDef[pawn].Add(weaponDef);
        }
        finally
        {
            sidearmLock.ExitWriteLock();
        }
    }

    public static void Cleanup()
    {
        var deadPawns = new List<Pawn>();

        // Find dead pawns while holding read lock
        weaponLock.EnterReadLock();
        try
        {
            deadPawns.AddRange(forcedWeaponsByDef.Keys.Where(p => p.DestroyedOrNull() || p.Dead));
        }
        finally
        {
            weaponLock.ExitReadLock();
        }

        // Remove dead pawns with write lock
        if (deadPawns.Count > 0)
        {
            weaponLock.EnterWriteLock();
            try
            {
                foreach (var pawn in deadPawns)
                {
                    forcedWeaponsByDef.Remove(pawn);
                }
            }
            finally
            {
                weaponLock.ExitWriteLock();
            }

            sidearmLock.EnterWriteLock();
            try
            {
                foreach (var pawn in deadPawns)
                {
                    forcedSidearmsByDef.Remove(pawn);
                }
            }
            finally
            {
                sidearmLock.ExitWriteLock();
            }
        }
    }

    // Save/Load methods with proper locking
    public static Dictionary<Pawn, ThingDef> GetSaveData()
    {
        weaponLock.EnterReadLock();
        try
        {
            return new Dictionary<Pawn, ThingDef>(forcedWeaponsByDef);
        }
        finally
        {
            weaponLock.ExitReadLock();
        }
    }

    public static void LoadSaveData(Dictionary<Pawn, ThingDef> data)
    {
        weaponLock.EnterWriteLock();
        try
        {
            forcedWeaponsByDef.Clear();
            if (data != null)
            {
                foreach (var kvp in data)
                {
                    if (kvp.Key != null && kvp.Value != null)
                    {
                        forcedWeaponsByDef[kvp.Key] = kvp.Value;
                    }
                }
            }
        }
        finally
        {
            weaponLock.ExitWriteLock();
        }
    }
}