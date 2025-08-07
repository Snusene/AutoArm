using AutoArm.Definitions;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace AutoArm.Testing.Helpers
{
    /// <summary>
    /// Test-specific constants and helpers
    /// </summary>
    public static class TestConstants
    {
        // Runtime settings that might override compile-time constants
        public static float WeaponUpgradeThreshold =>
            AutoArmMod.settings?.weaponUpgradeThreshold ?? Constants.WeaponUpgradeThreshold;

        // Test-specific constants
        public const int DefaultTestIterations = 10;

        public const int QuickTestIterations = 3;
        public const int ThoroughTestIterations = 20;

        public const int StressTestPawnCount = 50;
        public const int StressTestWeaponCount = 100;
        public const int StandardTestPawnCount = 10;
        public const int StandardTestWeaponCount = 20;

        // Performance thresholds (in milliseconds)
        public const double AcceptableJobCreationTime = 20.0;
        public const double MaxJobCreationTime = 50.0;
        public const double AcceptableCacheQueryTime = 10.0;
        public const double MaxCacheQueryTime = 50.0;
        public const double AcceptableScoreCalculationTime = 5.0;
        public const double MaxScoreCalculationTime = 20.0;
        
        // Cache rebuild thresholds
        public const double AcceptableCacheRebuildTime = 100.0;  // 100ms for full cache rebuild
        public const double MaxCacheRebuildTime = 500.0;  // 500ms max
        
        // Weapon search thresholds
        public const double AcceptableWeaponSearchTime = 5.0;  // 5ms average per search
        public const double MaxWeaponSearchTime = 20.0;  // 20ms max per search

        // Memory thresholds (in MB)
        public const double AcceptableMemoryUsage = 50.0;

        public const double MaxMemoryUsage = 200.0;

        // Test timeouts (in seconds)
        public const int DefaultTestTimeout = 30;

        public const int StressTestTimeout = 120;
        public const int QuickTestTimeout = 10;
    }

    /// <summary>
    /// Helper methods for generating test positions
    /// </summary>
    public static class TestPositions
    {
        /// <summary>
        /// Get positions in a circle around a center point
        /// </summary>
        public static List<IntVec3> GetCirclePositions(IntVec3 center, float radius, int count, Map map)
        {
            var positions = new List<IntVec3>();
            if (count <= 0) return positions;

            float angleStep = 360f / count;

            for (int i = 0; i < count; i++)
            {
                float angle = i * angleStep;
                var offset = (Vector3.forward.RotatedBy(angle) * radius).ToIntVec3();
                var pos = center + offset;

                // Ensure position is valid
                if (pos.InBounds(map) && pos.Standable(map))
                {
                    positions.Add(pos);
                }
                else
                {
                    // Try closer position if out of bounds
                    for (float r = radius * 0.8f; r > 0; r -= radius * 0.2f)
                    {
                        var closerOffset = (Vector3.forward.RotatedBy(angle) * r).ToIntVec3();
                        var closerPos = center + closerOffset;
                        if (closerPos.InBounds(map) && closerPos.Standable(map))
                        {
                            positions.Add(closerPos);
                            break;
                        }
                    }
                }
            }

            return positions;
        }

        /// <summary>
        /// Get positions at specific distances for progressive search testing
        /// </summary>
        public static Dictionary<float, List<IntVec3>> GetProgressiveDistancePositions(IntVec3 center, Map map)
        {
            var result = new Dictionary<float, List<IntVec3>>();

            float[] distances = {
                Constants.SearchRadiusClose,      // 15f
                Constants.SearchRadiusMedium,     // 30f
                Constants.DefaultSearchRadius,    // 60f
                Constants.RaidApproachDistance    // 80f
            };

            foreach (var distance in distances)
            {
                result[distance] = GetCirclePositions(center, distance, 4, map);
            }

            return result;
        }

        /// <summary>
        /// Get a valid position near a center point
        /// </summary>
        public static IntVec3 GetNearbyPosition(IntVec3 center, float minDistance, float maxDistance, Map map)
        {
            // Try random positions
            for (int i = 0; i < 20; i++)
            {
                float distance = Rand.Range(minDistance, maxDistance);
                float angle = Rand.Range(0f, 360f);
                var offset = (Vector3.forward.RotatedBy(angle) * distance).ToIntVec3();
                var pos = center + offset;

                if (pos.InBounds(map) && pos.Standable(map))
                {
                    return pos;
                }
            }

            // Fallback to finding any nearby standable cell
            for (int radius = (int)minDistance; radius <= (int)maxDistance; radius++)
            {
                if (CellFinder.TryFindRandomCellNear(center, map, radius,
                    c => c.Standable(map), out IntVec3 result))
                {
                    return result;
                }
            }

            // Last resort - any position near center
            return center + new IntVec3(Rand.Range(-3, 3), 0, Rand.Range(-3, 3));
        }

        /// <summary>
        /// Get positions in a line from center
        /// </summary>
        public static List<IntVec3> GetLinePositions(IntVec3 center, Vector3 direction, float spacing, int count, Map map)
        {
            var positions = new List<IntVec3>();
            direction = direction.normalized;

            for (int i = 1; i <= count; i++)
            {
                var offset = (direction * spacing * i).ToIntVec3();
                var pos = center + offset;

                if (pos.InBounds(map) && pos.Standable(map))
                {
                    positions.Add(pos);
                }
                else
                {
                    // Try perpendicular offsets if direct line is blocked
                    var perpendicular = Vector3.Cross(direction, Vector3.up).normalized;
                    for (int j = 1; j <= 3; j++)
                    {
                        var altPos1 = center + offset + (perpendicular * j).ToIntVec3();
                        var altPos2 = center + offset - (perpendicular * j).ToIntVec3();

                        if (altPos1.InBounds(map) && altPos1.Standable(map))
                        {
                            positions.Add(altPos1);
                            break;
                        }
                        if (altPos2.InBounds(map) && altPos2.Standable(map))
                        {
                            positions.Add(altPos2);
                            break;
                        }
                    }
                }
            }

            return positions;
        }

        /// <summary>
        /// Get a grid of positions
        /// </summary>
        public static List<IntVec3> GetGridPositions(IntVec3 center, int width, int height, int spacing, Map map)
        {
            var positions = new List<IntVec3>();

            int halfWidth = width / 2;
            int halfHeight = height / 2;

            for (int x = -halfWidth; x <= halfWidth; x++)
            {
                for (int z = -halfHeight; z <= halfHeight; z++)
                {
                    var pos = center + new IntVec3(x * spacing, 0, z * spacing);
                    if (pos.InBounds(map) && pos.Standable(map))
                    {
                        positions.Add(pos);
                    }
                }
            }

            return positions;
        }

        /// <summary>
        /// Get a random valid position on the map
        /// </summary>
        public static IntVec3 GetRandomPosition(Map map)
        {
            // Try to find a good random position
            for (int i = 0; i < 100; i++)
            {
                var pos = CellFinder.RandomCell(map);
                if (pos.Standable(map) && pos.GetRoom(map) != null && !pos.Fogged(map))
                {
                    return pos;
                }
            }

            // Fallback to any standable position
            for (int i = 0; i < 100; i++)
            {
                var pos = CellFinder.RandomCell(map);
                if (pos.Standable(map))
                {
                    return pos;
                }
            }

            // Last resort - area near map center
            return GetNearbyPosition(map.Center, 5, 20, map);
        }

        /// <summary>
        /// Find the nearest valid position to a given position
        /// </summary>
        public static IntVec3 GetNearestValidPosition(IntVec3 pos, Map map)
        {
            if (pos.InBounds(map) && pos.Standable(map))
                return pos;

            // Search in expanding circles
            for (int radius = 1; radius <= 10; radius++)
            {
                if (CellFinder.TryFindRandomCellNear(pos, map, radius,
                    c => c.Standable(map), out IntVec3 result))
                {
                    return result;
                }
            }

            // Fallback to map center area
            return GetNearbyPosition(map.Center, 5, 20, map);
        }
    }
}