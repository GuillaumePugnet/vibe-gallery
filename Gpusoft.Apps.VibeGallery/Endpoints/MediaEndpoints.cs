using Gpusoft.Apps.VibeGallery.Data;
using Gpusoft.Apps.VibeGallery.Services;
using Microsoft.EntityFrameworkCore;

namespace Gpusoft.Apps.VibeGallery.Endpoints;

/// <summary>
/// Minimal API endpoints for serving media files and thumbnails.
/// </summary>
public static class MediaEndpoints
{
    /// <summary>
    /// Maps media-related API endpoints.
    /// </summary>
    public static void MapMediaEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/media");

        group.MapGet("/{id:long}/file", ServeFileAsync);
        group.MapGet("/{id:long}/thumbnail", ServeThumbnailAsync);
        group.MapDelete("/{id:long}", DeleteMediaAsync);
    }

    private static async Task<IResult> ServeFileAsync(
        long id,
        AppDbContext db,
        IConfiguration configuration,
        CancellationToken ct)
    {
        var media = await db.Media
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == id, ct)
            .ConfigureAwait(false);

        if (media is null)
        {
            return Results.NotFound();
        }

        var mediaRoot = configuration.GetValue<string>("MediaScanner:MediaRoot") ?? "/app/media";
        var filePath = Path.Combine(mediaRoot, media.Path);

        if (!File.Exists(filePath))
        {
            return Results.NotFound();
        }

        return Results.File(filePath, media.ContentType, enableRangeProcessing: true);
    }

    private static async Task<IResult> ServeThumbnailAsync(
        long id,
        AppDbContext db,
        ThumbnailService thumbnailService,
        CancellationToken ct)
    {
        var media = await db.Media
            .AsNoTracking()
            .Include(m => m.Gallery)
            .FirstOrDefaultAsync(m => m.Id == id, ct)
            .ConfigureAwait(false);

        if (media is null)
        {
            return Results.NotFound();
        }

        var (path, contentType, _) = thumbnailService.GetThumbnailPath(media.Id, media.Path);

        if (!File.Exists(path))
        {
            return Results.NotFound();
        }

        return Results.File(path, contentType);
    }

    private static async Task<IResult> DeleteMediaAsync(
        long id,
        AppDbContext db,
        ThumbnailService thumbnailService,
        CancellationToken ct)
    {
        var media = await db.Media
            .Include(m => m.Gallery)
            .FirstOrDefaultAsync(m => m.Id == id, ct)
            .ConfigureAwait(false);

        if (media is null)
        {
            return Results.NotFound();
        }

        thumbnailService.DeleteThumbnail(media.Id, media.Path);

        db.Media.Remove(media);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return Results.NoContent();
    }
}
