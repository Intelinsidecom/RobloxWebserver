using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Thumbnails;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Claims;
using Microsoft.Extensions.Configuration;
using Npgsql;
using System;
using Users;

namespace Website.Controllers;

[ApiController]
[Route("v1/avatar")]
public class AvatarV1Controller : ControllerBase
{
    private readonly IThumbnailService _thumbnailService;
    private readonly IConfiguration _configuration;

    public AvatarV1Controller(IThumbnailService thumbnailService, IConfiguration configuration)
    {
        _thumbnailService = thumbnailService;
        _configuration = configuration;
    }

    // POST v1/avatar/redraw-thumbnail?type=headshot
    // Only the authenticated user may redraw their own thumbnail.
    [Authorize]
    [HttpPost("redraw-thumbnail")]
    [HttpGet("redraw-thumbnail")]
    public async Task<IActionResult> RedrawThumbnail([FromQuery] string? type, CancellationToken cancellationToken)
    {
        // Resolve target type
        var renderType = (type ?? "headshot").Trim().ToLowerInvariant();
        switch (renderType)
        {
            case "headshot":
            case "avatar":
            case "full":
                break;
            default:
                return BadRequest(new { error = "Invalid type. Allowed: headshot, avatar, full" });
        }

        // Resolve user from authentication only
        var idStr = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(idStr) || !long.TryParse(idStr, out var targetUserId) || targetUserId <= 0)
            return Unauthorized(new { error = "Authentication required" });

        try
        {
            var hash = await _thumbnailService.RenderAvatarAsync(renderType, targetUserId, cancellationToken: cancellationToken);
            // Compose full URL: base (config) + hash + ".png"
            var baseUrl = _configuration["Thumbnails:ThumbnailUrl"];
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                var scheme = string.IsNullOrWhiteSpace(Request.Scheme) ? "http" : Request.Scheme;
                var host = Request.Host.HasValue ? Request.Host.Value : "localhost";
                baseUrl = $"{scheme}://{host}/";
            }
            var fullUrl = CombineUrl(baseUrl!, hash + ".png");

            // Save to DB: users.thumbnail_url
            var connStr = _configuration.GetConnectionString("Default");
            if (!string.IsNullOrWhiteSpace(connStr))
            {
                await using var conn = new NpgsqlConnection(connStr);
                await conn.OpenAsync(cancellationToken);
                await using var cmd = new NpgsqlCommand("update users set thumbnail_url = @u where user_id = @id", conn);
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

    [Authorize]
    [HttpPost("set-body-colors")]
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
