using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Api.Data;

var builder = WebApplication.CreateBuilder(args);

// Add CORS for cross-origin requests
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
});

// Add DbContext
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("Default"),
        npgsql => npgsql.MigrationsAssembly("Api")
    )
);

// Add logging
builder.Logging.AddConsole();

var app = builder.Build();

app.UseCors();

// 1x1 WHITE PNG tracking pixel (base64 encoded)
// PNG 1x1 white
var trackingPixel = Convert.FromBase64String(
    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMBAAZ7A/8AAAAASUVORK5CYII="
);

// Main analytics endpoint - returns tracking pixel
app.MapGet("/www/e.png", async (HttpContext context, ILogger<Program> logger) =>
{
    var query = context.Request.Query;
    // Support both modern and legacy param names
    var target = query["Target"].ToString();
    if (string.IsNullOrWhiteSpace(target)) target = query["ctx"].ToString();
    var eventTypeValue = query["EventType"].ToString();
    if (string.IsNullOrWhiteSpace(eventTypeValue)) eventTypeValue = query["evt"].ToString();

    // Validate required parameters like original ecsv2 (after mapping)
    if (string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(eventTypeValue))
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        context.Response.ContentType = "text/plain";
        await context.Response.WriteAsync("Target and EventType parameters are required");
        return;
    }

    var analyticsEvent = new AnalyticsEvent
    {
        ActionType = query["aType"].ToString(),
        Field = query["field"].ToString(),
        EventType = eventTypeValue,
        Context = query["ctx"].ToString(),
        Url = query["url"].ToString(),
        LocalTime = query["lt"].ToString(),
        Timestamp = DateTime.UtcNow,
        IpAddress = context.Connection.RemoteIpAddress?.ToString(),
        UserAgent = context.Request.Headers["User-Agent"].ToString()
    };
    
    // Log the analytics event
    logger.LogInformation(
        "Analytics Event: Target={Target}, Type={EventType}, Action={ActionType}, Context={Context}, Field={Field}, URL={Url}, Time={LocalTime}",
        target,
        analyticsEvent.EventType,
        analyticsEvent.ActionType,
        analyticsEvent.Context,
        analyticsEvent.Field,
        analyticsEvent.Url,
        analyticsEvent.LocalTime
    );
    
    // Store event (in production, this would go to a database)
    AnalyticsStore.AddEvent(analyticsEvent);
    
    // Return 1x1 transparent PNG
    context.Response.ContentType = "image/png";
    context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
    context.Response.Headers["Pragma"] = "no-cache";
    context.Response.Headers["Expires"] = "0";
    
    await context.Response.Body.WriteAsync(trackingPixel);
});

// Additional endpoint for studio events
app.MapGet("/pe", async (HttpContext context, ILogger<Program> logger) =>
{
    var query = context.Request.Query;
    var eventType = query["t"].ToString();
    
    logger.LogInformation("PE Event: Type={Type}", eventType);
    
    context.Response.ContentType = "image/png";
    await context.Response.Body.WriteAsync(trackingPixel);
});

// Debug endpoint to view collected analytics
app.MapGet("/api/analytics", (int? limit) =>
{
    var events = AnalyticsStore.GetRecentEvents(limit ?? 100);
    return Results.Json(events);
});

// Root and /www should return the same error payload as Roblox ecsv2
app.MapGet("/", () => Results.Json(new { errors = new[] { new { code = 0, message = "" } } }));
app.MapGet("/www", () => Results.Json(new { errors = new[] { new { code = 0, message = "" } } }));

// Explicit catch-all under /www to avoid 404s for file-like paths (e.g., /www/e.pno)
app.MapMethods("/www/{*path}", new[] { HttpMethods.Get, HttpMethods.Head },
    () => Results.Json(new { errors = new[] { new { code = 0, message = "" } } })
);

// Fallback for any unknown route: mimic ecsv2 behavior instead of 404
app.MapFallback((HttpContext ctx) =>
    Results.Json(new { errors = new[] { new { code = 0, message = "" } } })
);

app.Run();

// Analytics event model
public class AnalyticsEvent
{
    public string? ActionType { get; set; }  // aType parameter
    public string? Field { get; set; }        // field parameter
    public string? EventType { get; set; }    // evt parameter
    public string? Context { get; set; }      // ctx parameter
    public string? Url { get; set; }          // url parameter
    public string? LocalTime { get; set; }    // lt parameter
    public DateTime Timestamp { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
}

// Simple in-memory store for analytics events
public static class AnalyticsStore
{
    private static readonly List<AnalyticsEvent> _events = new();
    private static readonly object _lock = new();
    private const int MaxEvents = 10000;
    
    public static void AddEvent(AnalyticsEvent evt)
    {
        lock (_lock)
        {
            _events.Add(evt);
            
            // Keep only recent events to prevent memory issues
            if (_events.Count > MaxEvents)
            {
                _events.RemoveRange(0, _events.Count - MaxEvents);
            }
        }
    }
    
    public static List<AnalyticsEvent> GetRecentEvents(int count)
    {
        lock (_lock)
        {
            return _events
                .OrderByDescending(e => e.Timestamp)
                .Take(count)
                .ToList();
        }
    }
}

[JsonSerializable(typeof(AnalyticsEvent[]))]
[JsonSerializable(typeof(AnalyticsEvent))]
internal partial class AppJsonSerializerContext : JsonSerializerContext
{
}
