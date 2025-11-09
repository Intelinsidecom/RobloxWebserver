using Microsoft.EntityFrameworkCore;
using Api.Data;
using Api.Controllers;
using System.Security.Claims;
using Npgsql;
using Microsoft.AspNetCore.HttpOverrides;
using Thumbnails;
using Microsoft.AspNetCore.Authentication;
using Website.Auth;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services
    .AddControllersWithViews()
    .AddApplicationPart(typeof(LoginController).Assembly);
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("Default"),
        npgsql => npgsql.MigrationsAssembly("Api")
    )
);
// Thumbnails service
builder.Services.AddSingleton<IThumbnailService>(sp => new ThumbnailService(sp.GetRequiredService<IConfiguration>()));

// Minimal auth: use the ClaimsPrincipal you set from .ROBLOSECURITY as the auth source
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = "Passthrough";
    options.DefaultChallengeScheme = "Passthrough";
    options.DefaultSignInScheme = "Passthrough";
})
.AddScheme<AuthenticationSchemeOptions, PassthroughAuthHandler>("Passthrough", options => { });
// Respect reverse proxy headers (e.g., Cloudflare) so Request.Scheme/IsHttps are accurate
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedHost;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});
var enableRequestLogging = builder.Configuration.GetValue<bool>("Features:EnableRequestLogging");
if (enableRequestLogging)
{
    builder.Services.AddHttpLogging(options =>
    {
        options.LoggingFields = Microsoft.AspNetCore.HttpLogging.HttpLoggingFields.RequestPropertiesAndHeaders |
                                 Microsoft.AspNetCore.HttpLogging.HttpLoggingFields.ResponsePropertiesAndHeaders |
                                 Microsoft.AspNetCore.HttpLogging.HttpLoggingFields.RequestBody |
                                 Microsoft.AspNetCore.HttpLogging.HttpLoggingFields.ResponseBody;
        options.RequestBodyLogLimit = 4096;
        options.ResponseBodyLogLimit = 4096;
    });
}

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

// Apply forwarded headers BEFORE HTTPS redirection/static files/routing
app.UseForwardedHeaders();

app.UseHttpsRedirection();

var provider = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
// Add mapping for .file extension
provider.Mappings[".file"] = "application/octet-stream";

app.UseStaticFiles(new StaticFileOptions
{
    ServeUnknownFileTypes = true,
    DefaultContentType = "application/octet-stream",
    ContentTypeProvider = provider
});

app.UseRouting();

if (enableRequestLogging)
{
    app.UseHttpLogging();
}

// Translate .ROBLOSECURITY into HttpContext.User by looking up sessions table
app.Use(async (context, next) =>
{
    var cookies = context.Request.Cookies;
    if (cookies.TryGetValue(".ROBLOSECURITY", out var raw))
    {
        var connStr = context.RequestServices.GetRequiredService<IConfiguration>().GetConnectionString("Default");
        if (!string.IsNullOrWhiteSpace(connStr))
        {
            try
            {
                await using var conn = new NpgsqlConnection(connStr);
                await conn.OpenAsync();
                await using var cmd = new NpgsqlCommand("select user_id from sessions where token = @t and (expires_at is null or expires_at > now() at time zone 'utc')", conn);
                cmd.Parameters.AddWithValue("t", raw);
                var obj = await cmd.ExecuteScalarAsync();
                if (obj is long uid && uid > 0)
                {
                    var claims = new[]
                    {
                        new Claim(ClaimTypes.NameIdentifier, uid.ToString()),
                        new Claim(ClaimTypes.Name, $"User_{uid}")
                    };
                    var identity = new ClaimsIdentity(claims, "Cookie");
                    context.User = new ClaimsPrincipal(identity);
                }
            }
            catch { /* ignore lookup errors */ }
        }
    }
    await next();
});

app.UseAuthentication();
app.UseAuthorization();

// Enable attribute-routed controllers (e.g., AuthGateController for /, /login, /newlogin)
app.MapControllers();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// Pages routing: placed AFTER default so specific controllers (e.g., /login/v1) win first
app.MapControllerRoute(
    name: "pages",
    pattern: "{*path}",
    defaults: new { controller = "Pages", action = "Route" }
);

app.Run();
