
using AutoArm.Caching;
using AutoArm.Compatibility;
using AutoArm.Helpers;
using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using Verse;

namespace AutoArm
{

    [Obsolete("Feature removed - stub exists only for save compatibility")]
    public sealed class OutfitComplianceChecker : GameComponent
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
    }


    internal static class OutfitFilterCache
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

    }

}
