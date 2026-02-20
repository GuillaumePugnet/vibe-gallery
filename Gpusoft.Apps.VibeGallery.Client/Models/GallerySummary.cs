namespace Gpusoft.Apps.VibeGallery.Client.Models;

/// <summary>
/// Summary DTO for a gallery, returned by the galleries API.
/// </summary>
public record GallerySummary(
    long Id,
    string Name,
    string Description,
    int MediaCount,
    long? ThumbnailMediaId,
    DateTime CreatedAt);
