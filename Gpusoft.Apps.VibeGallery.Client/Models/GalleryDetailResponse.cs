namespace Gpusoft.Apps.VibeGallery.Client.Models;

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
    string ContentType);
