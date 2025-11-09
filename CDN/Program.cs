using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using System.IO.Compression;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.AddResponseCompression(o =>
{
    o.EnableForHttps = true;
    o.Providers.Add<GzipCompressionProvider>();
    o.Providers.Add<BrotliCompressionProvider>();
});
builder.Services.Configure<GzipCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);
builder.Services.Configure<BrotliCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);

var app = builder.Build();

app.UseResponseCompression();

var assetsRoot = builder.Configuration["Cdn:AssetsRoot"] ?? Path.Combine(AppContext.BaseDirectory, "Assets");
Directory.CreateDirectory(assetsRoot);

// Simple root check
app.MapGet("/", () => Results.Text("OK", "text/plain"));

app.MapGet("/health", () => Results.Ok(new { ok = true }));

// Catch-all route: serve direct path under assets root if it exists;
// otherwise, if only a filename is provided, search subfolders (e.g. thumbnails) and serve first match.
app.MapGet("/{**path}", (HttpContext http, string? path) =>
{
    path ??= string.Empty;

    // For empty path, just return OK (root check above handles "/").
    if (path.Length == 0)
        return Results.Text("OK", "text/plain");

    // Prevent path traversal
    if (path.Contains(".."))
        return Results.BadRequest();

    // Try direct file under assets root
    var normalized = path.Replace('/', Path.DirectorySeparatorChar);
    var directFile = Path.Combine(assetsRoot, normalized);
    if (File.Exists(directFile))
    {
        var provider = new FileExtensionContentTypeProvider();
        if (!provider.TryGetContentType(directFile, out var ctDirect)) ctDirect = "application/octet-stream";
        http.Response.Headers.CacheControl = "public,max-age=31536000,immutable";
        return Results.File(directFile, contentType: ctDirect, enableRangeProcessing: true);
    }

    // If a single filename (no additional directory segments) was requested, search recursively
    var fileName = Path.GetFileName(path);
    if (!string.IsNullOrWhiteSpace(fileName) && fileName == path)
    {
        var match = Directory.EnumerateFiles(assetsRoot, fileName, SearchOption.AllDirectories).FirstOrDefault();
        if (match is not null)
        {
            var provider = new FileExtensionContentTypeProvider();
            if (!provider.TryGetContentType(match, out var ct)) ct = "application/octet-stream";
            http.Response.Headers.CacheControl = "public,max-age=31536000,immutable";
            return Results.File(match, contentType: ct, enableRangeProcessing: true);
        }
    }

    return Results.NotFound();
});

app.Run();
