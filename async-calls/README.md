# Async Operations and Cancellation Tokens - Training Material

This directory contains comprehensive training material and runnable .NET 10 Minimal API Proof-of-Concepts (PoCs) demonstrating async operations, cancellation tokens, and best practices.

## Table of Contents
- [Scenario: REST API Flight Offer Prices](#scenario-rest-api-flight-offer-prices)
- [Understanding async/await](#understanding-asyncawait)
- [Pass-Through Methods](#pass-through-methods)
- [await in catch Blocks](#await-in-catch-blocks)
- [Task.WhenAll for Parallel Operations](#taskwhenall-for-parallel-operations)
- [Running the Examples](#running-the-examples)
- [Proof of Concepts](#proof-of-concepts)
- [Testing Cancellation](#testing-cancellation)

## Scenario: REST API Flight Offer Prices

Consider a REST API that aggregates flight offers from multiple providers. When a client requests flight prices:

1. **The API receives the request** with a destination, dates, and passenger count
2. **The API queries multiple providers** (airlines, booking sites) in parallel
3. **Each provider call is I/O-bound** - waiting for HTTP responses
4. **The API aggregates results** and returns the best offers

### Key Challenges

**Challenge 1: Client Cancellation**
If the user closes their browser or cancels the request, the API should:
- Stop making new provider calls
- Cancel any in-flight HTTP requests
- Release resources immediately
- Avoid wasting backend capacity on abandoned work

**Challenge 2: Long-Running Operations**
When processing large datasets (e.g., generating all possible flight combinations):
- The operation might take several seconds or minutes
- The cancellation token must be checked periodically in loops
- Without cooperative cancellation, the server continues working on cancelled requests

**Challenge 3: Timeout Budgets**
The API might have a maximum response time (e.g., 5 seconds):
- Even if the client doesn't cancel, the API should stop after the timeout
- This requires linking multiple cancellation sources: client abort OR server timeout

## Understanding async/await

### async/await is Syntactic Sugar

The `async` and `await` keywords are **syntactic sugar** that the compiler transforms into a state machine. They don't create new threads - they allow the current thread to be released while waiting for I/O operations.

#### Without async/await (State Machine Pattern)

```csharp
// What the compiler essentially does behind the scenes
public Task<string> FetchDataManual()
{
    var httpClient = new HttpClient();
    return httpClient.GetStringAsync("https://api.example.com/data")
        .ContinueWith(task => 
        {
            if (task.IsFaulted) throw task.Exception!;
            return task.Result;
        });
}
```

#### With async/await (Readable Code)

```csharp
// Much more readable and maintainable
public async Task<string> FetchData()
{
    var httpClient = new HttpClient();
    return await httpClient.GetStringAsync("https://api.example.com/data");
}
```

**What happens behind the scenes:**
1. When `await` is hit, the compiler creates a state machine
2. The method returns a `Task<string>` immediately (doesn't block)
3. The current thread is released to handle other work
4. When the I/O completes, the continuation runs (possibly on a different thread)
5. The state machine resumes execution after the `await`

### Key Points About async/await

- **Not about parallelism**: `async/await` is primarily for I/O-bound operations, not CPU-bound work
- **Thread efficiency**: Allows the thread pool to serve other requests while waiting
- **Compiler magic**: The compiler generates a state machine that tracks execution state
- **Exceptions flow naturally**: Try/catch works as expected (unlike with continuations)

#### Example: Why async/await Matters for Web APIs

```csharp
// BAD: Synchronous I/O blocks the thread
app.MapGet("/data", () =>
{
    var httpClient = new HttpClient();
    var result = httpClient.GetStringAsync("https://slow-api.com/data").Result; // BLOCKS!
    return result;
});

// GOOD: Async I/O releases the thread
app.MapGet("/data", async () =>
{
    var httpClient = new HttpClient();
    var result = await httpClient.GetStringAsync("https://slow-api.com/data"); // Doesn't block!
    return result;
});
```

In the BAD example, if 100 requests arrive and each takes 2 seconds:
- **Synchronous**: Needs 100 threads, all blocked for 2 seconds = massive memory overhead
- **Asynchronous**: Might use only 10-20 threads total, efficiently shared across all requests

## Pass-Through Methods

When a method simply returns another async method's `Task` without doing any additional work before or after the `await`, you **don't need `async`/`await`**.

### When NOT to use async/await

```csharp
// ❌ UNNECESSARY: Adding async/await overhead for no reason
public async Task<string> GetDataAsync(string url)
{
    return await httpClient.GetStringAsync(url);
}

// ✅ BETTER: Direct pass-through, no state machine overhead
public Task<string> GetDataAsync(string url)
{
    return httpClient.GetStringAsync(url);
}
```

### When you MUST use async/await

```csharp
// ✅ REQUIRED: Need to await to use try/catch
public async Task<string> GetDataWithErrorHandlingAsync(string url)
{
    try
    {
        return await httpClient.GetStringAsync(url);
    }
    catch (HttpRequestException ex)
    {
        logger.LogError(ex, "Failed to fetch data");
        throw;
    }
}

// ✅ REQUIRED: Need to await to do work before/after
public async Task<string> GetDataWithLoggingAsync(string url)
{
    logger.LogInformation("Fetching data from {Url}", url);
    var result = await httpClient.GetStringAsync(url);
    logger.LogInformation("Successfully fetched data");
    return result;
}

// ✅ REQUIRED: Need to await to use the result
public async Task<int> GetDataLengthAsync(string url)
{
    var data = await httpClient.GetStringAsync(url);
    return data.Length;
}
```

### Why Does This Matter?

The `async` keyword causes the compiler to generate a state machine, which has a small overhead:
- Memory allocation for the state machine
- Additional method calls
- Slightly more complex stack traces

For simple pass-through methods, this overhead is unnecessary. However, the performance difference is minimal in most cases, and readability should be prioritized.

## await in catch Blocks

Modern C# (since C# 6) **allows `await` in `catch` and `finally` blocks**, which wasn't possible in earlier versions.

### What's Allowed

```csharp
// ✅ ALLOWED: await in catch block
public async Task<string> FetchWithFallbackAsync(string primaryUrl, string fallbackUrl)
{
    try
    {
        return await httpClient.GetStringAsync(primaryUrl);
    }
    catch (HttpRequestException ex)
    {
        logger.LogWarning(ex, "Primary source failed, trying fallback");
        // This is perfectly valid in modern C#
        return await httpClient.GetStringAsync(fallbackUrl);
    }
}

// ✅ ALLOWED: await in finally block
public async Task ProcessDataAsync()
{
    try
    {
        await ProcessAsync();
    }
    finally
    {
        // Cleanup that involves async I/O
        await CloseConnectionsAsync();
    }
}
```

### Common Pitfalls

#### Pitfall 1: Swallowing Exceptions with .Result or .Wait()

```csharp
// ❌ BAD: Swallows the original exception, throws AggregateException
public async Task<string> FetchDataBad()
{
    try
    {
        return await httpClient.GetStringAsync("https://api.example.com/data");
    }
    catch (HttpRequestException)
    {
        // BAD: Blocks and wraps exception in AggregateException
        return fallbackClient.GetStringAsync("https://fallback.example.com/data").Result;
    }
}

// ✅ GOOD: Properly awaits
public async Task<string> FetchDataGood()
{
    try
    {
        return await httpClient.GetStringAsync("https://api.example.com/data");
    }
    catch (HttpRequestException)
    {
        // GOOD: Properly awaits
        return await fallbackClient.GetStringAsync("https://fallback.example.com/data");
    }
}
```

#### Pitfall 2: Not Propagating Cancellation Tokens

```csharp
// ❌ BAD: catch block doesn't propagate cancellation token
public async Task<string> FetchWithFallbackBad(CancellationToken cancellationToken)
{
    try
    {
        return await httpClient.GetStringAsync("https://api.example.com/data", cancellationToken);
    }
    catch (HttpRequestException)
    {
        // BAD: Fallback doesn't respect cancellation
        return await fallbackClient.GetStringAsync("https://fallback.example.com/data");
    }
}

// ✅ GOOD: Propagates cancellation token everywhere
public async Task<string> FetchWithFallbackGood(CancellationToken cancellationToken)
{
    try
    {
        return await httpClient.GetStringAsync("https://api.example.com/data", cancellationToken);
    }
    catch (HttpRequestException)
    {
        // GOOD: Fallback respects cancellation
        return await fallbackClient.GetStringAsync("https://fallback.example.com/data", cancellationToken);
    }
}
```

#### Pitfall 3: Catching OperationCanceledException in catch blocks

```csharp
// ⚠️ CAREFUL: Don't catch OperationCanceledException unless you intend to handle cancellation
public async Task<string> FetchDataCareful(CancellationToken cancellationToken)
{
    try
    {
        return await httpClient.GetStringAsync("https://api.example.com/data", cancellationToken);
    }
    catch (Exception ex) // Too broad! Catches OperationCanceledException
    {
        logger.LogError(ex, "Request failed");
        return "default";
    }
}

// ✅ BETTER: Let cancellation exceptions flow through
public async Task<string> FetchDataBetter(CancellationToken cancellationToken)
{
    try
    {
        return await httpClient.GetStringAsync("https://api.example.com/data", cancellationToken);
    }
    catch (HttpRequestException ex) // Specific exception
    {
        logger.LogError(ex, "Request failed");
        return "default";
    }
    // OperationCanceledException is NOT caught and will propagate
}
```

### Recommended Pattern for Error Handling with Async

```csharp
public async Task<string> RobustFetchAsync(string url, CancellationToken cancellationToken)
{
    try
    {
        return await httpClient.GetStringAsync(url, cancellationToken);
    }
    catch (OperationCanceledException)
    {
        // Cancellation is expected, log and re-throw
        logger.LogInformation("Request was cancelled");
        throw;
    }
    catch (HttpRequestException ex)
    {
        // Specific HTTP errors
        logger.LogError(ex, "HTTP request failed for {Url}", url);
        throw;
    }
    catch (Exception ex)
    {
        // Unexpected errors
        logger.LogError(ex, "Unexpected error fetching {Url}", url);
        throw;
    }
}
```

## Task.WhenAll for Parallel Operations

`Task.WhenAll` allows you to run multiple async operations in parallel and wait for all of them to complete. This is essential for aggregating results from multiple sources efficiently.

### Flight Offers Example

```csharp
public async Task<List<FlightOffer>> GetBestOffersAsync(
    FlightRequest request, 
    CancellationToken cancellationToken)
{
    // Start all provider queries in parallel (they all run concurrently)
    var airline1Task = QueryAirlineAsync("Airline1", request, cancellationToken);
    var airline2Task = QueryAirlineAsync("Airline2", request, cancellationToken);
    var bookingSiteTask = QueryBookingSiteAsync("BookingSite", request, cancellationToken);
    
    // Wait for all to complete
    var results = await Task.WhenAll(airline1Task, airline2Task, bookingSiteTask);
    
    // Aggregate and return best offers
    return results
        .SelectMany(offers => offers)
        .OrderBy(offer => offer.Price)
        .Take(10)
        .ToList();
}
```

### Key Points

1. **Starts all tasks immediately**: The tasks begin execution as soon as they're created
2. **Waits for all**: `Task.WhenAll` completes when ALL tasks complete
3. **Total time ≈ max(individual times)**: NOT the sum of all times
4. **Propagates all exceptions**: If any task fails, `Task.WhenAll` throws `AggregateException`
5. **Cancellation propagates**: If cancelled, all tasks with the same token are notified

### Performance Comparison

```csharp
// ❌ SLOW: Sequential execution (6 seconds total if each takes 2 seconds)
var result1 = await QueryAirline1Async(); // 2 seconds
var result2 = await QueryAirline2Async(); // 2 seconds  
var result3 = await QueryBookingSiteAsync(); // 2 seconds

// ✅ FAST: Parallel execution (~2 seconds total for all three)
var results = await Task.WhenAll(
    QueryAirline1Async(),
    QueryAirline2Async(),
    QueryBookingSiteAsync()
);
```

### Handling Individual Failures

If you want to handle failures individually (don't fail all if one fails):

```csharp
public async Task<List<FlightOffer>> GetOffersWithFaultToleranceAsync(
    FlightRequest request,
    CancellationToken cancellationToken)
{
    var tasks = new[]
    {
        SafeQueryAsync("Provider1", request, cancellationToken),
        SafeQueryAsync("Provider2", request, cancellationToken),
        SafeQueryAsync("Provider3", request, cancellationToken)
    };
    
    var results = await Task.WhenAll(tasks);
    
    return results
        .Where(r => r != null)
        .SelectMany(r => r!)
        .OrderBy(offer => offer.Price)
        .ToList();
}

private async Task<List<FlightOffer>?> SafeQueryAsync(
    string provider,
    FlightRequest request,
    CancellationToken cancellationToken)
{
    try
    {
        return await QueryProviderAsync(provider, request, cancellationToken);
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Provider {Provider} failed", provider);
        return null; // Return null instead of throwing
    }
}
```

## Running the Examples

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Docker (optional, for containerized deployment)

### Running Locally

1. Navigate to the project directory:
```bash
cd async-calls/AsyncCallsApi
```

2. Run the application:
```bash
dotnet run
```

3. The API will start on `http://localhost:5000`

### Running with Docker

1. Build the Docker image:
```bash
cd async-calls
docker build -t async-calls-api .
```

2. Run the container:
```bash
docker run -p 5000:5000 async-calls-api
```

3. The API will be accessible at `http://localhost:5000`

### Using Docker Compose

```bash
cd async-calls
docker-compose up
```

## Proof of Concepts

The API implements several endpoints demonstrating different aspects of async operations and cancellation:

### Provider Endpoint (Simulated External Service)

- `GET /provider/pricing?delayMs=2000` - Simulates a slow external pricing API

### PoC A: Cancellation Token Propagation

Demonstrates the importance of propagating cancellation tokens to async operations.

**Endpoints:**
- `GET /offers/bad` - Does NOT propagate cancellation token to HttpClient
- `GET /offers/good` - Properly propagates cancellation token to HttpClient

**Key Learning:**
- Without propagation: When you cancel the request, the downstream HTTP call continues, wasting resources
- With propagation: Cancellation is cooperative and immediate, releasing resources quickly

### PoC B: Cooperative Cancellation in Loops

Demonstrates checking cancellation tokens in long-running loops.

**Endpoints:**
- `GET /combinations/bad` - Does NOT check cancellation in loop iterations
- `GET /combinations/good` - Checks cancellation token at each iteration

**Key Learning:**
- Without checking: The entire loop must complete even if the client cancels
- With checking: The loop exits immediately when cancellation is requested

### PoC C: Linked Cancellation Tokens

Demonstrates combining multiple cancellation sources (client abort OR server timeout).

**Endpoints:**
- `GET /offers/timeout?timeoutMs=3000` - Uses linked cancellation tokens

**Key Learning:**
- Links request cancellation token with a timeout cancellation token
- Operation cancels on EITHER condition: client abort OR timeout
- Logs indicate which condition triggered the cancellation

### Task.WhenAll: Parallel Aggregation

Demonstrates querying multiple providers in parallel and aggregating results.

**Endpoints:**
- `GET /offers/parallel` - Queries 3 providers in parallel using Task.WhenAll

**Key Learning:**
- All providers are queried simultaneously (not sequentially)
- Total time ≈ max(individual times), not sum
- Demonstrates real-world scenario of aggregating flight offers

## Testing Cancellation

### Using curl with Timeout

#### Test PoC A - Bad (No cancellation propagation)

```bash
# Start the request, then press Ctrl+C after ~2 seconds
curl http://localhost:5000/offers/bad

# OR use curl's timeout (will abort after 2 seconds)
curl --max-time 2 http://localhost:5000/offers/bad
```

**Observe the logs**: The downstream HTTP call to `/provider/pricing` continues even after you cancel!

#### Test PoC A - Good (With cancellation propagation)

```bash
# Start the request, then press Ctrl+C after ~2 seconds
curl http://localhost:5000/offers/good

# OR use curl's timeout
curl --max-time 2 http://localhost:5000/offers/good
```

**Observe the logs**: The downstream HTTP call is cancelled immediately!

#### Test PoC B - Bad (No cancellation checking in loop)

```bash
# Cancel after ~3 seconds (loop processes 10 days, 1 second each)
curl --max-time 3 http://localhost:5000/combinations/bad
```

**Observe the logs**: The loop continues processing all days even after cancellation!

#### Test PoC B - Good (With cancellation checking)

```bash
# Cancel after ~3 seconds
curl --max-time 3 http://localhost:5000/combinations/good
```

**Observe the logs**: The loop exits immediately when cancelled!

#### Test PoC C - Timeout

```bash
# This should timeout (provider delays 5s, timeout is 3s)
curl http://localhost:5000/offers/timeout?timeoutMs=3000

# This should succeed (provider delays 1s, timeout is 3s)
curl "http://localhost:5000/offers/timeout?timeoutMs=3000" &
# In another terminal, call the provider with a short delay:
curl "http://localhost:5000/provider/pricing?delayMs=1000"
```

**Observe the logs**: Check whether timeout or client cancellation triggered!

#### Test Task.WhenAll - Parallel Aggregation

```bash
# This queries 3 providers in parallel
curl http://localhost:5000/offers/parallel

# Observe the response time: should be ~2 seconds (the slowest provider)
# NOT 4.5 seconds (sum of 1s + 1.5s + 2s)
```

**Observe the logs**: All providers are queried simultaneously!

### Expected Log Output

When testing cancellation, you should see logs like:

**PoC A - Bad (no propagation):**
```
[PoC A - BAD] Starting request (NOT propagating cancellation token)
[Provider] Request received. Will delay 5000ms
<< CLIENT CANCELS HERE >>
[Provider] Request completed successfully after 5000ms  ← Still completes!
```

**PoC A - Good (with propagation):**
```
[PoC A - GOOD] Starting request (propagating cancellation token)
[Provider] Request received. Will delay 5000ms
<< CLIENT CANCELS HERE >>
[Provider] Request was cancelled before completion  ← Cancelled cooperatively!
[PoC A - GOOD] Request was cancelled after ~2000ms (cancellation was cooperative!)
```

**PoC B - Good (with cancellation checking):**
```
[PoC B - GOOD] Starting combinations processing
[PoC B - GOOD] Processing day 1
[PoC B - GOOD] Processing day 2
[PoC B - GOOD] Processing day 3
<< CLIENT CANCELS HERE >>
[PoC B - GOOD] Cancelled after ~3000ms, processed 3 days (responded quickly!)
```

**PoC C - Timeout:**
```
[PoC C] Starting request with 3000ms timeout
[Provider] Request received. Will delay 5000ms
<< TIMEOUT TRIGGERS AFTER 3 SECONDS >>
[Provider] Request was cancelled before completion
[PoC C] Request timed out after ~3000ms (timeout was 3000ms)
```

## Summary

This training material demonstrates:

1. ✅ **async/await is syntactic sugar** - The compiler generates state machines for cleaner async code
2. ✅ **Pass-through methods** - Don't need async/await if just returning a Task
3. ✅ **await in catch blocks** - Fully supported in modern C#, with common pitfalls to avoid
4. ✅ **Task.WhenAll** - Essential for parallel I/O operations and aggregation
5. ✅ **Cancellation token propagation** - Critical for responsive, resource-efficient APIs
6. ✅ **Cooperative cancellation in loops** - Check tokens periodically in long operations
7. ✅ **Linked cancellation tokens** - Combine multiple cancellation sources elegantly

These patterns are essential for building high-performance, scalable, and user-friendly .NET APIs!
