using Gpusoft.Apps.VibeGallery.Data;
using Gpusoft.Apps.VibeGallery.Services;
using Microsoft.EntityFrameworkCore;

namespace Gpusoft.Apps.VibeGallery.Endpoints;

/// <summary>
/// Minimal API endpoints for gallery operations.
/// </summary>
public static class GalleryEndpoints
{
    /// <summary>
    /// Maps gallery-related API endpoints.
    /// </summary>
    public static void MapGalleryEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/galleries");

        group.MapGet("/", ListGalleriesAsync);
        group.MapGet("/{id:long}", GetGalleryDetailAsync);
        group.MapDelete("/{id:long}", DeleteGalleryAsync);
    }

    private static async Task<IResult> ListGalleriesAsync(AppDbContext db, CancellationToken ct)
    {
        var galleries = await db.Galleries
            .AsNoTracking()
            .Include(g => g.Media)
            .OrderByDescending(g => g.CreatedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var result = galleries.Select(g =>
        {
            var imageMedia = g.Media.Where(m => m.Type == MediaType.Image).ToList();
            long? thumbnailMediaId = imageMedia.Count > 0
                ? imageMedia[Random.Shared.Next(imageMedia.Count)].Id
                : null;

            return new GallerySummary(
                g.Id,
                g.Name,
                g.Description,
                g.Media.Count,
                thumbnailMediaId,
                g.CreatedAt);
        }).ToList();

        return Results.Ok(result);
    }

    private static async Task<IResult> GetGalleryDetailAsync(
        long id,
        int? page,
        int? pageSize,
        AppDbContext db,
        CancellationToken ct)
    {
        var gallery = await db.Galleries
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == id, ct)
            .ConfigureAwait(false);

        if (gallery is null)
        {
            return Results.NotFound();
        }

        var effectivePage = Math.Max(1, page ?? 1);
        var effectivePageSize = Math.Clamp(pageSize ?? 24, 1, 100);

        var totalCount = await db.Media
            .CountAsync(m => m.GalleryId == id, ct)
            .ConfigureAwait(false);

        var items = await db.Media
            .AsNoTracking()
            .Where(m => m.GalleryId == id)
            .OrderByDescending(m => m.CreatedAt)
            .Skip((effectivePage - 1) * effectivePageSize)
            .Take(effectivePageSize)
            .Select(m => new GalleryMediaItem(m.Id, m.Type.ToString(), m.ContentType, m.CreatedAt))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var result = new GalleryDetailResponse(
            gallery.Id,
            gallery.Name,
            gallery.Description,
            totalCount,
            effectivePage,
            effectivePageSize,
            items);

        return Results.Ok(result);
    }

    private static async Task<IResult> DeleteGalleryAsync(
        long id,
        AppDbContext db,
        ThumbnailService thumbnailService,
        CancellationToken ct)
    {
        var gallery = await db.Galleries
            .FirstOrDefaultAsync(g => g.Id == id, ct)
            .ConfigureAwait(false);

        if (gallery is null)
        {
            return Results.NotFound();
        }

        // Delete thumbnail directory before DB removal; EF cascade handles the media rows.
        thumbnailService.DeleteGalleryThumbnails(gallery.Path);

        db.Galleries.Remove(gallery);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return Results.NoContent();
    }
}

/// <summary>
/// DTO for gallery list API response.
/// </summary>
public record GallerySummary(
    long Id,
    string Name,
    string Description,
    int MediaCount,
    long? ThumbnailMediaId,
    DateTime CreatedAt);

/// <summary>
/// DTO for the gallery detail API response with paginated media.
/// </summary>
public record GalleryDetailResponse(
    long Id,
    string Name,
    string Description,
    int TotalCount,
    int Page,
    int PageSize,
    List<GalleryMediaItem> Items);

/// <summary>
/// DTO for a single media item within a gallery.
/// </summary>
public record GalleryMediaItem(
    long Id,
    string Type,
    string ContentType,
    DateTime CreatedAt);
