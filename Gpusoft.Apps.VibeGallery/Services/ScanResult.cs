namespace Gpusoft.Apps.VibeGallery.Services;

/// <summary>
/// Final summary returned after a scan completes.
/// </summary>
public record ScanResult(
    int GalleriesAdded,
    int GalleriesRemoved,
    int MediaAdded,
    int MediaRemoved,
    TimeSpan Duration);
