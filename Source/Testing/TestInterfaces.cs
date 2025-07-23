using System.Collections.Generic;
using Verse;
using System.Linq;

namespace AutoArm.Testing
{
    public interface ITestScenario
    {
        string Name { get; }
        void Setup(Map map);
        TestResult Run();
        void Cleanup();
    }

    public class TestResult
    {
        public bool Success { get; set; }
        public string FailureReason { get; set; }
        public Dictionary<string, object> Data { get; set; } = new Dictionary<string, object>();

        public static TestResult Pass() => new TestResult { Success = true };
        public static TestResult Failure(string reason) => new TestResult { Success = false, FailureReason = reason };
    }

    public class TestResults
    {
        private Dictionary<string, TestResult> results = new Dictionary<string, TestResult>();

        public int TotalTests => results.Count;
        public int PassedTests => results.Count(r => r.Value.Success);
        public int FailedTests => results.Count(r => !r.Value.Success);
        public float SuccessRate => TotalTests > 0 ? PassedTests / (float)TotalTests : 0f;

        public void AddResult(string testName, TestResult result)
        {
            results[testName] = result;
        }

        public TestResult GetResult(string testName)
        {
            results.TryGetValue(testName, out var result);
            return result;
        }

        public Dictionary<string, TestResult> GetFailedTests()
        {
            return results.Where(r => !r.Value.Success).ToDictionary(r => r.Key, r => r.Value);
        }

        public Dictionary<string, TestResult> GetAllResults()
        {
            return new Dictionary<string, TestResult>(results);
        }
    }
}