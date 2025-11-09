using System.Threading;
using System.Threading.Tasks;

namespace Thumbnails;

public interface IThumbnailService
{
    Task<ThumbnailSaveResult> SaveBase64PngAsync(string base64, string? overrideOutputDirectory = null, CancellationToken cancellationToken = default);
    Task<ThumbnailSaveResult> RenderAvatarAsync(string type, long userId, int? x = null, int? y = null, CancellationToken cancellationToken = default);
}
