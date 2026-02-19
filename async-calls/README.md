# Async/Await and Cancellation Tokens in .NET

## Overview

This folder contains comprehensive training material and runnable Minimal API proofs-of-concept demonstrating .NET async operations and cancellation tokens. The examples use a realistic flight offer pricing scenario to illustrate best practices.

## Table of Contents

1. [Flight Offer Scenario](#flight-offer-scenario)
2. [Understanding Async/Await](#understanding-asyncawait)
3. [Pass-Through Methods](#pass-through-methods)
4. [Await in Catch Blocks](#await-in-catch-blocks)
5. [Task.WhenAll for Aggregation](#taskwhenall-for-aggregation)
6. [Running the Examples](#running-the-examples)
7. [Testing Cancellation](#testing-cancellation)
8. [Proof of Concepts](#proof-of-concepts)

---

## Flight Offer Scenario

### The Problem

Imagine building a REST API endpoint that retrieves flight offer prices from multiple external providers. Each provider call might take several seconds:

```
Client → Your API → Provider 1 (2-5 seconds)
                  → Provider 2 (2-5 seconds)
                  → Provider 3 (2-5 seconds)
```

### Why Async Matters

If your API uses **synchronous** (blocking) code:
- Each request **blocks a thread** for the entire duration (up to 5+ seconds)
- Under load (1000s of concurrent requests), you'd need 1000s of threads
- Thread pool exhaustion → request queuing → timeouts → system collapse

With **async/await**:
- Threads are **released** while waiting for I/O (network calls)
- The same thread can handle other requests
- Dramatically better scalability (handle 1000s of concurrent requests with dozens of threads)

### Why Cancellation Tokens Matter

When a client **disconnects** or **times out**:
- Without proper cancellation: Your server continues expensive work for no one
- With cancellation tokens: You can immediately stop work, free resources, and cancel downstream calls

---

## Understanding Async/Await

### Async/Await is Syntactic Sugar

The `async`/`await` keywords are **compiler syntactic sugar** that transform your sequential-looking code into a state machine that can pause and resume execution.

#### Example: What the Compiler Does

**Your code:**
```csharp
public async Task<string> GetDataAsync()
{
    var result = await httpClient.GetStringAsync("https://api.example.com/data");
    return result.ToUpper();
}
```

**What the compiler generates (simplified):**
```csharp
// The compiler creates a state machine struct
public Task<string> GetDataAsync()
{
    var stateMachine = new <GetDataAsync>StateMachine
    {
        state = 0,
        builder = AsyncTaskMethodBuilder<string>.Create(),
        // ... other fields
    };
    
    stateMachine.builder.Start(ref stateMachine);
    return stateMachine.builder.Task;
}

// State machine (simplified)
struct <GetDataAsync>StateMachine
{
    public int state;
    public AsyncTaskMethodBuilder<string> builder;
    private TaskAwaiter<string> awaiter;
    
    public void MoveNext()
    {
        string result;
        try
        {
            if (state == 0)
            {
                // First call - start the async operation
                awaiter = httpClient.GetStringAsync("https://api.example.com/data").GetAwaiter();
                
                if (!awaiter.IsCompleted)
                {
                    state = 1;
                    // Schedule continuation and return (releases thread)
                    builder.AwaitUnsafeOnCompleted(ref awaiter, ref this);
                    return;
                }
            }
            
            if (state == 1)
            {
                // Resumed after await - get the result
                string httpResult = awaiter.GetResult();
                result = httpResult.ToUpper();
                builder.SetResult(result);
                return;
            }
        }
        catch (Exception ex)
        {
            builder.SetException(ex);
        }
    }
}
```

#### Key Points

1. **async** marks a method as needing state machine generation
2. **await** marks suspension points where the method can pause
3. When awaiting an incomplete Task:
   - Current thread is released (can do other work)
   - A continuation is scheduled to resume when Task completes
   - Execution "jumps out" of the method
4. When the awaited Task completes:
   - The continuation runs (possibly on a different thread)
   - Execution "jumps back in" to continue from the await point

#### Comparing Async Method vs Direct Task Return

**WITH async/await (syntactic sugar):**
```csharp
public async Task<string> GetUserAsync(int userId)
{
    var response = await httpClient.GetStringAsync($"/users/{userId}");
    return response;
}
// Compiler generates state machine; overhead: ~200 bytes allocation
```

**WITHOUT async/await (direct Task return):**
```csharp
public Task<string> GetUserAsync(int userId)
{
    return httpClient.GetStringAsync($"/users/{userId}");
}
// No state machine; just returns the Task directly; more efficient
```

Both versions work identically from the caller's perspective, but the second is more efficient when you're just passing through the Task without additional work.

---

## Pass-Through Methods

### When You DON'T Need Async/Await

If your method simply calls another async method and returns its Task **without any additional work**, you don't need `async`/`await`:

**❌ Unnecessary async/await (adds overhead):**
```csharp
public async Task<FlightOffer> GetOfferAsync(string flightId)
{
    return await _provider.GetOfferAsync(flightId);
}
```

**✅ Direct Task return (more efficient):**
```csharp
public Task<FlightOffer> GetOfferAsync(string flightId)
{
    return _provider.GetOfferAsync(flightId);
}
```

### When You DO Need Async/Await

Use `async`/`await` when you need to:

1. **Do work before or after the await:**
```csharp
public async Task<FlightOffer> GetOfferAsync(string flightId)
{
    _logger.LogInformation("Fetching offer for {FlightId}", flightId);
    var offer = await _provider.GetOfferAsync(flightId);
    offer.RetrievedAt = DateTime.UtcNow; // Work after await
    return offer;
}
```

2. **Handle exceptions specifically:**
```csharp
public async Task<FlightOffer> GetOfferAsync(string flightId)
{
    try
    {
        return await _provider.GetOfferAsync(flightId);
    }
    catch (HttpRequestException ex)
    {
        _logger.LogError(ex, "Provider request failed");
        throw new ProviderException("Failed to fetch offer", ex);
    }
}
```

3. **Use `using` statements:**
```csharp
public async Task<FlightOffer> GetOfferAsync(string flightId)
{
    using var response = await _httpClient.GetAsync($"/offers/{flightId}");
    return await response.Content.ReadFromJsonAsync<FlightOffer>();
}
```

**Why the difference matters:** The `async` keyword causes the compiler to generate a state machine, which adds a small overhead (~200 bytes heap allocation). For simple pass-through scenarios, this is unnecessary.

---

## Await in Catch Blocks

### Modern C# (7.0+): Await is Allowed in Catch/Finally

Since C# 7.0, you **can** use `await` inside `catch` and `finally` blocks:

```csharp
public async Task<FlightOffer> GetOfferAsync(string flightId, CancellationToken cancellationToken)
{
    try
    {
        var response = await _httpClient.GetAsync($"/offers/{flightId}", cancellationToken);
        return await response.Content.ReadFromJsonAsync<FlightOffer>(cancellationToken);
    }
    catch (HttpRequestException ex)
    {
        // ✅ This is ALLOWED in modern C# (7.0+)
        await _logger.LogErrorAsync("Provider request failed", ex);
        throw;
    }
    finally
    {
        // ✅ This is also ALLOWED
        await _metrics.RecordRequestAsync();
    }
}
```

### Restrictions and Pitfalls

1. **Await and Lock Statements:**
   You still **cannot** await inside a `lock` statement:
   ```csharp
   lock (_syncObject)
   {
       await SomeMethodAsync(); // ❌ COMPILE ERROR
   }
   ```
   
   **Solution:** Use `SemaphoreSlim` for async synchronization:
   ```csharp
   await _semaphore.WaitAsync();
   try
   {
       await SomeMethodAsync(); // ✅ OK
   }
   finally
   {
       _semaphore.Release();
   }
   ```

2. **Exception Filter Restrictions:**
   You cannot await in exception filters (`when` clauses):
   ```csharp
   try { ... }
   catch (Exception ex) when (await ShouldHandleAsync(ex)) // ❌ COMPILE ERROR
   { ... }
   ```

3. **Performance Consideration:**
   Awaiting in catch blocks generates more complex state machines. For very hot paths, consider:
   ```csharp
   // Store exception info in normal flow, handle after try/catch
   Exception? caughtException = null;
   FlightOffer? result = null;
   
   try
   {
       result = await _provider.GetOfferAsync(flightId);
   }
   catch (Exception ex)
   {
       caughtException = ex;
   }
   
   if (caughtException != null)
   {
       await _logger.LogErrorAsync(caughtException);
       throw caughtException;
   }
   
   return result!;
   ```

### Recommended Patterns

For most cases, **directly awaiting in catch is fine and readable:**

```csharp
try
{
    return await DoWorkAsync(cancellationToken);
}
catch (OperationCanceledException)
{
    await _logger.LogWarningAsync("Operation was cancelled");
    throw;
}
catch (Exception ex)
{
    await _logger.LogErrorAsync("Operation failed", ex);
    throw new CustomException("Wrapped error", ex);
}
```

---

## Task.WhenAll for Aggregation

### Parallel Execution and Aggregation

`Task.WhenAll` allows you to run multiple async operations **in parallel** and wait for all of them to complete:

### Basic Example

```csharp
public async Task<FlightOfferSummary> GetBestOfferAsync(string flightId, CancellationToken cancellationToken)
{
    // Start all provider calls in parallel (don't await yet)
    var provider1Task = _provider1.GetOfferAsync(flightId, cancellationToken);
    var provider2Task = _provider2.GetOfferAsync(flightId, cancellationToken);
    var provider3Task = _provider3.GetOfferAsync(flightId, cancellationToken);
    
    // Wait for ALL to complete
    var offers = await Task.WhenAll(provider1Task, provider2Task, provider3Task);
    
    // Find the best offer
    var bestOffer = offers.MinBy(o => o.Price);
    return new FlightOfferSummary 
    { 
        BestPrice = bestOffer.Price,
        TotalProviders = offers.Length,
        AllOffers = offers
    };
}
```

### Key Points

1. **Parallel Execution:**
   - All tasks start immediately when created (if the method is called)
   - They run concurrently (not sequentially)
   - Total time ≈ time of slowest task (not sum of all times)

2. **Exception Handling:**
   - If **any** task fails, `Task.WhenAll` throws the **first** exception
   - Other exceptions are available in the `Task.Exception.InnerExceptions` collection
   ```csharp
   try
   {
       var results = await Task.WhenAll(task1, task2, task3);
   }
   catch (Exception ex)
   {
       // ex is the FIRST exception thrown
       // To see all exceptions:
       var allExceptions = Task.WhenAll(task1, task2, task3).Exception?.InnerExceptions;
   }
   ```

3. **Cancellation:**
   - All tasks should receive the same `CancellationToken`
   - When cancelled, `Task.WhenAll` will throw `OperationCanceledException`
   - Individual tasks continue until they check their token

### Advanced: Handling Partial Failures

If you want to continue even when some tasks fail:

```csharp
public async Task<FlightOfferSummary> GetAvailableOffersAsync(string flightId, CancellationToken cancellationToken)
{
    var tasks = new[]
    {
        _provider1.GetOfferAsync(flightId, cancellationToken),
        _provider2.GetOfferAsync(flightId, cancellationToken),
        _provider3.GetOfferAsync(flightId, cancellationToken)
    };
    
    // Wait for all to complete (whether successful or not)
    await Task.WhenAll(tasks);
    
    // Collect successful results
    var successfulOffers = tasks
        .Where(t => t.IsCompletedSuccessfully)
        .Select(t => t.Result)
        .ToList();
    
    // Log failures
    var failures = tasks
        .Where(t => t.IsFaulted)
        .Select(t => t.Exception)
        .ToList();
    
    foreach (var failure in failures)
    {
        _logger.LogWarning(failure, "Provider failed");
    }
    
    return new FlightOfferSummary 
    { 
        AvailableOffers = successfulOffers,
        FailedProviders = failures.Count
    };
}
```

### Real-World Flight Offers Example

```csharp
// Aggregate offers from multiple providers with timeout and cancellation
public async Task<AggregatedOffers> GetAggregatedOffersAsync(
    string origin, 
    string destination, 
    DateOnly date,
    CancellationToken cancellationToken)
{
    var providers = new[] { "Provider1", "Provider2", "Provider3", "Provider4" };
    
    // Create tasks for all providers
    var providerTasks = providers.Select(async provider =>
    {
        try
        {
            var stopwatch = Stopwatch.StartNew();
            var offer = await CallProviderAsync(provider, origin, destination, date, cancellationToken);
            stopwatch.Stop();
            
            return new ProviderResult
            {
                Provider = provider,
                Offer = offer,
                Success = true,
                ResponseTime = stopwatch.Elapsed
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Provider {Provider} failed", provider);
            return new ProviderResult
            {
                Provider = provider,
                Success = false,
                Error = ex.Message
            };
        }
    }).ToArray();
    
    // Wait for all providers (successful or not)
    var results = await Task.WhenAll(providerTasks);
    
    var successful = results.Where(r => r.Success).ToList();
    var bestOffer = successful.MinBy(r => r.Offer!.Price);
    
    return new AggregatedOffers
    {
        TotalProviders = providers.Length,
        SuccessfulProviders = successful.Count,
        BestOffer = bestOffer?.Offer,
        AllOffers = successful.Select(r => r.Offer!).ToList(),
        AverageResponseTime = successful.Average(r => r.ResponseTime.TotalMilliseconds)
    };
}
```

---

## Running the Examples

### Prerequisites

- .NET 10.0 SDK
- Docker (optional, for containerized testing)

### Running Locally

1. **Navigate to the project directory:**
   ```bash
   cd async-calls/AsyncCallsDemo
   ```

2. **Run the application:**
   ```bash
   dotnet run
   ```

3. **The API will start on HTTP (typically http://localhost:5000)**

4. **Test the endpoints:**
   ```bash
   # View available endpoints
   curl http://localhost:5000/
   
   # Test provider endpoint
   curl http://localhost:5000/provider/pricing?delayMs=3000
   ```

### Running with Docker

1. **Build the Docker image:**
   ```bash
   cd async-calls
   docker build -t async-calls-demo -f Dockerfile .
   ```

2. **Run the container:**
   ```bash
   docker run -p 8080:8080 async-calls-demo
   ```

3. **Test the endpoints:**
   ```bash
   curl http://localhost:8080/
   ```

### Using Docker Compose

1. **Start the services:**
   ```bash
   cd async-calls
   docker-compose up
   ```

2. **Stop the services:**
   ```bash
   docker-compose down
   ```

---

## Testing Cancellation

### Test PoC A: Cancellation Token Propagation

**Bad endpoint (does NOT propagate cancellation):**
```bash
# Start the request and abort after 2 seconds (provider delays 5s)
curl --max-time 2 http://localhost:5000/offers/bad

# Watch the logs - you'll see the provider call CONTINUES even after client aborts!
# Provider log: "Pricing request completed successfully" (even though client is gone)
```

**Good endpoint (properly propagates cancellation):**
```bash
# Start the request and abort after 2 seconds
curl --max-time 2 http://localhost:5000/offers/good

# Watch the logs - provider call is CANCELLED when client aborts
# Provider log: "Pricing request was CANCELLED"
```

### Test PoC B: Loop Cancellation

**Bad endpoint (ignores cancellation in loop):**
```bash
# Process 30 days (6 seconds total), abort after 2 seconds
curl --max-time 2 http://localhost:5000/combinations/bad?days=30

# Watch logs - loop continues to completion even after client abort
```

**Good endpoint (checks cancellation in loop):**
```bash
# Process 30 days, abort after 2 seconds
curl --max-time 2 http://localhost:5000/combinations/good?days=30

# Watch logs - loop stops immediately when client aborts
# Log: "Processing CANCELLED after X days"
```

### Test PoC C: Linked Cancellation (Timeout)

**Trigger server timeout:**
```bash
# Set 2-second timeout, but provider delays 5 seconds
curl http://localhost:5000/offers/timeout?timeoutMs=2000&providerDelayMs=5000

# Response: 408 Request Timeout
# Log: "Request TIMED OUT after 2000ms"
```

**Trigger client cancellation:**
```bash
# Client abort before server timeout
curl --max-time 1 http://localhost:5000/offers/timeout?timeoutMs=5000&providerDelayMs=3000

# Log: "Request was CANCELLED by client"
```

### Test Task.WhenAll Aggregation

**Successful aggregation:**
```bash
# Call 3 providers in parallel, each takes 2 seconds
# Total time: ~2 seconds (not 6 seconds!)
time curl http://localhost:5000/offers/aggregate?providerCount=3&providerDelayMs=2000

# Response includes results from all 3 providers
# Log shows all 3 starting at once and completing around the same time
```

**Cancelled aggregation:**
```bash
# Start aggregation of 5 providers (10 seconds total), abort after 3 seconds
curl --max-time 3 http://localhost:5000/offers/aggregate?providerCount=5&providerDelayMs=2000

# Log: "Aggregation was CANCELLED"
```

### Advanced Testing with Multiple Terminals

**Terminal 1: Start the app with detailed logging:**
```bash
cd async-calls/AsyncCallsDemo
DOTNET_ENVIRONMENT=Development dotnet run --urls "http://localhost:5000"
```

**Terminal 2: Send test requests:**
```bash
# Test bad cancellation
curl --max-time 2 http://localhost:5000/offers/bad

# Wait and observe logs, then test good cancellation
curl --max-time 2 http://localhost:5000/offers/good
```

**Terminal 3: Monitor with watch (Linux/Mac):**
```bash
watch -n 1 'curl -s http://localhost:5000/ | jq'
```

---

## Proof of Concepts

### PoC A: Cancellation Token Propagation

**Demonstrates:** The critical importance of passing `CancellationToken` to downstream async calls.

**Endpoints:**
- `/offers/bad` - Does NOT propagate cancellation token
- `/offers/good` - Properly propagates cancellation token

**Learning Points:**
- Without propagation, downstream calls continue wasting resources after client disconnects
- HttpClient and all async APIs should receive the cancellation token
- Proper propagation enables efficient resource cleanup across the call chain

### PoC B: Cooperative Cancellation in Loops

**Demonstrates:** How to make long-running loops responsive to cancellation.

**Endpoints:**
- `/combinations/bad?days=30` - Does NOT check cancellation in loop
- `/combinations/good?days=30` - Properly checks cancellation token

**Learning Points:**
- Loops must explicitly check `cancellationToken.ThrowIfCancellationRequested()`
- Pass cancellation token to all async operations (`Task.Delay`, I/O calls, etc.)
- Cooperative cancellation enables responsive applications

### PoC C: Linked Cancellation Tokens

**Demonstrates:** Combining multiple cancellation sources (client abort OR server timeout).

**Endpoints:**
- `/offers/timeout?timeoutMs=2000&providerDelayMs=5000` - Server-side timeout with linked token

**Learning Points:**
- `CancellationTokenSource.CreateLinkedTokenSource()` combines multiple cancellation sources
- Use `CancelAfter()` for timeouts
- Check which token triggered cancellation to provide appropriate response
- Return 408 Request Timeout for server timeouts, propagate OperationCanceledException for client aborts

### PoC D: Task.WhenAll Aggregation

**Demonstrates:** Parallel execution and result aggregation.

**Endpoints:**
- `/offers/aggregate?providerCount=3&providerDelayMs=2000` - Call multiple providers in parallel

**Learning Points:**
- `Task.WhenAll` enables parallel I/O operations
- Total time ≈ slowest operation (not sum of all)
- Dramatically improves performance for independent async operations
- All tasks should receive the same cancellation token

---

## Additional Resources

### Microsoft Documentation
- [Async/Await Best Practices](https://docs.microsoft.com/en-us/archive/msdn-magazine/2013/march/async-await-best-practices-in-asynchronous-programming)
- [Task-based Asynchronous Pattern (TAP)](https://docs.microsoft.com/en-us/dotnet/standard/asynchronous-programming-patterns/task-based-asynchronous-pattern-tap)
- [Cancellation in Managed Threads](https://docs.microsoft.com/en-us/dotnet/standard/threading/cancellation-in-managed-threads)

### Key Takeaways

1. ✅ **Always** propagate `CancellationToken` to downstream async calls
2. ✅ **Always** check cancellation in long-running loops
3. ✅ Use `async`/`await` when you need to do work before/after awaiting or handle exceptions
4. ✅ Use direct Task return for simple pass-through methods (avoid unnecessary overhead)
5. ✅ Use `Task.WhenAll` for parallel operations to improve performance
6. ✅ Use linked cancellation tokens to combine multiple cancellation sources
7. ✅ Modern C# allows `await` in `catch`/`finally` blocks - use it for cleaner code
8. ✅ Use `SemaphoreSlim` instead of `lock` when you need to await inside critical sections

---

## Project Structure

```
async-calls/
├── README.md                  # This file
├── Dockerfile                 # Docker image definition
├── docker-compose.yml         # Docker Compose configuration
└── AsyncCallsDemo/           # .NET 10 Minimal API project
    ├── AsyncCallsDemo.csproj
    ├── Program.cs             # All endpoints and PoCs
    ├── appsettings.json
    └── appsettings.Development.json
```

---

## Contributing

This is a training/demo project. Feel free to extend it with additional examples or scenarios!

## License

This project is for educational purposes.
