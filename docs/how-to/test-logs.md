# Test Logs

> **TL;DR**: Accordant writes detailed logs to `{assembly}/test-logs/{timestamp}/test-runner.txt`. You can customize the location, redirect output, add timestamps, and enable HTTP diagnostic logging.

---

## Where Logs Go By Default

When you run tests, Accordant writes logs to a timestamped folder next to your test assembly:

```
bin/Debug/net8.0/
├── YourTests.dll
└── test-logs/
    └── 2026-05-24-14-32-17-ms/
        └── test-runner.txt
```

The folder name includes a timestamp so each test run gets its own logs. Inside, `test-runner.txt` contains:
- Test case descriptions
- Operation executions (requests and responses)
- Validation results
- Success/failure messages

---

## Customizing the Output Directory

### For All Logs

Set the output directory before running tests:

```csharp
Logger.AsyncLocalOutputDirectory.Value = @"C:\my-logs\accordant";
```

### For a Scoped Section

Use the `Logger` disposable to temporarily change the directory:

```csharp
using (new Logger(outputDirectory: @"C:\my-logs\special-run"))
{
    // Logs in this block go to the custom directory
    await spec.RunTests(...);
}
// Back to the previous directory
```

---

## Redirecting Log Output

By default, logs write to a file. You can redirect them anywhere — console, test output, a custom sink:

### To Console

```csharp
Logger.LogLambda = logLine => Console.WriteLine(logLine);
```

### To Test Output (xUnit)

```csharp
public class MyTests
{
    private readonly ITestOutputHelper _output;

    public MyTests(ITestOutputHelper output)
    {
        _output = output;
        Logger.LogLambda = logLine => _output.WriteLine(logLine);
    }
}
```

### To Test Output (NUnit)

```csharp
[SetUp]
public void Setup()
{
    Logger.LogLambda = logLine => TestContext.WriteLine(logLine);
}
```

### To Test Output (MSTest)

```csharp
public TestContext TestContext { get; set; }

[TestInitialize]
public void Initialize()
{
    Logger.LogLambda = logLine => TestContext.WriteLine(logLine);
}
```

### To Multiple Destinations

```csharp
Logger.LogLambda = logLine =>
{
    Console.WriteLine(logLine);
    File.AppendAllText("my-log.txt", logLine + Environment.NewLine);
};
```

---

## Adding Timestamps

Enable timestamps to see when each log line was emitted:

```csharp
Logger.AsyncLocalEmitTimestamp.Value = true;
```

Output becomes:

```
5/24/2026 2:32:17 PM Executing 50 sequential tests.
5/24/2026 2:32:17 PM Test Case: 1 of 50
5/24/2026 2:32:18 PM     Executing "CreateAccount"
```

You can also enable timestamps for a scoped section:

```csharp
using (new Logger(emitTimestamp: true))
{
    // Timestamps enabled here
    await spec.RunTests(...);
}
```

---

## Indentation for Nested Logging

Accordant uses indentation to show structure. You can add more indentation for custom logging:

```csharp
Logger.Log("Starting custom validation");
using (new Logger(indent: true))
{
    Logger.Log("Checking field A");  // Indented
    Logger.Log("Checking field B");  // Indented
}
Logger.Log("Validation complete");   // Back to original level
```

Output:

```
Starting custom validation
    Checking field A
    Checking field B
Validation complete
```

---

## Emitting Your Own Log Lines

Call `Logger.Log()` anywhere to add custom messages:

```csharp
spec.Operation<string, ApiResult<User>>("CreateUser", (userId, state) =>
{
    Logger.Log($"Applying CreateUser for '{userId}'");
    
    if (state.Users.ContainsKey(userId))
    {
        Logger.Log($"  User already exists, expecting conflict");
        return Expect.That<ApiResult<User>>(r => r.IsConflict)
               .SameState();
    }
    
    Logger.Log($"  User is new, expecting success");
    // ...
});
```

---

## HTTP Diagnostic Logging

When using `HttpExecutable` for HTTP operations, you can enable detailed request/response logging:

```csharp
var httpExecutable = new HttpExecutable
{
    EmitDiagnosticLogs = true,           // Enable HTTP logging
    EmitCorrelationId = true,            // Include correlation IDs for tracing
    MaxLogLineLength = 4096,             // Increase max line length (default: 2KB)
    TrimLogLinesIfTooLong = true         // Truncate long responses
};
```

This logs HTTP details like:

```
[abc123] Executing GET https://api.example.com/users/alice
[abc123] Response: 200 OK
[abc123] Body: {"userId":"alice","name":"Alice"}
```

### Custom Log Emitter for HTTP

By default, HTTP logs go to `Logger.Log()`. You can redirect them:

```csharp
httpExecutable.LogLineEmitter = logLine => 
{
    // Send HTTP logs somewhere specific
    _httpLogger.LogDebug(logLine);
};
```

---

## Finding Logs After a Test Run

If you're not sure where logs went:

1. Check the test output for the path (Accordant logs the directory)
2. Look in `bin/Debug/net8.0/test-logs/` (or your output directory)
3. Sort by date — the most recent folder is your latest run

### Keeping Logs Organized

For CI/CD, set a predictable output directory:

```csharp
var runId = Environment.GetEnvironmentVariable("BUILD_ID") ?? DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss");
Logger.AsyncLocalOutputDirectory.Value = Path.Combine("test-results", runId);
```

---

## Summary

| Task | Code |
|------|------|
| Change output directory | `Logger.AsyncLocalOutputDirectory.Value = path;` |
| Redirect to console | `Logger.LogLambda = Console.WriteLine;` |
| Add timestamps | `Logger.AsyncLocalEmitTimestamp.Value = true;` |
| Log custom message | `Logger.Log("message");` |
| Enable HTTP logging | `httpExecutable.EmitDiagnosticLogs = true;` |
| Scoped settings | `using (new Logger(indent: true, emitTimestamp: true, outputDirectory: path))` |
