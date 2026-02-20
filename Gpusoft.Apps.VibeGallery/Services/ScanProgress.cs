namespace Gpusoft.Apps.VibeGallery.Services;

/// <summary>
/// Real-time progress update pushed via SignalR after each gallery is processed.
/// </summary>
public record ScanProgress(
    string Phase,
    string? GalleryName,
    int ProcessedGalleries,
    int TotalGalleries,
    int MediaAdded,
    int MediaRemoved,
    string Message);
