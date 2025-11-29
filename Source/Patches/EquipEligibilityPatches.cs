using AutoArm.Definitions;
using AutoArm.Logging;
using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;
using Verse.AI;

namespace AutoArm.Patches
{

    internal static class EquipEligibilityPatches
    {
        private static MethodInfo canEquipMethod;
        private static bool canEquipSearched;

        private static int[] parameterIndexMap;

        private static object[] cachedDefaultArgs;

        private static bool TryCanEquip(Thing target, Pawn pawn)
        {
            if (target == null || pawn == null) return false;

            if (!canEquipSearched)
            {
                canEquipSearched = true;
                try
                {
                    var type = typeof(EquipmentUtility);
                    var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
                        .Where(m => m.Name == nameof(EquipmentUtility.CanEquip) && m.ReturnType == typeof(bool))
                        .ToList();

                    foreach (var m in methods)
                    {
                        var ps = m.GetParameters();
                        if (ps.Length >= 2 &&
                            (ps[0].ParameterType.IsAssignableFrom(typeof(Thing)) || ps[0].ParameterType.IsAssignableFrom(typeof(ThingWithComps))) &&
                            ps[1].ParameterType.IsAssignableFrom(typeof(Pawn)))
                        {
                            canEquipMethod = m;
                            parameterIndexMap = new int[] { 0, 1 };
                            break;
                        }
                    }

                    if (canEquipMethod == null)
                    {
                        foreach (var m in methods)
                        {
                            var ps = m.GetParameters();
                            if (ps.Length >= 2 &&
                                ps[0].ParameterType.IsAssignableFrom(typeof(Pawn)) &&
                                (ps[1].ParameterType.IsAssignableFrom(typeof(Thing)) || ps[1].ParameterType.IsAssignableFrom(typeof(ThingWithComps))))
                            {
                                canEquipMethod = m;
                                parameterIndexMap = new int[] { 1, 0 };
                                break;
                            }
                        }
                    }

                    if (canEquipMethod != null)
                    {
                        var ps = canEquipMethod.GetParameters();
                        cachedDefaultArgs = new object[ps.Length];

                        for (int i = 0; i < ps.Length; i++)
                        {
                            var pt = ps[i].ParameterType;
                            var et = pt.IsByRef ? pt.GetElementType() : pt;

                            if (i == parameterIndexMap[0] || i == parameterIndexMap[1])
                                continue;

                            if (pt.IsByRef && et == typeof(string))
                            {
                                cachedDefaultArgs[i] = null;
                            }
                            else if (et == typeof(bool))
                            {
                                cachedDefaultArgs[i] = true;
                            }
                            else
                            {
                                cachedDefaultArgs[i] = et.IsValueType ? Activator.CreateInstance(et) : null;
                            }
                        }

                        if (AutoArmMod.settings?.debugLogging == true)
                        {
                            var paramNames = string.Join(", ", ps.Select(p => p.ParameterType.Name));
                            AutoArmLogger.Debug(() => $"[EquipEligibility] Resolved and cached EquipmentUtility.CanEquip({paramNames})");
                        }
                    }
                    else
                    {
                        AutoArmLogger.Warn("[EquipEligibility] Failed to resolve EquipmentUtility.CanEquip method - equipment eligibility checks will be permissive");
                    }
                }
                catch (Exception ex)
                {
                    AutoArmLogger.ErrorPatch(ex, "EquipEligibility_CanEquipResolution");
                }
            }

            if (canEquipMethod == null)
            {
                return true;
            }

            try
            {
                var args = (object[])cachedDefaultArgs.Clone();
                args[parameterIndexMap[0]] = target;
                args[parameterIndexMap[1]] = pawn;

                var result = canEquipMethod.Invoke(null, args);
                return (bool?)result == true;
            }
            catch (Exception ex)
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.Debug(() => $"[EquipEligibility] CanEquip invocation failed for {target?.LabelShort ?? "null"} on {pawn?.LabelShort ?? "null"}: {ex.Message}");
                }
                return true;
            }
        }

        /// <summary>
        /// Filters equip jobs at the JobGiver output stage.
        /// Block failed jobs
        /// </summary>
        [HarmonyPatch]
        private static class JobGiver_PickUpBetterWeapon_TryGiveJob_Patch
        {
            private static MethodBase TargetMethod()
            {
                try
                {
                    var asm = typeof(JobGiver_PickUpBetterWeapon_TryGiveJob_Patch).Assembly;
                    var type = asm.GetTypes().FirstOrDefault(t => string.Equals(t.Name, "JobGiver_PickUpBetterWeapon", StringComparison.Ordinal));
                    return type != null ? AccessTools.Method(type, "TryGiveJob") : null;
                }
                catch { return null; }
            }

            private static void Postfix(Pawn pawn, ref Job __result)
            {
                if (pawn == null || __result == null) return;

                if (EquipCooldownTracker.IsOnCooldown(pawn))
                {
                    __result = null;
                    return;
                }

                if (__result.playerForced) return;
                if (!__result.targetA.IsValid || !__result.targetA.HasThing) return;

                var thing = __result.targetA.Thing;

                bool isSidearmJob = Compatibility.SimpleSidearmsCompat.IsLoaded &&
                                    (AutoArmMod.settings?.autoEquipSidearms == true ||
                                     AutoArmMod.settings?.allowSidearmUpgrades == true) &&
                                    (__result.def == AutoArmDefOf.EquipSecondary ||
                                     __result.def == AutoArmDefOf.ReequipSecondary ||
                                     __result.def == AutoArmDefOf.ReequipSecondaryCombat);

                if (isSidearmJob)
                {
                    var weapon = thing as ThingWithComps;
                    if (weapon != null)
                    {
                        string reason;
                        if (!Compatibility.SimpleSidearmsCompat.CanPickupSidearm(weapon, pawn, out reason))
                        {
                            AutoArmLogger.Debug(() => $"[EquipEligibility] Blocked sidearm job - SimpleSidearms check failed: {reason}");
                            __result = null;
                            return;
                        }
                    }
                }
                else if (__result.def == JobDefOf.Equip)
                {
                    if (!TryCanEquip(thing, pawn))
                    {
                        __result = null;
                    }
                }
            }
        }
    }
}
