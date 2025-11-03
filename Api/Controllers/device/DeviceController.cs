using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Route("device")]
public class DeviceController : ControllerBase
{
    [HttpPost("initialize")]
    public IActionResult Initialize()
    {
        return Ok(new { success = true, message = string.Empty });
    }
}
