# AutoArm Test Error Reporting Guidelines

## Pattern for Test Failures

When a test fails, it should:

1. **Set result.Success = false**
2. **Set result.FailureReason** with a brief description
3. **Add result.Data["Error"]** with a more detailed error message
4. **Add additional diagnostic data** to help debug the issue
5. **Use AutoArmLogger.LogError()** to log to the debug file

## Example Pattern:

```csharp
if (job == null)
{
    var result = TestResult.Failure("No weapon pickup job created");
    result.Data["Error"] = "Failed to create weapon acquisition job";
    result.Data["PawnValid"] = isValidPawn;
    result.Data["InvalidReason"] = reason;
    result.Data["WeaponInCache"] = inCache;
    // ... more diagnostic data
    
    AutoArmLogger.LogError($"[TEST] TestName: Detailed error message");
    return result;
}
```

## Key Points:

1. **Always include an "Error" field** in Data dictionary for failed tests
2. **Include diagnostic information** that helps understand why the test failed
3. **Use descriptive field names** in the Data dictionary
4. **Log errors to debug file** for additional debugging

## Test Output Format:

Failed tests will show:
```
✗ Test Name
   └─ Brief failure reason from FailureReason
   └─ Error: Detailed error message
   └─ DiagnosticField1: value
   └─ DiagnosticField2: value
```

This ensures all test failures are clear, consistent, and provide enough information to debug issues.
