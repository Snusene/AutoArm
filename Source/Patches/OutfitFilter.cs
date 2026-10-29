
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

}
