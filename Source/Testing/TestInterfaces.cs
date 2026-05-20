using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace AutoArm.Testing
{
    public interface ITestScenario
    {
        string Name { get; }

        void Setup(Map map);

        TestResult Run();

        void Cleanup();
    }

    public enum TestOutcome { Pass, Fail, Skip }

    public class TestResult
    {
        public TestOutcome Outcome { get; set; } = TestOutcome.Pass;
        public string FailureReason { get; set; }
        public string SkipReason { get; set; }
        public Dictionary<string, object> Data { get; set; } = new Dictionary<string, object>();

        public bool Success
        {
            get => Outcome == TestOutcome.Pass;
            set => Outcome = value ? TestOutcome.Pass : TestOutcome.Fail;
        }

        public bool Skipped => Outcome == TestOutcome.Skip;

        public static TestResult Pass() => new TestResult { Outcome = TestOutcome.Pass };

        public static TestResult Failure(string reason) => new TestResult
        {
            Outcome = TestOutcome.Fail,
            FailureReason = reason
        };

        public static TestResult Skip(string reason) => new TestResult
        {
            Outcome = TestOutcome.Skip,
            SkipReason = reason
        };

        public TestResult WithData(string key, object value)
        {
            Data[key] = value;
            return this;
        }

        public override string ToString()
        {
            switch (Outcome)
            {
                case TestOutcome.Skip:
                    return $"SKIP: {SkipReason}";
                case TestOutcome.Fail:
                    return $"FAIL: {FailureReason}";
                default:
                    return Data.Count > 0
                        ? $"PASS (Data: {string.Join(", ", Data.Select(kvp => $"{kvp.Key}={kvp.Value}"))})"
                        : "PASS";
            }
        }
    }

    public class TestResults
    {
        private readonly Dictionary<string, TestResult> results = new Dictionary<string, TestResult>();
        private readonly Dictionary<string, TimeSpan> timings = new Dictionary<string, TimeSpan>();

        public int TotalTests => results.Count;
        public int PassedTests => results.Count(r => r.Value.Outcome == TestOutcome.Pass);
        public int FailedTests => results.Count(r => r.Value.Outcome == TestOutcome.Fail);
        public int SkippedTests => results.Count(r => r.Value.Outcome == TestOutcome.Skip);
        public int RanTests => TotalTests - SkippedTests;
        public float SuccessRate => RanTests > 0 ? PassedTests / (float)RanTests : 0f;

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
            return results.Where(r => r.Value.Outcome == TestOutcome.Fail)
                         .ToDictionary(r => r.Key, r => r.Value);
        }

        public Dictionary<string, TestResult> GetPassedTests()
        {
            return results.Where(r => r.Value.Outcome == TestOutcome.Pass)
                         .ToDictionary(r => r.Key, r => r.Value);
        }

        public Dictionary<string, TestResult> GetSkippedTests()
        {
            return results.Where(r => r.Value.Outcome == TestOutcome.Skip)
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
            return SkippedTests > 0
                ? $"TestResults: {PassedTests}/{RanTests} passed ({SuccessRate:P0}), {SkippedTests} skipped"
                : $"TestResults: {PassedTests}/{TotalTests} passed ({SuccessRate:P0})";
        }
    }

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

        protected T TrackPawn<T>(T pawn) where T : Pawn
        {
            if (pawn != null)
                createdPawns.Add(pawn);
            return pawn;
        }

        protected T TrackThing<T>(T thing) where T : Thing
        {
            if (thing != null)
                createdThings.Add(thing);
            return thing;
        }
    }

}
