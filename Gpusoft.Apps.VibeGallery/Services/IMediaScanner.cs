namespace Gpusoft.Apps.VibeGallery.Services;

/// <summary>
/// Scans the filesystem for galleries and media, synchronising state with the database.
/// </summary>
public interface IMediaScanner
{
    /// <summary>
    /// Runs a full scan of the media root directory. Only one scan can run at a time;
    /// returns <c>null</c> if a scan is already in progress.
    /// </summary>
    Task<ScanResult?> ScanAsync(CancellationToken cancellationToken = default);
}
