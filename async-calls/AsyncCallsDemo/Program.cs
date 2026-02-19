var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddOpenApi();
builder.Services.AddHttpClient();

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

var logger = app.Logger;

// ===========================================================================================
// FAKE PROVIDER ENDPOINTS (for testing)
// ===========================================================================================

// Simulates a slow external pricing provider
app.MapGet("/provider/pricing", async (int delayMs = 3000, CancellationToken cancellationToken = default) =>
{
    var requestId = Guid.NewGuid().ToString()[..8];
    logger.LogInformation("[Provider {RequestId}] Pricing request received, will delay {DelayMs}ms", requestId, delayMs);
    
    try
    {
        await Task.Delay(delayMs, cancellationToken);
        logger.LogInformation("[Provider {RequestId}] Pricing request completed successfully", requestId);
        return Results.Ok(new { price = Random.Shared.Next(200, 1000), currency = "USD", providerId = requestId });
    }
    catch (OperationCanceledException)
    {
        logger.LogWarning("[Provider {RequestId}] Pricing request was CANCELLED", requestId);
        throw;
    }
});

// ===========================================================================================
// POC A: Cancellation Token Propagation
// Demonstrates the importance of propagating cancellation tokens to downstream calls
// ===========================================================================================

// BAD: Does NOT propagate cancellation token to HttpClient
app.MapGet("/offers/bad", async (HttpContext httpContext, IHttpClientFactory clientFactory) =>
{
    var requestId = Guid.NewGuid().ToString()[..8];
    logger.LogInformation("[BAD {RequestId}] Request started", requestId);
    
    var httpClient = clientFactory.CreateClient();
    
    try
    {
        // NOT passing cancellationToken to GetAsync - the downstream call will continue even if client aborts!
        var response = await httpClient.GetAsync($"http://localhost:{httpContext.Connection.LocalPort}/provider/pricing?delayMs=5000");
        var content = await response.Content.ReadAsStringAsync();
        
        logger.LogInformation("[BAD {RequestId}] Request completed successfully", requestId);
        return Results.Ok(new { message = "Flight offers retrieved", data = content });
    }
    catch (Exception ex)
    {
        logger.LogError("[BAD {RequestId}] Request failed: {Error}", requestId, ex.Message);
        throw;
    }
})
.WithName("GetOffersBad");

// GOOD: Properly propagates cancellation token
app.MapGet("/offers/good", async (HttpContext httpContext, IHttpClientFactory clientFactory, CancellationToken cancellationToken) =>
{
    var requestId = Guid.NewGuid().ToString()[..8];
    logger.LogInformation("[GOOD {RequestId}] Request started", requestId);
    
    var httpClient = clientFactory.CreateClient();
    
    try
    {
        // Properly passing cancellationToken - downstream call will be cancelled if client aborts
        var response = await httpClient.GetAsync(
            $"http://localhost:{httpContext.Connection.LocalPort}/provider/pricing?delayMs=5000", 
            cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        
        logger.LogInformation("[GOOD {RequestId}] Request completed successfully", requestId);
        return Results.Ok(new { message = "Flight offers retrieved", data = content });
    }
    catch (OperationCanceledException)
    {
        logger.LogWarning("[GOOD {RequestId}] Request was CANCELLED by client", requestId);
        throw;
    }
    catch (Exception ex)
    {
        logger.LogError("[GOOD {RequestId}] Request failed: {Error}", requestId, ex.Message);
        throw;
    }
})
.WithName("GetOffersGood");

// ===========================================================================================
// POC B: Cooperative Cancellation in Loops
// Demonstrates checking cancellation token in long-running loops
// ===========================================================================================

// BAD: Does NOT check cancellation token in loop
app.MapGet("/combinations/bad", async (int days = 30) =>
{
    var requestId = Guid.NewGuid().ToString()[..8];
    logger.LogInformation("[LOOP-BAD {RequestId}] Processing {Days} days of combinations", requestId, days);
    
    var results = new List<string>();
    
    // Simulate processing multiple days - this will NOT respond to cancellation!
    for (int day = 1; day <= days; day++)
    {
        await Task.Delay(200); // Simulate work per day (200ms each)
        results.Add($"Day {day}: Processed 100 flight combinations");
        
        if (day % 10 == 0)
        {
            logger.LogInformation("[LOOP-BAD {RequestId}] Processed {Day}/{Days} days", requestId, day, days);
        }
    }
    
    logger.LogInformation("[LOOP-BAD {RequestId}] All combinations processed", requestId);
    return Results.Ok(new { message = "All combinations processed", totalDays = days, results });
})
.WithName("GetCombinationsBad");

// GOOD: Checks cancellation token in loop
app.MapGet("/combinations/good", async (int days = 30, CancellationToken cancellationToken = default) =>
{
    var requestId = Guid.NewGuid().ToString()[..8];
    logger.LogInformation("[LOOP-GOOD {RequestId}] Processing {Days} days of combinations", requestId, days);
    
    var results = new List<string>();
    
    try
    {
        // Properly checking cancellation token in loop
        for (int day = 1; day <= days; day++)
        {
            cancellationToken.ThrowIfCancellationRequested(); // Check before work
            
            await Task.Delay(200, cancellationToken); // Pass token to async operations
            results.Add($"Day {day}: Processed 100 flight combinations");
            
            if (day % 10 == 0)
            {
                logger.LogInformation("[LOOP-GOOD {RequestId}] Processed {Day}/{Days} days", requestId, day, days);
            }
        }
        
        logger.LogInformation("[LOOP-GOOD {RequestId}] All combinations processed", requestId);
        return Results.Ok(new { message = "All combinations processed", totalDays = days, results });
    }
    catch (OperationCanceledException)
    {
        logger.LogWarning("[LOOP-GOOD {RequestId}] Processing CANCELLED after {Count} days", requestId, results.Count);
        throw;
    }
})
.WithName("GetCombinationsGood");

// ===========================================================================================
// POC C: Linked Cancellation Tokens
// Demonstrates combining request cancellation with server-side timeout
// ===========================================================================================

app.MapGet("/offers/timeout", async (HttpContext httpContext, IHttpClientFactory clientFactory, 
    int timeoutMs = 2000, int providerDelayMs = 5000, CancellationToken cancellationToken = default) =>
{
    var requestId = Guid.NewGuid().ToString()[..8];
    logger.LogInformation("[TIMEOUT {RequestId}] Request started with {TimeoutMs}ms timeout, provider delay {ProviderDelayMs}ms", 
        requestId, timeoutMs, providerDelayMs);
    
    // Create a linked cancellation token: triggered by EITHER client abort OR server timeout
    using var timeoutCts = new CancellationTokenSource();
    timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(timeoutMs));
    
    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
    
    var httpClient = clientFactory.CreateClient();
    
    try
    {
        var response = await httpClient.GetAsync(
            $"http://localhost:{httpContext.Connection.LocalPort}/provider/pricing?delayMs={providerDelayMs}", 
            linkedCts.Token);
        var content = await response.Content.ReadAsStringAsync(linkedCts.Token);
        
        logger.LogInformation("[TIMEOUT {RequestId}] Request completed successfully", requestId);
        return Results.Ok(new { message = "Flight offers retrieved", data = content });
    }
    catch (OperationCanceledException)
    {
        if (timeoutCts.Token.IsCancellationRequested)
        {
            logger.LogWarning("[TIMEOUT {RequestId}] Request TIMED OUT after {TimeoutMs}ms", requestId, timeoutMs);
            return Results.StatusCode(StatusCodes.Status408RequestTimeout);
        }
        else
        {
            logger.LogWarning("[TIMEOUT {RequestId}] Request was CANCELLED by client", requestId);
            throw;
        }
    }
    catch (Exception ex)
    {
        logger.LogError("[TIMEOUT {RequestId}] Request failed: {Error}", requestId, ex.Message);
        throw;
    }
})
.WithName("GetOffersWithTimeout");

// ===========================================================================================
// Task.WhenAll Example: Parallel Aggregation
// Demonstrates calling multiple providers in parallel and aggregating results
// ===========================================================================================

app.MapGet("/offers/aggregate", async (HttpContext httpContext, IHttpClientFactory clientFactory, 
    int providerCount = 3, int providerDelayMs = 2000, CancellationToken cancellationToken = default) =>
{
    var requestId = Guid.NewGuid().ToString()[..8];
    logger.LogInformation("[AGGREGATE {RequestId}] Calling {Count} providers in parallel", requestId, providerCount);
    
    var httpClient = clientFactory.CreateClient();
    var baseUrl = $"http://localhost:{httpContext.Connection.LocalPort}/provider/pricing?delayMs={providerDelayMs}";
    
    try
    {
        // Create multiple parallel tasks to call different providers
        var tasks = Enumerable.Range(1, providerCount)
            .Select(async i =>
            {
                logger.LogInformation("[AGGREGATE {RequestId}] Starting provider {ProviderId} call", requestId, i);
                var response = await httpClient.GetAsync(baseUrl, cancellationToken);
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogInformation("[AGGREGATE {RequestId}] Provider {ProviderId} completed", requestId, i);
                return new { providerId = i, result = content };
            })
            .ToArray();
        
        // Wait for ALL tasks to complete (or any to fail/cancel)
        var results = await Task.WhenAll(tasks);
        
        logger.LogInformation("[AGGREGATE {RequestId}] All {Count} providers completed successfully", requestId, providerCount);
        
        return Results.Ok(new 
        { 
            message = $"Aggregated results from {providerCount} providers", 
            totalProviders = providerCount,
            results = results 
        });
    }
    catch (OperationCanceledException)
    {
        logger.LogWarning("[AGGREGATE {RequestId}] Aggregation was CANCELLED", requestId);
        throw;
    }
    catch (Exception ex)
    {
        logger.LogError("[AGGREGATE {RequestId}] Aggregation failed: {Error}", requestId, ex.Message);
        throw;
    }
})
.WithName("GetOffersAggregate");

// ===========================================================================================
// Info endpoint
// ===========================================================================================

app.MapGet("/", () => Results.Ok(new
{
    message = "Async/Await Cancellation Demo API",
    endpoints = new
    {
        provider = "/provider/pricing?delayMs=3000",
        pocA_bad = "/offers/bad (does NOT propagate cancellation)",
        pocA_good = "/offers/good (properly propagates cancellation)",
        pocB_bad = "/combinations/bad?days=30 (does NOT check cancellation in loop)",
        pocB_good = "/combinations/good?days=30 (checks cancellation in loop)",
        pocC = "/offers/timeout?timeoutMs=2000&providerDelayMs=5000 (linked cancellation)",
        whenAll = "/offers/aggregate?providerCount=3&providerDelayMs=2000 (Task.WhenAll)"
    }
}))
.WithName("GetInfo");

app.Run();
