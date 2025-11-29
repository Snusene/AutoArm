using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace AutoArm.Testing
{
    /// <summary>
    /// Test interface
    /// </summary>
    public interface ITestScenario
    {
        /// <summary>
        /// Name of the test scenario
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Setup test
        /// </summary>
        void Setup(Map map);

        /// <summary>
        /// Run the test and return results
        /// </summary>
        TestResult Run();

        /// <summary>
        /// Cleanup after test
        /// </summary>
        void Cleanup();
    }

    /// <summary>
    /// Single test result
    /// </summary>
    public class TestResult
    {
        public bool Success { get; set; }
        public string FailureReason { get; set; }
        public Dictionary<string, object> Data { get; set; } = new Dictionary<string, object>();

        public static TestResult Pass() => new TestResult { Success = true };

        public static TestResult Failure(string reason) => new TestResult
        {
            Success = false,
            FailureReason = reason
        };

        public TestResult WithData(string key, object value)
        {
            Data[key] = value;
            return this;
        }

        public override string ToString()
        {
            if (Success)
            {
                return Data.Count > 0 ?
                    $"PASS (Data: {string.Join(", ", Data.Select(kvp => $"{kvp.Key}={kvp.Value}"))})" :
                    "PASS";
            }
            return $"FAIL: {FailureReason}";
        }
    }

    /// <summary>
    /// Test results
    /// </summary>
    public class TestResults
    {
        private readonly Dictionary<string, TestResult> results = new Dictionary<string, TestResult>();
        private readonly Dictionary<string, TimeSpan> timings = new Dictionary<string, TimeSpan>();

        public int TotalTests => results.Count;
        public int PassedTests => results.Count(r => r.Value.Success);
        public int FailedTests => results.Count(r => !r.Value.Success);
        public float SuccessRate => TotalTests > 0 ? PassedTests / (float)TotalTests : 0f;

        public void AddResult(string testName, TestResult result)
        {
            if (string.IsNullOrEmpty(testName))
                throw new ArgumentNullException(nameof(testName));

            if (result == null)
                throw new ArgumentNullException(nameof(result));

            results[testName] = result;
        }

        public void AddTiming(string testName, TimeSpan duration)
        {
            timings[testName] = duration;
        }

        public TestResult GetResult(string testName)
        {
            results.TryGetValue(testName, out var result);
            return result;
        }

        public TimeSpan? GetTiming(string testName)
        {
            return timings.TryGetValue(testName, out var timing) ? timing : (TimeSpan?)null;
        }

        public Dictionary<string, TestResult> GetFailedTests()
        {
            return results.Where(r => !r.Value.Success)
                         .ToDictionary(r => r.Key, r => r.Value);
        }

        public Dictionary<string, TestResult> GetPassedTests()
        {
            return results.Where(r => r.Value.Success)
                         .ToDictionary(r => r.Key, r => r.Value);
        }

        public Dictionary<string, TestResult> GetAllResults()
        {
            return new Dictionary<string, TestResult>(results);
        }

        public void Clear()
        {
            results.Clear();
            timings.Clear();
        }

        public override string ToString()
        {
            return $"TestResults: {PassedTests}/{TotalTests} passed ({SuccessRate:P0})";
        }
    }

    /// <summary>
    /// Test base class
    /// </summary>
    public abstract class TestScenarioBase : ITestScenario
    {
        public abstract string Name { get; }

        protected Map testMap;
        protected List<Pawn> createdPawns = new List<Pawn>();
        protected List<Thing> createdThings = new List<Thing>();

        public virtual void Setup(Map map)
        {
            testMap = map;
            createdPawns.Clear();
            createdThings.Clear();
        }

        public abstract TestResult Run();

        public virtual void Cleanup()
        {
            foreach (var thing in createdThings)
            {
                if (thing != null && !thing.Destroyed)
                {
                    thing.Destroy();
                }
            }
            createdThings.Clear();

            foreach (var pawn in createdPawns)
            {
                if (pawn != null && !pawn.Destroyed)
                {
                    pawn.jobs?.StopAll();
                    pawn.equipment?.DestroyAllEquipment();
                    pawn.Destroy();
                }
            }
            createdPawns.Clear();
        }

        /// <summary>
        /// Track pawns
        /// </summary>
        protected T TrackPawn<T>(T pawn) where T : Pawn
        {
            if (pawn != null)
                createdPawns.Add(pawn);
            return pawn;
        }

        /// <summary>
        /// Track things
        /// </summary>
        protected T TrackThing<T>(T thing) where T : Thing
        {
            if (thing != null)
                createdThings.Add(thing);
            return thing;
        }
    }

    /// <summary>
    /// Test exception
    /// </summary>
    public class TestException : Exception
    {
        public TestException() : base()
        {
        }

        public TestException(string message) : base(message)
        {
        }

        public TestException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
