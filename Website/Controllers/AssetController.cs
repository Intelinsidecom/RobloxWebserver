using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Npgsql;
using System.Text;
using System.Threading.Tasks;
using Users;

namespace Website.Controllers
{
    [ApiController]
    [Route("Asset")]
    public class AssetController : ControllerBase
    {
        private readonly IConfiguration _configuration;

        public AssetController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        // GET /Asset/characterfetch.ashx?player={id}
        [HttpGet("characterfetch.ashx")]
        public IActionResult CharacterFetchAshx([FromQuery] string? userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
                return BadRequest(new { error = "userId is required" });
            return CharacterFetchInternal(userId);
        }

        private IActionResult CharacterFetchInternal(string? userId)
        {
            var pid = string.IsNullOrWhiteSpace(userId) ? "0" : userId;
            var scheme = string.IsNullOrEmpty(Request.Scheme) ? "http" : Request.Scheme;
            var host = Request.Host.HasValue ? Request.Host.Value : "localhost";
            var baseUrl = $"{scheme}://{host}";

            // Response format (semicolon-separated URLs), per request:
            // http://your.url.here/Asset/bodycolors.ashx;http://your.url.here/Asset/id?=PLAYER;http://your.url.here/Asset/id?=PLAYER
            var body = string.Join(';', new[]
            {
                $"{baseUrl}/asset/bodycolors.ashx?userId={pid}",
            });

            return Content(body, "text/plain");
        }

        // Minimal stubs to avoid 404 if the engine requests these URLs
        [HttpGet("BodyColors.ashx")]
        public async Task<IActionResult> BodyColors([FromQuery] long? userId)
        {
            if (!userId.HasValue || userId.Value <= 0)
                return BadRequest(new { error = "userId is required" });

            int head = 1, leftArm = 1, leftLeg = 1, rightArm = 1, rightLeg = 1, torso = 1;
            var uid = userId.Value;
            var connStr = _configuration.GetConnectionString("Default");

            if (!string.IsNullOrWhiteSpace(connStr))
            {
                try
                {
                    var exists = await UserQueries.UserExistsAsync(connStr, uid);
                    if (!exists)
                        return NotFound(new { error = "User not found" });

                    await using var conn = new NpgsqlConnection(connStr);
                    await conn.OpenAsync();
                    const string sql = @"select head_color, left_arm_color, left_leg_color, right_arm_color, right_leg_color, torso_color
                                          from bodycolors where user_id = @uid";
                    await using var cmd = new NpgsqlCommand(sql, conn);
                    cmd.Parameters.AddWithValue("uid", uid);
                    await using var reader = await cmd.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        head = reader.IsDBNull(0) ? 1 : reader.GetInt32(0);
                        leftArm = reader.IsDBNull(1) ? 1 : reader.GetInt32(1);
                        leftLeg = reader.IsDBNull(2) ? 1 : reader.GetInt32(2);
                        rightArm = reader.IsDBNull(3) ? 1 : reader.GetInt32(3);
                        rightLeg = reader.IsDBNull(4) ? 1 : reader.GetInt32(4);
                        torso = reader.IsDBNull(5) ? 1 : reader.GetInt32(5);
                    }
                }
                catch
                {
                    // Ignore DB errors and fall back to defaults
                }
            }

            var xml = $"""
<roblox xmlns:xmime="http://www.w3.org/2005/05/xmlmime" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xsi:noNamespaceSchemaLocation="http://www.roblox.com/roblox.xsd" version="4">
    <External>null</External>
    <External>nil</External>
    <Item class="BodyColors"> 
        <Properties>
            <int name="HeadColor">{head}</int>
            <int name="LeftArmColor">{leftArm}</int>
            <int name="LeftLegColor">{leftLeg}</int>
            <string name="Name">Body Colors</string>
            <int name="RightArmColor">{rightArm}</int>
            <int name="RightLegColor">{rightLeg}</int>
            <int name="TorsoColor">{torso}</int>
            <bool name="archivable">true</bool>
        </Properties>
    </Item>
</roblox>
""";

            return Content(xml, "application/xml", Encoding.UTF8);
        }

        // Accepts /Asset/id?=123 or /Asset/id?id=123 (we don't use the value yet)  <int name="HeadColor">{head}</int>
        [HttpGet("id")]
        public IActionResult AssetById()
        {
            // Return empty 200; extend later to serve actual asset data
            return Content(string.Empty, "text/plain");
        }
    }
}

