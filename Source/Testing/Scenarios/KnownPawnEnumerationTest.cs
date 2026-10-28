using AutoArm.Helpers;
using AutoArm.Testing.Helpers;
using Verse;

namespace AutoArm.Testing.Scenarios
{
    internal sealed class KnownPawnEnumerationTest : ITestScenario
    {
        public string Name => "Known pawn enumeration covers map and world";

        private Pawn testPawn;

        public void Setup(Map map)
        {
            if (map == null) return;
            testPawn = TestHelpers.CreateTestPawn(map);
        }

        public TestResult Run()
        {
            if (testPawn == null)
                return TestResult.Failure("Test setup failed");

            var result = new TestResult { Success = true };

            var pawns = ListPool<Pawn>.Get(256);
            try
            {
                ForcedWeapons.CollectKnownPawns(pawns);

                bool mapPawnFound = pawns.Contains(testPawn);
                result.Data["MapPawnFound"] = mapPawnFound;
                if (!mapPawnFound)
                {
                    result.Success = false;
                    result.FailureReason = "Spawned map colonist missing from CollectKnownPawns";
                }

                Pawn worldPawn = null;
                var worldPawns = Find.World?.worldPawns?.AllPawnsAliveOrDead;
                if (worldPawns != null)
                {
                    for (int i = 0; i < worldPawns.Count; i++)
                    {
                        if (worldPawns[i] != null)
                        {
                            worldPawn = worldPawns[i];
                            break;
                        }
                    }
                }

                if (worldPawn != null)
                {
                    bool worldPawnFound = pawns.Contains(worldPawn);
                    result.Data["WorldPawnFound"] = worldPawnFound;
                    if (!worldPawnFound)
                    {
                        result.Success = false;
                        result.FailureReason = "World pawn missing from CollectKnownPawns";
                    }
                }
                else
                {
                    result.Data["NoWorldPawnsToCheck"] = true;
                }
            }
            finally
            {
                ListPool<Pawn>.Return(pawns);
            }

            return result;
        }

        public void Cleanup()
        {
            if (testPawn != null && !testPawn.Destroyed)
                TestHelpers.SafeDestroyPawn(testPawn);
        }
    }
}
