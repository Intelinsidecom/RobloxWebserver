 using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Api.Middleware;
using Api.Services;
using Microsoft.EntityFrameworkCore;
using Api.Data;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpLogging(o =>
{
    o.LoggingFields = HttpLoggingFields.RequestPropertiesAndHeaders | HttpLoggingFields.ResponsePropertiesAndHeaders | HttpLoggingFields.RequestBody | HttpLoggingFields.ResponseBody;
    o.RequestBodyLogLimit = 1048576;
    o.ResponseBodyLogLimit = 1048576;
    o.MediaTypeOptions.AddText("application/json");
    o.MediaTypeOptions.AddText("text/plain");
    o.MediaTypeOptions.AddText("application/x-www-form-urlencoded");
    o.MediaTypeOptions.AddText("text/*");
});
// Honor X-Forwarded-* headers from reverse proxy (e.g., Cloudflare)
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});
// Ensure HTTPS redirection knows the external HTTPS port
builder.Services.AddHttpsRedirection(options =>
{
    options.HttpsPort = 443;
});
builder.Services.AddHostedService<ConsoleKeyListenerHostedService>();
builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default"));
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<RequestResponseLoggingMiddleware>();

// Apply forwarded headers BEFORE HTTPS redirection
app.UseForwardedHeaders();
app.UseHttpsRedirection();

app.UseAuthorization();

// Only emit the default JSON body for 404 responses
app.Use(async (context, next) =>
{
    await next();
    if (context.Response.StatusCode == StatusCodes.Status404NotFound && !context.Response.HasStarted)
    {
        context.Response.ContentType = "application/json; charset=utf-8";
        var payload = new { errors = new[] { new { code = 0, message = string.Empty } } };
        var json = System.Text.Json.JsonSerializer.Serialize(payload);
        await context.Response.WriteAsync(json);
    }
});

app.MapControllers();

app.MapGet("/", () => Results.Json(new { errors = new[] { new { code = 0, message = string.Empty } } }));

app.Run();
