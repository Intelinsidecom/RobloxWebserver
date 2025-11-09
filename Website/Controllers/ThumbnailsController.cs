using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Thumbnails;

namespace Website.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ThumbnailsController : ControllerBase
{
    private readonly IThumbnailService _thumbnailService;
    private readonly IConfiguration _configuration;

    public ThumbnailsController(IThumbnailService thumbnailService, IConfiguration configuration)
    {
        _thumbnailService = thumbnailService;
        _configuration = configuration;
    }
}
