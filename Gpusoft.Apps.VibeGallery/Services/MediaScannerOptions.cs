namespace Gpusoft.Apps.VibeGallery.Services;

/// <summary>
/// Configuration options for the media scanner service, bound from the "MediaScanner" config section.
/// </summary>
public class MediaScannerOptions
{
    public const string SectionName = "MediaScanner";

    public string MediaRoot { get; set; } = "/app/media";

    public string[] SupportedImageExtensions { get; set; } =
    [
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".svg", ".bmp",
        ".avif", ".jxl", ".tiff", ".tif", ".ico", ".heic", ".heif"
    ];

    public string[] SupportedVideoExtensions { get; set; } =
    [
        ".mp4", ".webm", ".mov", ".avi", ".mkv", ".wmv", ".m4v"
    ];

    private HashSet<string>? _allExtensions;

    /// <summary>
    /// Returns all supported extensions (image + video) as a case-insensitive set.
    /// </summary>
    public IReadOnlySet<string> AllSupportedExtensions =>
        _allExtensions ??= new HashSet<string>(
            SupportedImageExtensions.Concat(SupportedVideoExtensions),
            StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Determines the <see cref="Data.MediaType"/> for a given file extension.
    /// </summary>
    public Data.MediaType GetMediaType(string extension) =>
        SupportedVideoExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase)
            ? Data.MediaType.Video
            : Data.MediaType.Image;
}
