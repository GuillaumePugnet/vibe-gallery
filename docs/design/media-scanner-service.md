# Media Scanner Service — Design & Progress

## 1. Goal

Provide a server-side service that **scans the filesystem** under a configurable
media root (default `/app/media`), discovers gallery folders and media files
(images and videos), and **synchronises** that state with the SQLite database —
adding new entries and removing stale ones. The service **reports scan progress
in real time** via SignalR (per-gallery granularity).

---

## 2. High-Level Architecture

```
┌────────────┐         ┌───────────────────┐  read/write  ┌───────────┐
│ Admin UI   │─trigger─▶│ MediaScannerService│─────────────▶│ AppDbCtx  │
│ / API      │◀progress─│  (scoped service)  │              └───────────┘
└────────────┘  SignalR │                    │  enumerate
       ▲                │                    │─────────────▶ /app/media
       │                └───────────────────┘               filesystem
       │                        │
       └────────────────────────┘
              ScanHub
```

### Components

| Component | Location | Responsibility |
|---|---|---|
| `IMediaScanner` | `Services/IMediaScanner.cs` | Defines the `ScanAsync` contract |
| `MediaScannerService` | `Services/MediaScannerService.cs` | Scan logic: diff filesystem ↔ DB |
| `MediaScannerOptions` | `Services/MediaScannerOptions.cs` | Config POCO bound from `appsettings.json` |
| `ScanResult` | `Services/ScanResult.cs` | Final summary DTO (added/removed counts) |
| `ScanProgress` | `Services/ScanProgress.cs` | Per-gallery progress DTO pushed via SignalR |
| `MediaType` | `Data/MediaType.cs` | Enum: `Image`, `Video` |
| `ScanHub` | `Hubs/ScanHub.cs` | SignalR hub for real-time progress |
| `MediaScannerBackgroundService` | `Services/MediaScannerBackgroundService.cs` | `BackgroundService` that runs the initial scan on startup |
| Minimal API endpoint | `Program.cs` | `POST /api/scan` on-demand trigger |

---

## 3. Confirmed Design Decisions

| # | Decision | Answer |
|---|---|---|
| **D1** | Media root path | **Configurable** via `MediaScannerOptions.MediaRoot` in `appsettings.json`. Default: `/app/media`. |
| **D2** | Gallery depth | **Top-level only** — each direct subdirectory of the media root = one gallery. |
| **D3** | Supported file types | **Images:** `.jpg`, `.jpeg`, `.png`, `.gif`, `.webp`, `.svg`, `.bmp`, `.avif`, `.jxl`, `.tiff`, `.tif`, `.ico`, `.heic`, `.heif` — **Videos:** `.mp4`, `.webm`, `.mov`, `.avi`, `.mkv`, `.wmv`, `.m4v` |
| **D4** | Deleted media policy | **Hard-delete** DB rows. Cascade delete removes media when a gallery is removed. |
| **D5** | Gallery naming | `Name` defaults to folder name, `Description` defaults to `""`. Both **modifiable later** via entity mutation methods. |
| **D6** | Media metadata | `ContentType` from extension → MIME mapping. `FileSize` from `FileInfo.Length`. `Type` (`MediaType` enum) derived from extension. |
| **D7** | Concurrency | **Serialised** via `SemaphoreSlim(1, 1)`. Concurrent trigger returns "scan already in progress". |
| **D8** | Trigger mechanism | **MVP:** `POST /api/scan` endpoint + auto-scan on startup. **Later:** admin UI button, optional schedule. |
| **D9** | Scan on startup | **Yes** — runs as a **background task** (`BackgroundService`) after migrations. App serves requests immediately. |
| **D10** | Entity IDs | **Auto-generated** by SQLite. Remove `id` parameter from public constructors. |
| **D11** | Entity rename | Rename `Image` → **`Media`** and `Gallery.Images` → **`Gallery.Media`**, `DbSet<Image> Images` → **`DbSet<Media> Media`**. |
| **D12** | Media type enum | Add **`MediaType`** enum (`Image`, `Video`) as a property on the `Media` entity. Derived from file extension at scan time. |
| **D13** | Progress notifications | **SignalR hub** pushes `ScanProgress` per gallery to connected clients. |
| **D14** | Progress granularity | **Per gallery** — one progress event after each gallery is scanned. |

---

## 4. Scan Algorithm

```
ScanAsync(CancellationToken ct)
│
├─ 1. Acquire SemaphoreSlim — if busy, return "scan already in progress"
│
├─ 2. Enumerate subdirectories of MediaRoot          → fsGalleries
├─ 3. Load all galleries from DB                     → dbGalleries
│
├─ 4. Diff galleries (by relative Path)
│   ├─ added   = fsGalleries \ dbGalleries
│   ├─ removed = dbGalleries \ fsGalleries
│   └─ kept    = intersection
│
├─ 5. Insert new Gallery rows (Name = folder name)
├─ 6. Remove gone Gallery rows (cascade deletes Media)
├─ 7. SaveChangesAsync — persist gallery changes
├─ 8. ** Push ScanProgress: "Gallery sync complete: +N added, −M removed" **
│
├─ 9. For each gallery (kept + newly added), index = i:
│   ├─ Enumerate media files matching allowed extensions → fsMedia
│   ├─ Load gallery's media from DB                     → dbMedia
│   ├─ added   = fsMedia \ dbMedia   (by relative Path)
│   ├─ removed = dbMedia \ fsMedia   (by relative Path)
│   ├─ Insert new Media rows (ContentType, FileSize, MediaType)
│   ├─ Remove gone Media rows
│   └─ ** Push ScanProgress: "Gallery '{name}' ({i}/{total}): +X added, −Y removed" **
│
├─ 10. SaveChangesAsync — persist media changes
├─ 11. Release SemaphoreSlim
└─ 12. Return ScanResult
```

---

## 5. Entity Changes

### Rename: `Image` → `Media`

| Property | Type | Notes |
|---|---|---|
| `Id` | `long` | Auto-generated by SQLite. **Remove from constructor.** |
| `Path` | `string` | Relative path from media root (e.g. `holidays/photo1.jpg`). |
| `ContentType` | `string` | MIME type derived from extension (e.g. `image/jpeg`, `video/mp4`). |
| `FileSize` | `long` | `FileInfo.Length` at scan time. |
| `Type` | `MediaType` | Enum: `Image` or `Video`. Derived from extension. |
| `Tags` | `string?` | User-defined tags (future). |
| `GalleryId` | `long?` | FK to `Gallery`. |
| `Gallery` | `Gallery?` | Navigation property. |

**New constructor:**
```csharp
public Media(string path, string contentType, long fileSize, MediaType type,
             string? tags = null, long? galleryId = null)
```

### `Gallery` — updates

| Change | Detail |
|---|---|
| Remove `id` from constructor | Let SQLite auto-generate `Id`. |
| Rename `Images` → `Media` | `public List<Media> Media { get; private set; } = new();` |
| Add `UpdateName(string name)` | Mutator for future admin editing. |
| Add `UpdateDescription(string desc)` | Mutator for future admin editing. |

**New constructor:**
```csharp
public Gallery(string name, string description, string path)
```

### New: `MediaType` enum

```csharp
public enum MediaType
{
    Image,
    Video
}
```

### `AppDbContext` — updates

| Change | Detail |
|---|---|
| Rename DbSet | `public DbSet<Media> Media => Set<Media>();` |
| Rename DbSet | `public DbSet<Gallery> Galleries => Set<Gallery>();` (unchanged) |
| Cascade delete | Configure `Gallery → Media` with `DeleteBehavior.Cascade`. |
| Store enum as string | `modelBuilder.Entity<Media>().Property(m => m.Type).HasConversion<string>();` |

### EF Migration

A new migration is required after the entity rename and constructor changes.

---

## 6. Progress Notification Design

### `ScanProgress` DTO

```csharp
public record ScanProgress(
    string Phase,            // "Galleries" or "Media"
    string? GalleryName,     // null during gallery phase
    int ProcessedGalleries,  // galleries processed so far
    int TotalGalleries,      // total galleries to scan
    int MediaAdded,          // media files added in this gallery
    int MediaRemoved,        // media files removed in this gallery
    string Message           // human-readable status line
);
```

### `ScanResult` DTO

```csharp
public record ScanResult(
    int GalleriesAdded,
    int GalleriesRemoved,
    int MediaAdded,
    int MediaRemoved,
    TimeSpan Duration
);
```

### SignalR Hub

- **Route:** `/hubs/scan`
- **Client method:** `ReceiveScanProgress(ScanProgress progress)`
- `MediaScannerService` receives `IHubContext<ScanHub>` and pushes updates after each gallery is processed.

---

## 7. Configuration

```jsonc
// appsettings.json
{
  "MediaScanner": {
    "MediaRoot": "/app/media",
    "SupportedImageExtensions": [
      ".jpg", ".jpeg", ".png", ".gif", ".webp", ".svg", ".bmp",
      ".avif", ".jxl", ".tiff", ".tif", ".ico", ".heic", ".heif"
    ],
    "SupportedVideoExtensions": [
      ".mp4", ".webm", ".mov", ".avi", ".mkv", ".wmv", ".m4v"
    ]
  }
}
```

```csharp
public class MediaScannerOptions
{
    public string MediaRoot { get; set; } = "/app/media";
    public string[] SupportedImageExtensions { get; set; } = [ ".jpg", ".jpeg", ... ];
    public string[] SupportedVideoExtensions { get; set; } = [ ".mp4", ".webm", ... ];

    // Convenience: all supported extensions combined
    public IReadOnlySet<string> AllSupportedExtensions => ...;

    // Convenience: determine MediaType from extension
    public MediaType GetMediaType(string extension) => ...;
}
```

---

## 8. Implementation Plan

| Phase | Task | Files | Status |
|---|---|---|---|
| **P0a** | Rename `Image` entity to `Media`, add `MediaType` property | `Data/Media.cs`, `Data/MediaType.cs` | ✅ |
| **P0b** | Update `Gallery` entity (auto-ID, rename nav prop, add mutation methods) | `Data/Gallery.cs` | ✅ |
| **P0c** | Update `AppDbContext` (renamed DbSet, cascade, enum conversion) | `Data/AppDbContext.cs` | ✅ |
| **P0d** | Fix all compile references from `Image` → `Media` across the solution | Various | ✅ |
| **P0e** | Generate EF migration for entity changes | `Data/Migrations/` | ✅ |
| **P1** | Create `MediaScannerOptions` config class | `Services/MediaScannerOptions.cs` | ✅ |
| **P2** | Define `IMediaScanner`, `ScanResult`, `ScanProgress` DTOs | `Services/` | ✅ |
| **P3** | Create `ScanHub` (SignalR) | `Hubs/ScanHub.cs` | ✅ |
| **P4** | Implement `MediaScannerService` (scan algorithm + SignalR progress) | `Services/MediaScannerService.cs` | ✅ |
| **P5** | Create `MediaScannerBackgroundService` (startup scan) | `Services/MediaScannerBackgroundService.cs` | ✅ |
| **P6** | Register services, SignalR, bind config, map hub in `Program.cs` | `Program.cs` | ✅ |
| **P7** | Add `POST /api/scan` minimal API endpoint | `Program.cs` | ✅ |
| **P8** | Add config section to `appsettings.json` | `appsettings.json` | ✅ |
| **P9** | Admin UI scan button + progress display *(deferred)* | `Components/Pages/` | ⬜ |
| **P10** | Unit tests for scan diffing logic *(deferred)* | `Tests/` | ⬜ |

---

## 9. File Locations (planned)

```
Gpusoft.Apps.VibeGallery/
├── Data/
│   ├── Gallery.cs              ← modified (auto-ID, Media nav, mutation methods)
│   ├── Media.cs                ← renamed from Image.cs (auto-ID, MediaType)
│   ├── MediaType.cs            ← new enum
│   └── AppDbContext.cs         ← modified (DbSet rename, cascade, enum conversion)
├── Hubs/
│   └── ScanHub.cs              ← new
├── Services/
│   ├── IMediaScanner.cs                   ← new
│   ├── MediaScannerService.cs             ← new
│   ├── MediaScannerBackgroundService.cs   ← new
│   ├── MediaScannerOptions.cs             ← new
│   ├── ScanResult.cs                      ← new
│   └── ScanProgress.cs                    ← new
└── Program.cs                             ← modified (DI, SignalR, endpoint)
```

---

## 10. Open Items

All design decisions are **confirmed**. No blocking questions remain.
Future items tracked but not in scope for this implementation:

- Admin UI page with scan button and real-time progress bar (P9).
- Scheduled periodic scan via `IHostedService` timer.
- `Media.Hash` (SHA-256) for detecting replaced files with the same name.
- `Gallery.LastScannedAt` / `Media.DiscoveredAt` timestamps.