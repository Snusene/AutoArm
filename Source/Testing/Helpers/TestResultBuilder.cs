// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: Test result builder utility
// Provides consistent test result formatting

using System.Collections.Generic;
using System.Linq;

namespace AutoArm.Testing.Helpers
{
    /// <summary>
    /// Helper class to build test results with consistent formatting
    /// </summary>
    public class TestResultBuilder
    {
        private TestResult result;
        private List<string> subTests = new List<string>();
        
        public TestResultBuilder()
        {
            result = new TestResult { Success = true };
        }
        
        /// <summary>
        /// Add a simple data entry
        /// </summary>
        public TestResultBuilder AddData(string key, object value)
        {
            result.Data[key] = value;
            return this;
        }
        
        /// <summary>
        /// Add a note (will be formatted as "Note: ...")
        /// </summary>
        public TestResultBuilder AddNote(string note)
        {
            result.Data[$"Note_{result.Data.Count}"] = note;
            return this;
        }
        
        /// <summary>
        /// Add a sub-test result
        /// </summary>
        public TestResultBuilder AddSubTest(string name, bool passed, string details = null)
        {
            string status = passed ? "PASS" : "FAIL";
            string entry = $"{name}: {status}";
            if (!string.IsNullOrEmpty(details))
            {
                entry += $" - {details}";
            }
            
            result.Data[$"Test{subTests.Count + 1}_{name}"] = entry;
            subTests.Add(name);
            
            if (!passed)
            {
                result.Success = false;
            }
            
            return this;
        }
        
        /// <summary>
        /// Add detailed log output
        /// </summary>
        public TestResultBuilder AddDetailedLog(string logKey, string logContent)
        {
            result.Data[logKey] = logContent;
            return this;
        }
        
        /// <summary>
        /// Set the test as failed with a reason
        /// </summary>
        public TestResultBuilder Fail(string reason)
        {
            result.Success = false;
            result.FailureReason = reason;
            return this;
        }
        
        /// <summary>
        /// Add a warning without failing the test
        /// </summary>
        public TestResultBuilder AddWarning(string warning)
        {
            result.Data[$"Warning_{result.Data.Count}"] = warning;
            return this;
        }
        
        /// <summary>
        /// Add an error and fail the test
        /// </summary>
        public TestResultBuilder AddError(string error)
        {
            result.Success = false;
            if (string.IsNullOrEmpty(result.FailureReason))
            {
                result.FailureReason = error;
            }
            result.Data[$"Error_{result.Data.Count}"] = error;
            return this;
        }
        
        /// <summary>
        /// Add a result summary
        /// </summary>
        public TestResultBuilder AddSummary(string summary)
        {
            result.Data["Summary"] = summary;
            return this;
        }
        
        /// <summary>
        /// Add a validation result
        /// </summary>
        public TestResultBuilder AddValidation(string item, bool isValid, string reason = null)
        {
            string validation = isValid ? "Valid" : "Invalid";
            if (!string.IsNullOrEmpty(reason))
            {
                validation += $" - {reason}";
            }
            
            result.Data[$"Validation_{item}"] = validation;
            
            if (!isValid)
            {
                result.Success = false;
            }
            
            return this;
        }
        
        /// <summary>
        /// Build the final test result
        /// </summary>
        public TestResult Build()
        {
            // If we have sub-tests, add a summary
            if (subTests.Count > 0)
            {
                int passed = subTests.Count(name => 
                {
                    var key = result.Data.Keys.FirstOrDefault(k => k.Contains(name));
                    return key != null && result.Data[key].ToString().Contains("PASS");
                });
                
                if (!result.Data.ContainsKey("Summary"))
                {
                    result.Data["Summary"] = $"{passed}/{subTests.Count} sub-tests passed";
                }
            }
            
            return result;
        }
    }
}
