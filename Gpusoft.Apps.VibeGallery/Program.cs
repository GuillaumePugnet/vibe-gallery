using Gpusoft.Apps.VibeGallery.Client.Pages;
using Gpusoft.Apps.VibeGallery.Components;
using Gpusoft.Apps.VibeGallery.Data;
using Gpusoft.Apps.VibeGallery.Endpoints;
using Gpusoft.Apps.VibeGallery.Hubs;
using Gpusoft.Apps.VibeGallery.Services;
using Microsoft.EntityFrameworkCore;

namespace Gpusoft.Apps.VibeGallery
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.
            builder.Services.AddRazorComponents()
                .AddInteractiveWebAssemblyComponents();

            var dataDir = Path.Combine(builder.Environment.ContentRootPath, "appData");
            Directory.CreateDirectory(dataDir);

            builder.Services.AddDbContext<AppDbContext>(options =>
                options.UseSqlite($"Data Source={Path.Combine(dataDir, "vibegallery.db")}"));

            builder.Services.Configure<MediaScannerOptions>(
                builder.Configuration.GetSection(MediaScannerOptions.SectionName));
            builder.Services.AddSingleton<IMediaScanner, MediaScannerService>();
            builder.Services.AddHostedService<MediaScannerBackgroundService>();
            builder.Services.AddSingleton<ThumbnailService>();
            builder.Services.AddHostedService<ThumbnailBackgroundService>();
            builder.Services.AddSignalR();

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.UseWebAssemblyDebugging();
            }
            else
            {
                app.UseExceptionHandler("/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
            app.UseHttpsRedirection();

            app.UseAntiforgery();

            // Apply pending migrations automatically on startup.
            using (var scope = app.Services.CreateScope())
            {
                scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
            }

            app.MapStaticAssets();
            app.MapHub<ScanHub>("/hubs/scan");
            app.MapRazorComponents<App>()
                .AddInteractiveWebAssemblyRenderMode()
                .AddAdditionalAssemblies(typeof(Client._Imports).Assembly);

            app.MapMediaEndpoints();
            app.MapGalleryEndpoints();

            app.MapPost("/api/scan", async (IMediaScanner scanner, CancellationToken ct) =>
            {
                var result = await scanner.ScanAsync(ct);
                return result is null
                    ? Results.Conflict(new { message = "A scan is already in progress." })
                    : Results.Ok(result);
            });

            app.Run();
        }
    }
}
