
using AutoArm.Caching;
using AutoArm.Compatibility;
using AutoArm.Helpers;
using AutoArm.Logging;
using AutoArm.Weapons;
using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace AutoArm
{

    /// <summary>
    /// Empty stub
    /// Legacy GameComponent
    /// </summary>
    [Obsolete("Feature removed - stub exists only for save compatibility")]
    public class OutfitComplianceChecker : GameComponent
    {
        public OutfitComplianceChecker()
        {
        }

        public OutfitComplianceChecker(Game game)
        {
        }

        public override void ExposeData()
        {
            base.ExposeData();
        }

        public override void GameComponentTick()
        {
        }
    }


    /// <summary>
    /// Outfit policy cache
    /// </summary>
    public static class OutfitFilterCache
    {
        private static readonly Dictionary<ThingFilter, ApparelPolicy> filterToPolicyMap = new Dictionary<ThingFilter, ApparelPolicy>();

        public static void RebuildCache()
        {
            filterToPolicyMap.Clear();

            var outfitDatabase = Current.Game?.outfitDatabase;
            if (outfitDatabase?.AllOutfits == null)
                return;

            foreach (var policy in outfitDatabase.AllOutfits)
            {
                if (policy?.filter != null)
                {
                    filterToPolicyMap[policy.filter] = policy;
                }
            }
        }

        public static ApparelPolicy GetPolicyForFilter(ThingFilter filter)
        {
            if (filter == null)
                return null;

            if (filterToPolicyMap.TryGetValue(filter, out var policy))
                return policy;

            RebuildCache();
            filterToPolicyMap.TryGetValue(filter, out policy);
            return policy;
        }

        public static void Clear()
        {
            filterToPolicyMap.Clear();
        }
    }

}
