using AutoArm.Jobs;
using Verse;

namespace AutoArm.Testing.Scenarios
{
    internal sealed class NullInputSafetyTest : ITestScenario
    {
        public string Name => "Null inputs handled";

        public void Setup(Map map) { }

        public TestResult Run()
        {
            var jobGiver = new JobGiver_PickUpBetterWeapon();

            if (jobGiver.TestTryGiveJob(null) != null)
                return TestResult.Failure("TestTryGiveJob(null) returned a job");

            if (jobGiver.GetWeaponScore(null, null) != 0f)
                return TestResult.Failure("GetWeaponScore(null, null) returned non-zero");

            return TestResult.Pass();
        }

        public void Cleanup() { }
    }
}
