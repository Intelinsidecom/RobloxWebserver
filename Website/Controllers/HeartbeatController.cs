using Microsoft.AspNetCore.Mvc;

namespace RobloxWebserver.Controllers
{
    [ApiController]
    [Route("Roblox/heartbeat")]
    public class HeartbeatController : ControllerBase
    {
        [HttpPost]
        public IActionResult Post()
        {
            return NoContent();
        }
    }
}
