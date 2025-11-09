using System;
using System.IO;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading;
using System.Threading.Tasks;

namespace Thumbnails
{
    public static class ThumbnailDerivatives
    {
        public static string ComposeDerivedName(string baseHash, string variant, int width, int height, string format)
        {
            var ext = NormalizeFormat(format);
            return string.Concat(baseHash, "_", variant, "_", width, "x", height, ".", ext);
        }

        public static async Task<string> EnsureDerivedAsync(string outputDirectory, string baseHash, string variant, int width, int height, string format, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(outputDirectory)) throw new ArgumentException("outputDirectory required", nameof(outputDirectory));
            if (string.IsNullOrWhiteSpace(baseHash)) throw new ArgumentException("baseHash required", nameof(baseHash));
            if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException("width/height must be positive");

            Directory.CreateDirectory(outputDirectory);
            var basePath = ResolveBasePath(outputDirectory, baseHash);
            var ext = NormalizeFormat(format);
            var derivedName = ComposeDerivedName(baseHash, variant, width, height, ext);
            var derivedPath = Path.Combine(outputDirectory, derivedName);

            if (File.Exists(derivedPath)) return derivedPath;
            if (!File.Exists(basePath)) return basePath; // Let caller decide fallback behavior

            // Resize synchronously (no async IO exposed by System.Drawing)
            using var image = Image.FromFile(basePath);
            using var bmp = new Bitmap(width, height);
            using (var g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                g.DrawImage(image, 0, 0, width, height);
            }

            if (ext == "jpg")
            {
                bmp.Save(derivedPath, ImageFormat.Jpeg);
            }
            else
            {
                bmp.Save(derivedPath, ImageFormat.Png);
            }

            // Simulate async boundary for API consistency
            await Task.CompletedTask;
            return derivedPath;
        }

        public static async Task<string> EnsureConvertedAsync(string outputDirectory, string baseHash, string variant, string format, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(outputDirectory)) throw new ArgumentException("outputDirectory required", nameof(outputDirectory));
            if (string.IsNullOrWhiteSpace(baseHash)) throw new ArgumentException("baseHash required", nameof(baseHash));

            Directory.CreateDirectory(outputDirectory);
            var basePath = ResolveBasePath(outputDirectory, baseHash);
            var ext = NormalizeFormat(format);
            // If PNG requested and base is PNG, just return the base path
            if (ext == "png" && string.Equals(Path.GetExtension(basePath), ".png", StringComparison.OrdinalIgnoreCase))
                return basePath;

            var derivedName = string.Concat(baseHash, "_", variant, ".", ext);
            var derivedPath = Path.Combine(outputDirectory, derivedName);
            if (File.Exists(derivedPath)) return derivedPath;
            if (!File.Exists(basePath)) return basePath;

            using var image = Image.FromFile(basePath);
            // Save to requested format without resizing
            if (ext == "jpg")
            {
                image.Save(derivedPath, ImageFormat.Jpeg);
            }
            else
            {
                image.Save(derivedPath, ImageFormat.Png);
            }

            await Task.CompletedTask;
            return derivedPath;
        }

        private static string NormalizeFormat(string? format)
        {
            var f = (format ?? "png").Trim().ToLowerInvariant();
            return f == "jpeg" || f == "jpg" ? "jpg" : "png";
        }

        private static string ResolveBasePath(string outputDirectory, string baseHash)
        {
            var png = Path.Combine(outputDirectory, baseHash + ".png");
            if (File.Exists(png)) return png;
            var jpg = Path.Combine(outputDirectory, baseHash + ".jpg");
            if (File.Exists(jpg)) return jpg;
            // Default to PNG path if none exist
            return png;
        }
    }
}
