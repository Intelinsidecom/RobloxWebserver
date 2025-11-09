using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace RCCArbiter.Endpoints
{
    public class RenderAvatarEndpoint : ICompiledEndpoint
    {
        private IConfiguration? _configuration;
        public string Route => "/renderavatar";
        public string ScriptName => "RenderAvatar";

        public void SetConfiguration(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public IDictionary<string, string> MapParameters(HttpRequest req)
        {
            var p = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // Map required/optional inputs
            var type = req.Query.TryGetValue("type", out var t) ? t.ToString() : "headshot";
            var userId = req.Query.TryGetValue("userId", out var uid) ? uid.ToString() : 
                        (req.Query.TryGetValue("playerId", out var pid) ? pid.ToString() : "1");
            // Defaults for x/y depend on the type, but explicit query overrides
            string defaultX, defaultY;
            switch ((type ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "headshot":
                    defaultX = "1024"; defaultY = "1024"; break;
                case "avatar":
                    defaultX = "420"; defaultY = "800"; break;
                case "full":
                case "fullbody":
                    defaultX = "1024"; defaultY = "1024"; break;
                case "thumbnail":
                case "thumb":
                    defaultX = "150"; defaultY = "150"; break;
                default:
                    defaultX = "420"; defaultY = "420"; break;
            }
            var x = req.Query.TryGetValue("x", out var xv) ? xv.ToString() : defaultX;
            var y = req.Query.TryGetValue("y", out var yv) ? yv.ToString() : defaultY;

            // baseUrl preference: appsettings -> query -> inferred from request
            string? configuredBase = _configuration?["Arbiter:BaseUrl"];
            var host = req.Host.HasValue ? req.Host.Value : "localhost";
            var scheme = string.IsNullOrEmpty(req.Scheme) ? "http" : req.Scheme;
            string inferred = $"{scheme}://{host}";
            var baseUrl = !string.IsNullOrWhiteSpace(configuredBase)
                ? configuredBase!
                : (req.Query.TryGetValue("baseUrl", out var bu) && !string.IsNullOrWhiteSpace(bu)
                    ? bu.ToString()
                    : inferred);

            // Upload target preference: query -> appsettings
            var uploadUrl = req.Query.TryGetValue("uploadUrl", out var uu) && !string.IsNullOrWhiteSpace(uu)
                ? uu.ToString()
                : _configuration?["Arbiter:UploadUrl"] ?? string.Empty;

            var accessKey = req.Query.TryGetValue("accessKey", out var ak) && !string.IsNullOrWhiteSpace(ak)
                ? ak.ToString()
                : _configuration?["Arbiter:AccessKey"] ?? string.Empty;

            // Parameter names correspond to tokens used in the Lua template (%token%)
            p["type"] = type;
            p["userId"] = userId;
            p["x"] = x;
            p["y"] = y;
            p["baseUrl"] = baseUrl;
            p["uploadUrl"] = uploadUrl;
            p["accessKey"] = accessKey;
            
            // Debug logging
            Console.WriteLine($"[RenderAvatar] Parameters: type={type}, userId={userId}, x={x}, y={y}, uploadUrl={(string.IsNullOrEmpty(uploadUrl) ? "<empty>" : "set")}");

            return p;
        }
    }
}
