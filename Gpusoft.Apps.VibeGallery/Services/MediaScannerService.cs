using System.Diagnostics;
using Gpusoft.Apps.VibeGallery.Data;
using Gpusoft.Apps.VibeGallery.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Gpusoft.Apps.VibeGallery.Services;

/// <summary>
/// Scans the configured media root directory, discovers gallery folders and media files,
/// and synchronises state with the database. Pushes per-gallery progress via SignalR.
/// </summary>
public sealed class MediaScannerService : IMediaScanner
{
    private static readonly SemaphoreSlim _scanLock = new(1, 1);

    private static readonly Dictionary<string, string> MimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        // Images
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
        [".svg"] = "image/svg+xml",
        [".bmp"] = "image/bmp",
        [".avif"] = "image/avif",
        [".jxl"] = "image/jxl",
        [".tiff"] = "image/tiff",
        [".tif"] = "image/tiff",
        [".ico"] = "image/x-icon",
        [".heic"] = "image/heic",
        [".heif"] = "image/heif",
        // Videos
        [".mp4"] = "video/mp4",
        [".webm"] = "video/webm",
        [".mov"] = "video/quicktime",
        [".avi"] = "video/x-msvideo",
        [".mkv"] = "video/x-matroska",
        [".wmv"] = "video/x-ms-wmv",
        [".m4v"] = "video/x-m4v",
    };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHubContext<ScanHub> _hubContext;
    private readonly MediaScannerOptions _options;
    private readonly ILogger<MediaScannerService> _logger;

    public MediaScannerService(
        IServiceScopeFactory scopeFactory,
        IHubContext<ScanHub> hubContext,
        IOptions<MediaScannerOptions> options,
        ILogger<MediaScannerService> logger)
    {
        _scopeFactory = scopeFactory;
        _hubContext = hubContext;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ScanResult?> ScanAsync(CancellationToken cancellationToken = default)
    {
        if (!_scanLock.Wait(0))
        {
            _logger.LogWarning("Scan already in progress, skipping.");
            return null;
        }

        try
        {
            return await ExecuteScanAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _scanLock.Release();
        }
    }

    private async Task<ScanResult> ExecuteScanAsync(CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation("Media scan started. Root: {MediaRoot}", _options.MediaRoot);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // --- Gallery sync ---

        var fsGalleryDirs = Directory.Exists(_options.MediaRoot)
            ? Directory.GetDirectories(_options.MediaRoot)
            : [];

        var fsGalleryPaths = fsGalleryDirs
            .Select(d => System.IO.Path.GetFileName(d))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var dbGalleries = await db.Galleries
            .Include(g => g.Media)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var dbGalleryLookup = dbGalleries.ToDictionary(g => g.Path, StringComparer.OrdinalIgnoreCase);

        var addedGalleryPaths = fsGalleryPaths.Except(dbGalleryLookup.Keys, StringComparer.OrdinalIgnoreCase).ToList();
        var removedGalleries = dbGalleries.Where(g => !fsGalleryPaths.Contains(g.Path)).ToList();

        foreach (var path in addedGalleryPaths)
        {
            var galleryDir = System.IO.Path.Combine(_options.MediaRoot, path);
            var createdAt = Directory.GetCreationTimeUtc(galleryDir);
            var gallery = new Gallery(name: path, description: "", path: path, createdAt: createdAt);
            db.Galleries.Add(gallery);
            dbGalleryLookup[path] = gallery;
        }

        db.Galleries.RemoveRange(removedGalleries);

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        _logger.LogInformation("Gallery sync: +{Added} / -{Removed}", addedGalleryPaths.Count, removedGalleries.Count);

        await _hubContext.Clients.All.SendAsync(
            "ReceiveScanProgress",
            new ScanProgress(
                Phase: "Galleries",
                GalleryName: null,
                ProcessedGalleries: 0,
                TotalGalleries: dbGalleryLookup.Count,
                MediaAdded: 0,
                MediaRemoved: 0,
                Message: $"Gallery sync complete: +{addedGalleryPaths.Count} added, -{removedGalleries.Count} removed"),
            ct).ConfigureAwait(false);

        // --- Media sync per gallery ---

        var allGalleries = dbGalleryLookup.Values.ToList();
        var totalMediaAdded = 0;
        var totalMediaRemoved = 0;

        for (var i = 0; i < allGalleries.Count; i++)
        {
            var gallery = allGalleries[i];
            var galleryDir = System.IO.Path.Combine(_options.MediaRoot, gallery.Path);

            var (mediaAdded, mediaRemoved) = SyncGalleryMedia(db, gallery, galleryDir);
            totalMediaAdded += mediaAdded;
            totalMediaRemoved += mediaRemoved;

            _logger.LogInformation(
                "Scanned gallery '{Name}' ({Index}/{Total}): +{Added} / -{Removed}",
                gallery.Name, i + 1, allGalleries.Count, mediaAdded, mediaRemoved);

            await _hubContext.Clients.All.SendAsync(
                "ReceiveScanProgress",
                new ScanProgress(
                    Phase: "Media",
                    GalleryName: gallery.Name,
                    ProcessedGalleries: i + 1,
                    TotalGalleries: allGalleries.Count,
                    MediaAdded: mediaAdded,
                    MediaRemoved: mediaRemoved,
                    Message: $"Gallery '{gallery.Name}' ({i + 1}/{allGalleries.Count}): +{mediaAdded} added, -{mediaRemoved} removed"),
                ct).ConfigureAwait(false);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        stopwatch.Stop();

        var result = new ScanResult(
            GalleriesAdded: addedGalleryPaths.Count,
            GalleriesRemoved: removedGalleries.Count,
            MediaAdded: totalMediaAdded,
            MediaRemoved: totalMediaRemoved,
            Duration: stopwatch.Elapsed);

        _logger.LogInformation(
            "Media scan completed in {Duration}. Galleries: +{GA}/-{GR}, Media: +{MA}/-{MR}",
            result.Duration, result.GalleriesAdded, result.GalleriesRemoved, result.MediaAdded, result.MediaRemoved);

        return result;
    }

    private (int added, int removed) SyncGalleryMedia(AppDbContext db, Gallery gallery, string galleryDir)
    {
        var fsFiles = Directory.Exists(galleryDir)
            ? Directory.GetFiles(galleryDir)
                .Where(f => _options.AllSupportedExtensions.Contains(System.IO.Path.GetExtension(f)))
                .ToList()
            : [];

        var fsRelativePaths = fsFiles
            .Select(f => System.IO.Path.Combine(gallery.Path, System.IO.Path.GetFileName(f)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var dbMedia = gallery.Media;
        var dbMediaPaths = dbMedia.Select(m => m.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // New media
        var newPaths = fsRelativePaths.Except(dbMediaPaths, StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var relativePath in newPaths)
        {
            var fullPath = System.IO.Path.Combine(_options.MediaRoot, relativePath);
            var fileInfo = new FileInfo(fullPath);
            var extension = System.IO.Path.GetExtension(relativePath);
            var contentType = MimeTypes.GetValueOrDefault(extension, "application/octet-stream");
            var mediaType = _options.GetMediaType(extension);

            var media = new Data.Media(
                path: relativePath,
                contentType: contentType,
                fileSize: fileInfo.Length,
                type: mediaType,
                galleryId: gallery.Id);

            db.Media.Add(media);
        }

        // Removed media
        var removedMedia = dbMedia.Where(m => !fsRelativePaths.Contains(m.Path)).ToList();
        db.Media.RemoveRange(removedMedia);

        return (newPaths.Count, removedMedia.Count);
    }
}
