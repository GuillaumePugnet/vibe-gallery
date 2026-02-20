namespace Gpusoft.Apps.VibeGallery.Data;

public class Gallery
{
    public long Id { get; private set; }
    public string Name { get; private set; } = "";
    public string Description { get; private set; } = "";
    public string Path { get; private set; } = "";
    public DateTime CreatedAt { get; private set; }
    public List<Media> Media { get; private set; } = new();

    private Gallery() { } // For EF Core

    public Gallery(string name, string description, string path, DateTime createdAt)
    {
        Name = name;
        Description = description;
        Path = path;
        CreatedAt = createdAt;
    }

    public void UpdateName(string name)
    {
        Name = name;
    }

    public void UpdateDescription(string description)
    {
        Description = description;
    }
}