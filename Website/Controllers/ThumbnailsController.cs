using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Thumbnails;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using System.Security.Claims;
using Npgsql;
using Users;
using System.IO;

namespace Website.Controllers;

[ApiController]
public class ThumbnailsController : ControllerBase
{
    private readonly IThumbnailService _thumbnailService;
    private readonly IConfiguration _configuration;

    public ThumbnailsController(IThumbnailService thumbnailService, IConfiguration configuration)
    {
        _thumbnailService = thumbnailService;
        _configuration = configuration;
    }

    // Legacy endpoint used by AjaxAvatarThumbnail.js
    // GET /thumbs/rawavatar.ashx?UserID=<id>&ThumbnailFormatID=<fmt>
    [HttpGet("thumbs/rawavatar.ashx")]
    public async Task<IActionResult> RawAvatar([FromQuery] long UserID, [FromQuery] int ThumbnailFormatID)
    {
        try
        {
            if (UserID <= 0)
                return BadRequest(new { error = "UserID is required" });
            var connStr = _configuration.GetConnectionString("Default");
            if (string.IsNullOrWhiteSpace(connStr))
                return Content("ERROR: DB_NOT_CONFIGURED", "text/plain");

            var exists = await UserQueries.UserExistsAsync(connStr, UserID);
            if (!exists)
                return Content("ERROR: USER_NOT_FOUND", "text/plain");

            var url = await ThumbnailQueries.GetUserThumbnailUrlAsync(connStr, UserID);

            if (!string.IsNullOrWhiteSpace(url))
            {
                return Content(url!, "text/plain");
            }
            // Legacy polling contract: do not trigger rendering here
            return Content("PENDING", "text/plain");
        }
        catch (Exception ex)
        {
            return Content("ERROR: " + ex.Message, "text/plain");
        }
    }

    // Disabled duplicate: handled by AvatarV1Controller
    [NonAction]
    public async Task<IActionResult> RedrawThumbnail([FromQuery] string? type, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(type))
            return BadRequest(new { error = "type is required" });
        var renderType = type.Trim().ToLowerInvariant();

        var idStr = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(idStr) || !long.TryParse(idStr, out var targetUserId) || targetUserId <= 0)
            return Unauthorized(new { error = "Authentication required" });

        try
        {
            var save = await _thumbnailService.RenderAvatarAsync(renderType, targetUserId, cancellationToken: cancellationToken);
            var hash = save.Hash;
            var baseUrl = _configuration["Thumbnails:ThumbnailUrl"];
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                var scheme = string.IsNullOrWhiteSpace(Request.Scheme) ? "http" : Request.Scheme;
                var host = Request.Host.HasValue ? Request.Host.Value : "localhost";
                baseUrl = $"{scheme}://{host}/";
            }
            var fullUrl = CombineUrl(baseUrl!, save.FileName);

            var connStr = _configuration.GetConnectionString("Default");
            if (!string.IsNullOrWhiteSpace(connStr) && renderType == "headshot")
            {
                await using var conn = new NpgsqlConnection(connStr);
                await conn.OpenAsync(cancellationToken);
                await using var cmd = new NpgsqlCommand("update users set headshot_url = @u where user_id = @id", conn);
                cmd.Parameters.AddWithValue("u", fullUrl);
                cmd.Parameters.AddWithValue("id", targetUserId);
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }

            return Ok(new { hash, thumbnail_url = fullUrl });
        }
        catch (Exception ex)
        {
            return Problem(ex.Message);
        }
    }

    // POST v1/avatar/set-body-colors
    [Authorize]
    [HttpPost("v1/avatar/set-body-colors")]
    public async Task<IActionResult> SetBodyColors([FromBody] BodyColorsModel bodyColorsModel, CancellationToken cancellationToken)
    {
        var idStr = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(idStr) || !long.TryParse(idStr, out var userId) || userId <= 0)
            return Unauthorized(new { error = "Authentication required" });

        var connStr = _configuration.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(connStr))
            return Problem("Database not configured");

        var repo = new BodyColorsRepository();
        var colors = new BodyColorsRepository.BodyColors
        {
            HeadColorId = bodyColorsModel.headColorId,
            TorsoColorId = bodyColorsModel.torsoColorId,
            RightArmColorId = bodyColorsModel.rightArmColorId,
            LeftArmColorId = bodyColorsModel.leftArmColorId,
            RightLegColorId = bodyColorsModel.rightLegColorId,
            LeftLegColorId = bodyColorsModel.leftLegColorId
        };
        await repo.SetBodyColorsAsync(connStr, userId, colors, cancellationToken);

        return Ok(new { success = true });
    }

    // GET /headshot-thumbnail/image
    [HttpGet("headshot-thumbnail/image")]
    public async Task<IActionResult> Headshot([FromQuery] long userId, [FromQuery] int? width, [FromQuery] int? height, [FromQuery] string? format, CancellationToken cancellationToken)
    {
        if (userId <= 0) return BadRequest(new { error = "userId is required" });
        var connStr = _configuration.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(connStr))
            return Problem("Database not configured");

        var exists = await UserQueries.UserExistsAsync(connStr, userId, cancellationToken);
        if (!exists) return NotFound(new { error = "User not found" });

        var url = await ThumbnailQueries.GetUserHeadshotUrlAsync(connStr, userId, cancellationToken);
        if (!string.IsNullOrWhiteSpace(url))
            return Redirect(url);

        // If no headshot URL yet, render now and persist, then redirect
        var save = await _thumbnailService.RenderAvatarAsync("headshot", userId, cancellationToken: cancellationToken);
        var hash = save.Hash;
        var baseUrl = _configuration["Thumbnails:ThumbnailUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            var scheme = string.IsNullOrWhiteSpace(Request.Scheme) ? "http" : Request.Scheme;
            var host = Request.Host.HasValue ? Request.Host.Value : "localhost";
            baseUrl = $"{scheme}://{host}/";
        }
        var fullUrl = CombineUrl(baseUrl!, save.FileName);
        await using (var conn = new NpgsqlConnection(connStr))
        {
            await conn.OpenAsync(cancellationToken);
            await using var cmd = new NpgsqlCommand("update users set headshot_url = @u where user_id = @id", conn);
            cmd.Parameters.AddWithValue("u", fullUrl);
            cmd.Parameters.AddWithValue("id", userId);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        return Redirect(fullUrl);
    }

    // GET /bust-thumbnail/image
    [HttpGet("bust-thumbnail/image")]
    public async Task<IActionResult> Bust([FromQuery] long userId, [FromQuery] int? width, [FromQuery] int? height, [FromQuery] string? format, CancellationToken cancellationToken)
    {
        if (userId <= 0) return BadRequest(new { error = "userId is required" });
        string? hash = null;
        string? existingUrl = null;
        // Try to use existing thumbnail_url to avoid re-rendering
        var connStrCheck = _configuration.GetConnectionString("Default");
        if (!string.IsNullOrWhiteSpace(connStrCheck))
        {
            try
            {
                var exists = await UserQueries.UserExistsAsync(connStrCheck, userId, cancellationToken);
                if (!exists)
                    return NotFound(new { error = "User not found" });

                var s = await ThumbnailQueries.GetUserHeadshotUrlAsync(connStrCheck, userId, cancellationToken);
                if (!string.IsNullOrWhiteSpace(s))
                {
                    existingUrl = s;
                    // Expect URLs like <cdn>/<hash>.png
                    var file = Path.GetFileName(new Uri(s, UriKind.Absolute).AbsolutePath);
                    if (!string.IsNullOrWhiteSpace(file))
                    {
                        var dot = file.IndexOf('.');
                        hash = dot > 0 ? file.Substring(0, dot) : file;
                    }
                }
            }
            catch { /* ignore and fall back */ }
        }
        // If no existing hash, render once to create base image
        if (string.IsNullOrWhiteSpace(hash))
        {
            var save = await _thumbnailService.RenderAvatarAsync("avatar", userId, cancellationToken: cancellationToken);
            hash = save.Hash;
            // Persist the actual base URL for future lookups
            var baseUrlNew = GetCdnBaseUrl();
            var fullUrlNew = CombineUrl(baseUrlNew, save.FileName);
            try
            {
                var connStrPersist = _configuration.GetConnectionString("Default");
                if (!string.IsNullOrWhiteSpace(connStrPersist))
                {
                    await using var conn = new NpgsqlConnection(connStrPersist);
                    await conn.OpenAsync(cancellationToken);
                    await using var cmd = new NpgsqlCommand("update users set headshot_url = @u where user_id = @id", conn);
                    cmd.Parameters.AddWithValue("u", fullUrlNew);
                    cmd.Parameters.AddWithValue("id", userId);
                    await cmd.ExecuteNonQueryAsync(cancellationToken);
                    existingUrl = fullUrlNew;
                }
            }
            catch { /* best effort */ }
        }

        // Resolve output directory where base thumbnails are saved
        var outputDir = _configuration["Thumbnails:OutputDirectory"];
        if (string.IsNullOrWhiteSpace(outputDir))
        {
            // Fallback: ./thumbnails relative to current process
            outputDir = Path.Combine(AppContext.BaseDirectory, "thumbnails");
        }
        Directory.CreateDirectory(outputDir);

        // Base file may be .png or .jpg
        var pngPath = Path.Combine(outputDir, hash + ".png");
        var jpgPath = Path.Combine(outputDir, hash + ".jpg");
        var baseFile = System.IO.File.Exists(pngPath) ? pngPath : (System.IO.File.Exists(jpgPath) ? jpgPath : pngPath);
        // If no resizing/format requested, just redirect to base PNG on CDN
        var targetWidth = width.GetValueOrDefault(0);
        var targetHeight = height.GetValueOrDefault(0);
        var fmt = (format ?? "png").Trim().ToLowerInvariant();
        if (targetWidth <= 0 || targetHeight <= 0)
        {
            // If only format specified (e.g., jpg), convert base PNG without resizing
            if (!string.IsNullOrWhiteSpace(format))
            {
                // If base doesn't exist but we had an existing URL, prefer redirecting to it to avoid rendering work
                if (!System.IO.File.Exists(baseFile) && !string.IsNullOrWhiteSpace(existingUrl))
                {
                    return Redirect(existingUrl);
                }
                var convPath = await ThumbnailDerivatives.EnsureConvertedAsync(outputDir, hash, "bust", fmt, cancellationToken);
                var convName = Path.GetFileName(convPath);
                var convUrl = CombineUrl(GetCdnBaseUrl(), convName);
                return Redirect(convUrl);
            }

            // No format requested; if we have existing URL and base file missing, redirect to existing URL
            if (!System.IO.File.Exists(baseFile) && !string.IsNullOrWhiteSpace(existingUrl))
            {
                return Redirect(existingUrl);
            }
            // Redirect to the actual base file name if we can infer extension
            var baseName = System.IO.File.Exists(jpgPath) ? (hash + ".jpg") : (hash + ".png");
            var url = CombineUrl(GetCdnBaseUrl(), baseName);
            return Redirect(url);
        }
        var derivedPath = await ThumbnailDerivatives.EnsureDerivedAsync(outputDir, hash, "bust", targetWidth, targetHeight, fmt, cancellationToken);
        var derivedName = Path.GetFileName(derivedPath);
        var derivedUrl = CombineUrl(GetCdnBaseUrl(), derivedName);
        return Redirect(derivedUrl);
    }

    // GET /outfit-thumbnail/image
    [HttpGet("outfit-thumbnail/image")]
    public IActionResult Outfit([FromQuery] long userOutfitId, [FromQuery] int? width, [FromQuery] int? height, [FromQuery] string? format)
        => NotFound(new { error = "outfit thumbnails not implemented" });

    // GET /asset-thumbnail/image
    [HttpGet("asset-thumbnail/image")]
    public IActionResult Asset([FromQuery] long assetId, [FromQuery] int? width, [FromQuery] int? height, [FromQuery] string? format)
        => NotFound(new { error = "asset thumbnails not implemented" });

    private string GetCdnBaseUrl()
    {
        var baseUrl = _configuration["Thumbnails:ThumbnailUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            var scheme = string.IsNullOrWhiteSpace(Request.Scheme) ? "http" : Request.Scheme;
            var host = Request.Host.HasValue ? Request.Host.Value : "localhost";
            baseUrl = $"{scheme}://{host}/";
        }
        return baseUrl!;
    }

    private static string CombineUrl(string baseUrl, string relative)
    {
        if (string.IsNullOrEmpty(baseUrl)) return relative;
        if (string.IsNullOrEmpty(relative)) return baseUrl;
        var trimmedBase = baseUrl.EndsWith("/") ? baseUrl : baseUrl + "/";
        return trimmedBase + relative.TrimStart('/');
    }

    public sealed class BodyColorsModel
    {
        public int headColorId { get; set; }
        public int torsoColorId { get; set; }
        public int rightArmColorId { get; set; }
        public int leftArmColorId { get; set; }
        public int rightLegColorId { get; set; }
        public int leftLegColorId { get; set; }
    }
}
