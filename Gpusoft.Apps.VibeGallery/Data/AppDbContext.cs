using Microsoft.EntityFrameworkCore;

namespace Gpusoft.Apps.VibeGallery.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Media> Media => Set<Media>();
    public DbSet<Gallery> Galleries => Set<Gallery>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Media>()
            .Property(m => m.Type)
            .HasConversion<string>();

        modelBuilder.Entity<Gallery>()
            .HasMany(gallery => gallery.Media)
            .WithOne(media => media.Gallery)
            .HasForeignKey(media => media.GalleryId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired(false);
    }
}
