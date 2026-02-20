using Gpusoft.Apps.VibeGallery.Data;
using Microsoft.EntityFrameworkCore;

namespace Gpusoft.Apps.VibeGallery.Services;

/// <summary>
/// Background service that periodically generates missing thumbnails for media files.
/// Processes images via SkiaSharp and videos via FFMpegCore, saving to appData/thumbnails/.
/// </summary>
public sealed class ThumbnailBackgroundService(
    IServiceScopeFactory scopeFactory,
    ThumbnailService thumbnailService,
    ILogger<ThumbnailBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan BatchDelay = TimeSpan.FromMilliseconds(100);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait briefly to let the initial media scan finish first
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);

        logger.LogInformation("Thumbnail background service started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processed = await ProcessMissingThumbnailsAsync(stoppingToken).ConfigureAwait(false);
                if (processed > 0)
                {
                    logger.LogInformation("Generated {Count} thumbnails.", processed);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error during thumbnail generation cycle.");
            }

            await Task.Delay(PollingInterval, stoppingToken).ConfigureAwait(false);
        }

        logger.LogInformation("Thumbnail background service stopped.");
    }

    private async Task<int> ProcessMissingThumbnailsAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var allMedia = await db.Media
            .AsNoTracking()
            .Include(m => m.Gallery)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var processed = 0;

        foreach (var media in allMedia)
        {
            ct.ThrowIfCancellationRequested();

            if (thumbnailService.ThumbnailExists(media.Id, media.Gallery?.Path))
            {
                continue;
            }

            var success = media.Type switch
            {
                MediaType.Image => thumbnailService.GenerateImageThumbnail(media),
                MediaType.Video => await thumbnailService.GenerateVideoThumbnailAsync(media, ct).ConfigureAwait(false),
                _ => false
            };

            if (success)
            {
                processed++;
            }

            // Yield between items to avoid CPU spikes
            await Task.Delay(BatchDelay, ct).ConfigureAwait(false);
        }

        return processed;
    }
}
