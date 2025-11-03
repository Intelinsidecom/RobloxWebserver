using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace RobloxWebserver.Controllers
{
    [ApiController]
    [Route("device")]
    public class DeviceController : ControllerBase
    {
        private readonly ILogger<DeviceController> _logger;
        private readonly Microsoft.Extensions.Configuration.IConfiguration _config;

        public DeviceController(ILogger<DeviceController> logger, Microsoft.Extensions.Configuration.IConfiguration config)
        {
            _logger = logger;
            _config = config;
        }

        [HttpPost("initialize")]
        public IActionResult Initialize([FromBody] JsonElement body)
        {
            if (_config.GetValue<bool>("Features:EnableRequestLogging"))
            {
                _logger.LogInformation("/device/initialize body: {Body}", body.ToString());
            }

            var expectedRequestExample = new
            {
                deviceId = "abc-123",
                apiKey = "your-api-key",
                version = "1.0.0"
            };

            var expectedResponseExample = new
            {
                status = "ok",
                message = "initialized"
            };

            return Ok(new
            {
                received = body,
                expectedRequestExample,
                expectedResponseExample
            });
        }

        [HttpGet("initialize")]
        public IActionResult InitializeSchema()
        {
            var expectedRequestExample = new
            {
                deviceId = "abc-123",
                apiKey = "your-api-key",
                version = "1.0.0"
            };

            var expectedResponseExample = new
            {
                status = "ok",
                message = "initialized"
            };

            return Ok(new
            {
                expectedRequestExample,
                expectedResponseExample
            });
        }
    }
}
