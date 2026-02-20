namespace Gpusoft.Apps.VibeGallery.Data;

public class Media
{
    public long Id { get; private set; }
    public string Path { get; private set; } = "";
    public string ContentType { get; private set; } = "";
    public long FileSize { get; private set; }
    public MediaType Type { get; private set; }
    public string? Tags { get; private set; }
    public long? GalleryId { get; private set; }
    public Gallery? Gallery { get; private set; }

    private Media() { } // For EF Core

    public Media(string path, string contentType, long fileSize, MediaType type, string? tags = null, long? galleryId = null)
    {
        Path = path;
        ContentType = contentType;
        FileSize = fileSize;
        Type = type;
        Tags = tags;
        GalleryId = galleryId;
    }
}
