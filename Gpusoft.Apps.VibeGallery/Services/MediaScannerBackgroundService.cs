namespace Gpusoft.Apps.VibeGallery.Services;

/// <summary>
/// Runs an initial media scan in the background after the application starts.
/// The app serves requests immediately; the scan runs asynchronously.
/// </summary>
public sealed class MediaScannerBackgroundService(
    IMediaScanner mediaScanner,
    ILogger<MediaScannerBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Startup media scan starting in background.");

        try
        {
            var result = await mediaScanner.ScanAsync(stoppingToken).ConfigureAwait(false);

            if (result is not null)
            {
                logger.LogInformation(
                    "Startup scan completed in {Duration}. Galleries: +{GA}/-{GR}, Media: +{MA}/-{MR}",
                    result.Duration, result.GalleriesAdded, result.GalleriesRemoved,
                    result.MediaAdded, result.MediaRemoved);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("Startup media scan was cancelled.");
        }
    }
}
