
using AutoArm.Definitions;
using UnityEngine;
using Verse;

namespace AutoArm.Testing.Helpers
{
    internal static class TestConstants
    {
        public static float WeaponUpgradeThreshold =>
            AutoArmMod.settings?.weaponUpgradeThreshold ?? Constants.WeaponUpgradeThreshold;
    }

    internal static class TestPositions
    {
        public static IntVec3 GetNearbyPosition(IntVec3 center, float minDistance, float maxDistance, Map map)
        {
            for (int i = 0; i < 20; i++)
            {
                float distance = Rand.Range(minDistance, maxDistance);
                float angle = Rand.Range(0f, 360f);
                var offset = (Vector3.forward.RotatedBy(angle) * distance).ToIntVec3();
                var pos = center + offset;

                if (pos.InBounds(map) && pos.Standable(map))
                {
                    if (map.reachability != null && map.reachability.CanReach(center, pos, Verse.AI.PathEndMode.OnCell, TraverseMode.PassDoors))
                    {
                        return pos;
                    }
                }
            }

            for (int radius = (int)minDistance; radius <= (int)maxDistance; radius++)
            {
                if (CellFinder.TryFindRandomCellNear(center, map, radius,
                    c => c.Standable(map) && (map.reachability == null || map.reachability.CanReach(center, c, Verse.AI.PathEndMode.OnCell, TraverseMode.PassDoors)),
                    out IntVec3 result))
                {
                    return result;
                }
            }

            return center + new IntVec3(Rand.Range(-3, 3), 0, Rand.Range(-3, 3));
        }
    }
}
