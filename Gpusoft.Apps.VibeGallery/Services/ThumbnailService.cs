using FFMpegCore;
using Gpusoft.Apps.VibeGallery.Data;
using SkiaSharp;

namespace Gpusoft.Apps.VibeGallery.Services;

/// <summary>
/// Generates and serves thumbnails for media files. Handles both image resizing
/// and video frame extraction. Falls back from AVIF to WebP if encoding is unavailable.
/// </summary>
public sealed class ThumbnailService
{
    private const int MaxDimension = 300;
    private const int Quality = 63;
    private const string ThumbnailDirectory = "thumbnails";
    private const string PlaceholderFileName = "placeholder.svg";

    private readonly string _thumbnailsPath;
    private readonly string _placeholderPath;
    private readonly string _mediaRoot;
    private readonly ILogger<ThumbnailService> _logger;

    private SKEncodedImageFormat _encodingFormat = SKEncodedImageFormat.Avif;
    private string _fileExtension = ".avif";
    private string _contentType = "image/avif";
    private bool _formatDetected;

    public ThumbnailService(
        IWebHostEnvironment env,
        IConfiguration configuration,
        ILogger<ThumbnailService> logger)
    {
        var appDataDir = Path.Combine(env.ContentRootPath, "appData");
        _thumbnailsPath = Path.Combine(appDataDir, ThumbnailDirectory);
        Directory.CreateDirectory(_thumbnailsPath);

        _placeholderPath = Path.Combine(env.WebRootPath, "images", PlaceholderFileName);
        _mediaRoot = configuration.GetValue<string>("MediaScanner:MediaRoot") ?? "/app/media";
        _logger = logger;
    }

    /// <summary>
    /// Gets the content type of generated thumbnails.
    /// </summary>
    public string ThumbnailContentType => _contentType;

    /// <summary>
    /// Gets the file path for a media thumbnail, or the placeholder path if it doesn't exist.
    /// </summary>
    public (string Path, string ContentType, bool IsPlaceholder) GetThumbnailPath(long mediaId, string? galleryPath = null)
    {
        var dir = BuildThumbnailDirectory(galleryPath);

        var thumbnailPath = Path.Combine(dir, $"{mediaId}{_fileExtension}");
        if (File.Exists(thumbnailPath))
        {
            return (thumbnailPath, _contentType, false);
        }

        // Try alternate format in case a fallback was used
        var webpPath = Path.Combine(dir, $"{mediaId}.webp");
        if (File.Exists(webpPath))
        {
            return (webpPath, "image/webp", false);
        }

        var avifPath = Path.Combine(dir, $"{mediaId}.avif");
        if (File.Exists(avifPath))
        {
            return (avifPath, "image/avif", false);
        }

        return (_placeholderPath, "image/svg+xml", true);
    }

    /// <summary>
    /// Deletes the thumbnail file for a single media item.
    /// </summary>
    public void DeleteThumbnail(long mediaId, string? galleryPath = null)
    {
        var dir = BuildThumbnailDirectory(galleryPath);
        foreach (var ext in (ReadOnlySpan<string>)[".avif", ".webp"])
        {
            var path = Path.Combine(dir, $"{mediaId}{ext}");
            if (File.Exists(path))
            {
                File.Delete(path);
                _logger.LogDebug("Deleted thumbnail for media {MediaId}.", mediaId);
            }
        }
    }

    /// <summary>
    /// Deletes all thumbnails for a gallery by removing its thumbnail subdirectory.
    /// </summary>
    public void DeleteGalleryThumbnails(string galleryPath)
    {
        if (string.IsNullOrWhiteSpace(galleryPath)) return;

        var dir = BuildThumbnailDirectory(galleryPath);
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
            _logger.LogInformation("Deleted thumbnail directory for gallery path '{GalleryPath}'.", galleryPath);
        }
    }

    /// <summary>
    /// Returns true if a thumbnail already exists on disk for the given media.
    /// </summary>
    public bool ThumbnailExists(long mediaId, string? galleryPath = null)
    {
        var dir = BuildThumbnailDirectory(galleryPath);
        return File.Exists(Path.Combine(dir, $"{mediaId}.avif"))
            || File.Exists(Path.Combine(dir, $"{mediaId}.webp"));
    }

    /// <summary>
    /// Generates a thumbnail for an image media file.
    /// </summary>
    public bool GenerateImageThumbnail(Media media)
    {
        var sourcePath = Path.Combine(_mediaRoot, media.Path);
        if (!File.Exists(sourcePath))
        {
            _logger.LogWarning("Source image not found for media {MediaId}: {Path}", media.Id, sourcePath);
            return false;
        }

        try
        {
            using var original = SKBitmap.Decode(sourcePath);
            if (original is null)
            {
                _logger.LogWarning("Failed to decode image for media {MediaId}: {Path}", media.Id, sourcePath);
                return false;
            }

            return ResizeAndSave(original, media.Id, media.Gallery?.Path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating image thumbnail for media {MediaId}", media.Id);
            return false;
        }
    }

    /// <summary>
    /// Generates a thumbnail for a video media file by extracting a frame at 10% of its duration.
    /// </summary>
    public async Task<bool> GenerateVideoThumbnailAsync(Media media, CancellationToken cancellationToken = default)
    {
        var sourcePath = Path.Combine(_mediaRoot, media.Path);
        if (!File.Exists(sourcePath))
        {
            _logger.LogWarning("Source video not found for media {MediaId}: {Path}", media.Id, sourcePath);
            return false;
        }

        try
        {
            var mediaInfo = await FFProbe.AnalyseAsync(sourcePath, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            var captureTime = mediaInfo.Duration * 0.1;

            using var memoryStream = new MemoryStream();
            await FFMpegArguments
                .FromFileInput(sourcePath, verifyExists: false, options => options
                    .Seek(captureTime))
                .OutputToPipe(new FFMpegCore.Pipes.StreamPipeSink(memoryStream), options => options
                    .WithFrameOutputCount(1)
                    .ForceFormat("image2pipe")
                    .WithVideoCodec("png"))
                .CancellableThrough(cancellationToken)
                .ProcessAsynchronously()
                .ConfigureAwait(false);

            memoryStream.Position = 0;

            using var bitmap = SKBitmap.Decode(memoryStream);
            if (bitmap is null)
            {
                _logger.LogWarning("Failed to decode extracted video frame for media {MediaId}", media.Id);
                return false;
            }

            return ResizeAndSave(bitmap, media.Id, media.Gallery?.Path);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error generating video thumbnail for media {MediaId}", media.Id);
            return false;
        }
    }

    private bool ResizeAndSave(SKBitmap original, long mediaId, string? galleryPath = null)
    {
        var (targetWidth, targetHeight) = CalculateTargetSize(original.Width, original.Height);

        using var resized = original.Resize(new SKImageInfo(targetWidth, targetHeight), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        if (resized is null)
        {
            _logger.LogWarning("Failed to resize image for media {MediaId}", mediaId);
            return false;
        }

        using var image = SKImage.FromBitmap(resized);

        DetectEncodingFormat(image);

        var data = image.Encode(_encodingFormat, Quality);
        if (data is null)
        {
            _logger.LogWarning("Failed to encode thumbnail for media {MediaId} with format {Format}", mediaId, _encodingFormat);
            return false;
        }

        var dir = BuildThumbnailDirectory(galleryPath);
        Directory.CreateDirectory(dir);
        var outputPath = Path.Combine(dir, $"{mediaId}{_fileExtension}");
        using var fileStream = File.OpenWrite(outputPath);
        data.SaveTo(fileStream);

        return true;
    }

    private string BuildThumbnailDirectory(string? galleryPath) =>
        string.IsNullOrWhiteSpace(galleryPath)
            ? _thumbnailsPath
            : Path.Combine(_thumbnailsPath, galleryPath);

    private void DetectEncodingFormat(SKImage testImage)
    {
        if (_formatDetected) return;

        var testData = testImage.Encode(SKEncodedImageFormat.Avif, Quality);
        if (testData is not null && testData.Size > 0)
        {
            _encodingFormat = SKEncodedImageFormat.Avif;
            _fileExtension = ".avif";
            _contentType = "image/avif";
            _logger.LogInformation("Thumbnail encoding: using AVIF format");
        }
        else
        {
            _encodingFormat = SKEncodedImageFormat.Webp;
            _fileExtension = ".webp";
            _contentType = "image/webp";
            _logger.LogWarning("AVIF encoding not available, falling back to WebP");
        }

        _formatDetected = true;
    }

    private static (int width, int height) CalculateTargetSize(int sourceWidth, int sourceHeight)
    {
        if (sourceWidth <= MaxDimension && sourceHeight <= MaxDimension)
        {
            return (sourceWidth, sourceHeight);
        }

        var ratio = (double)MaxDimension / Math.Max(sourceWidth, sourceHeight);
        return ((int)(sourceWidth * ratio), (int)(sourceHeight * ratio));
    }
}
