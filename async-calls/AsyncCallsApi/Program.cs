using System.Diagnostics;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddOpenApi();
builder.Services.AddHttpClient();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

var logger = app.Logger;

// =============================================================================
// PROVIDER ENDPOINTS (Simulated external services)
// =============================================================================

// Provider endpoint that simulates a slow external pricing API
app.MapGet("/provider/pricing", async (HttpContext context, int delayMs = 2000) =>
{
    var cancellationToken = context.RequestAborted;
    logger.LogInformation("[Provider] Request received. Will delay {DelayMs}ms", delayMs);
    
    try
    {
        await Task.Delay(delayMs, cancellationToken);
        logger.LogInformation("[Provider] Request completed successfully after {DelayMs}ms", delayMs);
        return Results.Ok(new { price = Random.Shared.Next(100, 1000), currency = "USD", timestamp = DateTime.UtcNow });
    }
    catch (OperationCanceledException)
    {
        logger.LogWarning("[Provider] Request was cancelled before completion");
        return Results.StatusCode(499); // Client Closed Request
    }
});

// =============================================================================
// POC A: Cancellation Token Propagation
// =============================================================================

// BAD: Does NOT propagate cancellation token to HttpClient
app.MapGet("/offers/bad", async (IHttpClientFactory httpClientFactory, HttpContext context) =>
{
    var stopwatch = Stopwatch.StartNew();
    logger.LogInformation("[PoC A - BAD] Starting request (NOT propagating cancellation token)");
    
    var httpClient = httpClientFactory.CreateClient();
    
    try
    {
        // NOTE: We're NOT passing context.RequestAborted to GetAsync
        var response = await httpClient.GetAsync("http://localhost:5000/provider/pricing?delayMs=5000");
        var content = await response.Content.ReadAsStringAsync();
        
        stopwatch.Stop();
        logger.LogInformation("[PoC A - BAD] Request completed in {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
        return Results.Ok(new { result = content, elapsedMs = stopwatch.ElapsedMilliseconds });
    }
    catch (OperationCanceledException)
    {
        stopwatch.Stop();
        logger.LogWarning("[PoC A - BAD] Request was cancelled after {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
        return Results.StatusCode(499);
    }
    catch (Exception ex)
    {
        stopwatch.Stop();
        logger.LogError(ex, "[PoC A - BAD] Request failed after {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
        return Results.Problem("Request failed");
    }
});

// GOOD: Propagates cancellation token to HttpClient
app.MapGet("/offers/good", async (IHttpClientFactory httpClientFactory, HttpContext context) =>
{
    var stopwatch = Stopwatch.StartNew();
    var cancellationToken = context.RequestAborted;
    logger.LogInformation("[PoC A - GOOD] Starting request (propagating cancellation token)");
    
    var httpClient = httpClientFactory.CreateClient();
    
    try
    {
        // GOOD: Passing cancellationToken to GetAsync
        var response = await httpClient.GetAsync("http://localhost:5000/provider/pricing?delayMs=5000", cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        
        stopwatch.Stop();
        logger.LogInformation("[PoC A - GOOD] Request completed in {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
        return Results.Ok(new { result = content, elapsedMs = stopwatch.ElapsedMilliseconds });
    }
    catch (OperationCanceledException)
    {
        stopwatch.Stop();
        logger.LogWarning("[PoC A - GOOD] Request was cancelled after {ElapsedMs}ms (cancellation was cooperative!)", stopwatch.ElapsedMilliseconds);
        return Results.StatusCode(499);
    }
    catch (Exception ex)
    {
        stopwatch.Stop();
        logger.LogError(ex, "[PoC A - GOOD] Request failed after {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
        return Results.Problem("Request failed");
    }
});

// =============================================================================
// POC B: Cooperative Cancellation in Loops
// =============================================================================

// BAD: Does NOT check cancellation token in loop
app.MapGet("/combinations/bad", async (HttpContext context) =>
{
    var stopwatch = Stopwatch.StartNew();
    logger.LogInformation("[PoC B - BAD] Starting combinations processing (NOT checking cancellation)");
    
    var results = new List<string>();
    
    try
    {
        // Simulate processing multiple days of data
        for (int day = 1; day <= 10; day++)
        {
            logger.LogInformation("[PoC B - BAD] Processing day {Day}", day);
            
            // Simulate expensive work per day
            await Task.Delay(1000); // NOT passing cancellation token
            
            results.Add($"Day {day}: Processed {Random.Shared.Next(100, 500)} combinations");
        }
        
        stopwatch.Stop();
        logger.LogInformation("[PoC B - BAD] Completed all processing in {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
        return Results.Ok(new { results, elapsedMs = stopwatch.ElapsedMilliseconds });
    }
    catch (OperationCanceledException)
    {
        stopwatch.Stop();
        logger.LogWarning("[PoC B - BAD] Cancelled after {ElapsedMs}ms, processed {Count} days", 
            stopwatch.ElapsedMilliseconds, results.Count);
        return Results.StatusCode(499);
    }
});

// GOOD: Checks cancellation token in loop
app.MapGet("/combinations/good", async (HttpContext context) =>
{
    var stopwatch = Stopwatch.StartNew();
    var cancellationToken = context.RequestAborted;
    logger.LogInformation("[PoC B - GOOD] Starting combinations processing (checking cancellation)");
    
    var results = new List<string>();
    
    try
    {
        // Simulate processing multiple days of data
        for (int day = 1; day <= 10; day++)
        {
            // GOOD: Check cancellation at the start of each iteration
            cancellationToken.ThrowIfCancellationRequested();
            
            logger.LogInformation("[PoC B - GOOD] Processing day {Day}", day);
            
            // Simulate expensive work per day, passing cancellation token
            await Task.Delay(1000, cancellationToken);
            
            results.Add($"Day {day}: Processed {Random.Shared.Next(100, 500)} combinations");
        }
        
        stopwatch.Stop();
        logger.LogInformation("[PoC B - GOOD] Completed all processing in {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
        return Results.Ok(new { results, elapsedMs = stopwatch.ElapsedMilliseconds });
    }
    catch (OperationCanceledException)
    {
        stopwatch.Stop();
        logger.LogWarning("[PoC B - GOOD] Cancelled after {ElapsedMs}ms, processed {Count} days (responded quickly!)", 
            stopwatch.ElapsedMilliseconds, results.Count);
        return Results.StatusCode(499);
    }
});

// =============================================================================
// POC C: Linked Cancellation Tokens (Request Abort OR Timeout)
// =============================================================================

app.MapGet("/offers/timeout", async (IHttpClientFactory httpClientFactory, HttpContext context, int timeoutMs = 3000) =>
{
    var stopwatch = Stopwatch.StartNew();
    var requestToken = context.RequestAborted;
    
    // Create a timeout cancellation token
    using var timeoutCts = new CancellationTokenSource();
    timeoutCts.CancelAfter(timeoutMs);
    
    // Link both tokens: request cancellation OR timeout
    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(requestToken, timeoutCts.Token);
    
    logger.LogInformation("[PoC C] Starting request with {TimeoutMs}ms timeout", timeoutMs);
    
    var httpClient = httpClientFactory.CreateClient();
    
    try
    {
        // Use the linked token which will cancel on EITHER condition
        var response = await httpClient.GetAsync("http://localhost:5000/provider/pricing?delayMs=5000", linkedCts.Token);
        var content = await response.Content.ReadAsStringAsync(linkedCts.Token);
        
        stopwatch.Stop();
        logger.LogInformation("[PoC C] Request completed in {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
        return Results.Ok(new { result = content, elapsedMs = stopwatch.ElapsedMilliseconds });
    }
    catch (OperationCanceledException)
    {
        stopwatch.Stop();
        
        // Determine which token caused the cancellation
        if (timeoutCts.Token.IsCancellationRequested)
        {
            logger.LogWarning("[PoC C] Request timed out after {ElapsedMs}ms (timeout was {TimeoutMs}ms)", 
                stopwatch.ElapsedMilliseconds, timeoutMs);
            return Results.Problem("Request timed out", statusCode: 504);
        }
        else
        {
            logger.LogWarning("[PoC C] Request was cancelled by client after {ElapsedMs}ms", 
                stopwatch.ElapsedMilliseconds);
            return Results.StatusCode(499);
        }
    }
    catch (Exception ex)
    {
        stopwatch.Stop();
        logger.LogError(ex, "[PoC C] Request failed after {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
        return Results.Problem("Request failed");
    }
});

// =============================================================================
// TASK.WHENALL: Parallel Aggregation Example
// =============================================================================

app.MapGet("/offers/parallel", async (IHttpClientFactory httpClientFactory, HttpContext context) =>
{
    var stopwatch = Stopwatch.StartNew();
    var cancellationToken = context.RequestAborted;
    
    logger.LogInformation("[Task.WhenAll] Starting parallel provider requests");
    
    var httpClient = httpClientFactory.CreateClient();
    
    try
    {
        // Query multiple providers in parallel
        var provider1Task = FetchProviderPriceAsync(httpClient, "Provider1", 1000, cancellationToken);
        var provider2Task = FetchProviderPriceAsync(httpClient, "Provider2", 1500, cancellationToken);
        var provider3Task = FetchProviderPriceAsync(httpClient, "Provider3", 2000, cancellationToken);
        
        // Wait for all tasks to complete
        var results = await Task.WhenAll(provider1Task, provider2Task, provider3Task);
        
        stopwatch.Stop();
        logger.LogInformation("[Task.WhenAll] All requests completed in {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
        
        return Results.Ok(new 
        { 
            offers = results, 
            elapsedMs = stopwatch.ElapsedMilliseconds,
            note = "All 3 providers were queried in parallel. Total time is ~max(individual times), not sum!"
        });
    }
    catch (OperationCanceledException)
    {
        stopwatch.Stop();
        logger.LogWarning("[Task.WhenAll] Requests were cancelled after {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
        return Results.StatusCode(499);
    }
    catch (Exception ex)
    {
        stopwatch.Stop();
        logger.LogError(ex, "[Task.WhenAll] Requests failed after {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
        return Results.Problem("One or more provider requests failed");
    }
});

async Task<ProviderOffer> FetchProviderPriceAsync(HttpClient httpClient, string providerName, int delayMs, CancellationToken cancellationToken)
{
    var sw = Stopwatch.StartNew();
    logger.LogInformation("[Task.WhenAll] Fetching from {Provider} (simulated delay: {DelayMs}ms)", providerName, delayMs);
    
    var response = await httpClient.GetAsync($"http://localhost:5000/provider/pricing?delayMs={delayMs}", cancellationToken);
    var content = await response.Content.ReadFromJsonAsync<PricingResponse>(cancellationToken);
    
    sw.Stop();
    logger.LogInformation("[Task.WhenAll] {Provider} responded in {ElapsedMs}ms", providerName, sw.ElapsedMilliseconds);
    
    return new ProviderOffer(providerName, content?.price ?? 0, content?.currency ?? "USD", sw.ElapsedMilliseconds);
}

app.Run();

// =============================================================================
// MODELS
// =============================================================================

record PricingResponse(int price, string currency, DateTime timestamp);
record ProviderOffer(string Provider, int Price, string Currency, long ResponseTimeMs);
