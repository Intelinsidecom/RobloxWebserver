using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Npgsql;
using System;
using System.Threading.Tasks;
using Thumbnails;

namespace Website.Controllers;

[ApiController]
[Route("thumbs")]
public class LegacyThumbsController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly IThumbnailService _thumbnailService;

    public LegacyThumbsController(IConfiguration configuration, IThumbnailService thumbnailService)
    {
        _configuration = configuration;
        _thumbnailService = thumbnailService;
    }

    // Legacy endpoint used by AjaxAvatarThumbnail.js
    // GET /thumbs/rawavatar.ashx?UserID=<id>&ThumbnailFormatID=<fmt>
    [HttpGet("rawavatar.ashx")]
    public async Task<IActionResult> RawAvatar([FromQuery] long UserID, [FromQuery] int ThumbnailFormatID)
    {
        try
        {
            var connStr = _configuration.GetConnectionString("Default");
            if (string.IsNullOrWhiteSpace(connStr))
                return Content("ERROR: DB_NOT_CONFIGURED", "text/plain");

            string? url = null;
            await using (var conn = new NpgsqlConnection(connStr))
            {
                await conn.OpenAsync();
                await using var cmd = new NpgsqlCommand("select thumbnail_url from users where user_id = @id", conn);
                cmd.Parameters.AddWithValue("id", UserID);
                var obj = await cmd.ExecuteScalarAsync();
                if (obj is string s && !string.IsNullOrWhiteSpace(s))
                    url = s;
            }

            if (!string.IsNullOrWhiteSpace(url))
            {
                return Content(url!, "text/plain");
            }
            else
            {
                try
                {
                    string renderType;
                    switch (ThumbnailFormatID)
                    {
                        case 1:
                            renderType = "headshot";
                            break;
                        case 2:
                            renderType = "avatar";
                            break;
                        case 3:
                            renderType = "full";
                            break;
                        default:
                            renderType = "headshot";
                            break;
                    }
                    var hash = await _thumbnailService.RenderAvatarAsync(renderType, UserID);
                    var baseUrl = _configuration["Thumbnails:ThumbnailUrl"];
                    if (string.IsNullOrWhiteSpace(baseUrl))
                    {
                        var scheme = string.IsNullOrWhiteSpace(Request.Scheme) ? "http" : Request.Scheme;
                        var host = Request.Host.HasValue ? Request.Host.Value : "localhost";
                        baseUrl = $"{scheme}://{host}/";
                    }
                    var fullUrl = CombineUrl(baseUrl!, hash + ".png");

                    // Persist for future fast path
                    await using (var conn = new NpgsqlConnection(connStr))
                    {
                        await conn.OpenAsync();
                        await using var up = new NpgsqlCommand("update users set thumbnail_url = @u where user_id = @id", conn);
                        up.Parameters.AddWithValue("u", fullUrl);
                        up.Parameters.AddWithValue("id", UserID);
                        await up.ExecuteNonQueryAsync();
                    }

                    return Content(fullUrl, "text/plain");
                }
                catch
                {
                    // Fall back to legacy polling contract
                    return Content("PENDING", "text/plain");
                }
            }
        }
        catch (Exception ex)
        {
            return Content("ERROR: " + ex.Message, "text/plain");
        }
    }

    private static string CombineUrl(string baseUrl, string relative)
    {
        if (string.IsNullOrEmpty(baseUrl)) return relative;
        if (string.IsNullOrEmpty(relative)) return baseUrl;
        var trimmedBase = baseUrl.EndsWith("/") ? baseUrl : baseUrl + "/";
        return trimmedBase + relative.TrimStart('/');
    }
}
