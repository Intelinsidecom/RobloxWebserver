using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Thumbnails;
using Users;

namespace Website.Controllers;

[ApiController]
[Route("thumbnail")]
public class ThumbnailCompatController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly IThumbnailService _thumbnailService;

    public ThumbnailCompatController(IConfiguration configuration, IThumbnailService thumbnailService)
    {
        _configuration = configuration;
        _thumbnailService = thumbnailService;
    }

    // GET /thumbnail/avatar-headshot?userId=123
    [HttpGet("avatar-headshot")]
    public async Task<IActionResult> AvatarHeadshot([FromQuery] long userId)
    {
        if (userId <= 0)
            return BadRequest(new { error = "userId is required" });
        try
        {
            var connStr = _configuration.GetConnectionString("Default");
            if (string.IsNullOrWhiteSpace(connStr))
                return Problem("Database not configured");

            var exists = await UserQueries.UserExistsAsync(connStr, userId);
            if (!exists)
                return NotFound(new { error = "User not found" });

            var (found, url) = await TryGetThumbnailUrlAsync(userId);
            if (!found)
            {
                // Render synchronously and persist
                var save = await _thumbnailService.RenderAvatarAsync("headshot", userId);
                var baseUrl = _configuration["Thumbnails:ThumbnailUrl"];
                if (string.IsNullOrWhiteSpace(baseUrl))
                {
                    var scheme = string.IsNullOrWhiteSpace(Request.Scheme) ? "http" : Request.Scheme;
                    var host = Request.Host.HasValue ? Request.Host.Value : "localhost";
                    baseUrl = $"{scheme}://{host}/";
                }
                var fullUrl = CombineUrl(baseUrl!, save.FileName);
                await PersistThumbnailUrlAsync(userId, fullUrl);
                return Ok(new { Final = true, Url = fullUrl });
            }
            return Ok(new { Final = true, Url = url });
        }
        catch (Exception ex)
        {
            return Problem(ex.Message);
        }
    }

    // GET /thumbnail/avatar-headshots?userIds=1,2,3
    [HttpGet("avatar-headshots")]
    public async Task<IActionResult> AvatarHeadshots([FromQuery] string userIds)
    {
        if (string.IsNullOrWhiteSpace(userIds))
            return BadRequest(new { error = "userIds is required" });

        try
        {
            var ids = userIds
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => long.TryParse(s, out var id) ? id : 0)
                .Where(id => id > 0)
                .Distinct()
                .ToArray();

            var results = new List<object>(ids.Length);

            var map = await BulkGetThumbnailUrlsAsync(ids);

            foreach (var id in ids)
            {
                if (map.TryGetValue(id, out var url) && !string.IsNullOrWhiteSpace(url))
                {
                    results.Add(new { userId = id, Final = true, Url = url });
                }
                else
                {
                    // Render synchronously and persist per user
                    var save = await _thumbnailService.RenderAvatarAsync("headshot", id);
                    var baseUrl = _configuration["Thumbnails:ThumbnailUrl"];
                    if (string.IsNullOrWhiteSpace(baseUrl))
                    {
                        var scheme = string.IsNullOrWhiteSpace(Request.Scheme) ? "http" : Request.Scheme;
                        var host = Request.Host.HasValue ? Request.Host.Value : "localhost";
                        baseUrl = $"{scheme}://{host}/";
                    }
                    var fullUrl = CombineUrl(baseUrl!, save.FileName);
                    await PersistThumbnailUrlAsync(id, fullUrl);
                    results.Add(new { userId = id, Final = true, Url = fullUrl });
                }
            }

            return Ok(results);
        }
        catch (Exception ex)
        {
            return Problem(ex.Message);
        }
    }

    private async Task<(bool found, string? url)> TryGetThumbnailUrlAsync(long userId)
    {
        var connStr = _configuration.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(connStr))
            return (false, null);

        var s = await ThumbnailQueries.GetUserHeadshotUrlAsync(connStr, userId);
        if (!string.IsNullOrWhiteSpace(s))
            return (true, s);
        return (false, null);
    }

    private async Task<Dictionary<long, string?>> BulkGetThumbnailUrlsAsync(long[] ids)
    {
        var map = new Dictionary<long, string?>();
        var connStr = _configuration.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(connStr) || ids.Length == 0)
            return map;

        var result = await ThumbnailQueries.GetUserHeadshotUrlsAsync(connStr, ids);
        foreach (var kv in result)
            map[kv.Key] = kv.Value;
        return map;
    }

    private async Task PersistThumbnailUrlAsync(long userId, string url)
    {
        var connStr = _configuration.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(connStr)) return;
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        await using var up = new NpgsqlCommand("update users set headshot_url = @u where user_id = @id", conn);
        up.Parameters.AddWithValue("u", url);
        up.Parameters.AddWithValue("id", userId);
        await up.ExecuteNonQueryAsync();
    }

    private static string CombineUrl(string baseUrl, string relative)
    {
        if (string.IsNullOrEmpty(baseUrl)) return relative;
        if (string.IsNullOrEmpty(relative)) return baseUrl;
        var trimmedBase = baseUrl.EndsWith("/") ? baseUrl : baseUrl + "/";
        return trimmedBase + relative.TrimStart('/');
    }
}
