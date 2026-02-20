# Homepage Gallery Grid — Design Document

> **Status:** Ready for implementation
> **Last updated:** 2025-02-20

---

## 1. Overview

The homepage (`/`) displays all available galleries as a responsive card grid. Each card shows a thumbnail randomly selected from the gallery's **image** media on every page load. Clicking a card navigates to the gallery detail page.

---

## 2. Features & Scope

### 2.1 In scope (this iteration)

| # | Feature | Notes |
|---|---------|-------|
| F1 | **Gallery grid on homepage** | Interactive WebAssembly component fetching from a server API. |
| F2 | **Server API: list galleries** | `GET /api/galleries` — returns gallery summaries with a random image thumbnail per gallery. |
| F3 | **Server API: serve media files** | `GET /api/media/{id}/file` — streams the original media file from disk. Supports HTTP Range requests. |
| F4 | **Thumbnail generation (images)** | Background service using SkiaSharp. Generates AVIF thumbnails (300 px max, quality 63). Stored in `appData/thumbnails/`. |
| F5 | **Server API: serve thumbnails** | `GET /api/media/{id}/thumbnail` — serves the generated thumbnail, or a built-in placeholder if not yet generated. |
| F6 | **Thumbnail generation (videos)** | Background service using FFMpegCore. Extracts a frame at 10 % of video duration. Stored alongside image thumbnails. |
| F7 | **Gallery detail page (placeholder)** | `/gallery/{id}` — scaffold a minimal page, full implementation later. |
| F8 | **Empty gallery placeholder** | Galleries with zero media display a default placeholder image/icon. |
| F9 | **Add `CreatedAt` to Gallery** | Populated from the gallery directory's creation time during media scan. Used for default sort order. |

### 2.2 Out of scope (future)

| Feature | Notes |
|---------|-------|
| User-selected gallery cover | A `CoverMediaId` column on `Gallery`; user picks from gallery media. |
| Sort order picker | UI control to change sort order (name, date, count). Default is most-recent-first. |
| Gallery CRUD from UI | Create/edit/delete galleries from the browser. |
| Pagination / infinite scroll | For large numbers of galleries. |

---

## 3. Resolved Decisions

All previously open questions have been resolved:

| # | Decision | Detail |
|---|----------|--------|
| D1 | **Gallery `CreatedAt`** | Add a `DateTime CreatedAt` column. Populated from the gallery directory's file-system creation time during the media scan. Requires a new EF migration. |
| D2 | **Thumbnail max dimension** | **300 px** on the longest side. Aspect ratio preserved (no cropping). |
| D3 | **Thumbnail format** | **AVIF** (`image/avif`). SkiaSharp supports AVIF encoding via `SKEncodedImageFormat.Avif`. Fallback to **WebP** if AVIF encoding fails at runtime (some native builds may lack the encoder). |
| D4 | **Thumbnail quality** | **63** (SkiaSharp quality parameter; for AVIF this maps to high visual quality at small file size). |
| D5 | **Image processing library** | **SkiaSharp** (`SkiaSharp` + `SkiaSharp.NativeAssets.Linux` for Alpine Docker). |
| D6 | **Thumbnail generation trigger** | **Dedicated background service** (`ThumbnailBackgroundService`). Polls for media records without a thumbnail on disk and generates them asynchronously. Decoupled from the media scan. |
| D7 | **Video frame extraction** | **FFMpegCore** NuGet wrapper. FFmpeg installed in the Docker image via `apk add ffmpeg`. Frame extracted at **10 % of video duration**. Output resized to 300 px max and saved as AVIF. |
| D8 | **Thumbnail API fallback** | `/api/media/{id}/thumbnail` returns a **built-in placeholder image** (embedded SVG or static PNG) when the thumbnail is not yet generated. Never returns 404 for valid media IDs. |
| D9 | **Range header support** | **Yes.** `/api/media/{id}/file` supports HTTP `Range` requests for in-browser video seeking. Use `Results.File(..., enableRangeProcessing: true)`. |
| D10 | **Loading UX** | **Skeleton placeholder cards** while `/api/galleries` is loading. |
| D11 | **Card thumbnail aspect ratio** | **Natural ratio** — thumbnails preserve the original media aspect ratio. Cards use `object-fit: contain` with a consistent max-height so the grid stays visually uniform. |
| D12 | **Video thumbnails in gallery grid** | Random thumbnail selection picks from **Image-type media only**. Video thumbnails are still generated (for future gallery detail / media views) but are not used as gallery cover thumbnails. |
| D13 | **Sort order** | **Most recent first** (`CreatedAt` descending). Future: user-selectable sort. |

---

## 4. Architecture

### 4.1 Data flow

```
Browser (WASM)                          Server
?????????????????                       ??????????????????????????
Home page loads
  ??? HttpClient GET /api/galleries ??? Query DB: galleries + random Image-type media
                                        Return GallerySummary[]
  ??? JSON ??????????????????????????????
Render grid (skeleton cards ? real cards)
  ??? <img src="/api/media/{id}/thumbnail"> per card
                                    ??? Serve AVIF thumbnail from appData/thumbnails/
                                        If missing ? return built-in placeholder image
Click card
  ??? NavigateTo /gallery/{id}
```

### 4.2 Thumbnail generation flow

```
ThumbnailBackgroundService (loop)
  ?
  ??? Query DB: Media records with no thumbnail file on disk
  ?
  ??? For each Image media:
  ?     SkiaSharp: load ? resize (300 px max) ? encode AVIF (quality 63) ? save
  ?
  ??? For each Video media:
        FFMpegCore: extract frame at 10% duration ? pipe to SkiaSharp ? resize ? AVIF ? save
```

### 4.3 Thumbnail storage layout

```
appData/
??? vibegallery.db
??? thumbnails/
    ??? {mediaId}.avif         # image thumbnails (resized)
    ??? {mediaId}.avif         # video thumbnails (extracted frame, resized)
```

### 4.4 Render modes

| Component | Render mode | Reason |
|-----------|-------------|--------|
| `Home.razor` (gallery grid) | Interactive WebAssembly | Client-side fetch, enables future interactivity (search/filter/sort). |
| `GalleryDetail.razor` | Interactive WebAssembly (placeholder) | Will be fully designed later. |

---

## 5. API Design

### 5.1 `GET /api/galleries`

Returns all galleries sorted by `CreatedAt` descending (most recent first).

**Response `200 OK`:**
```json
[
  {
    "id": 1,
    "name": "Vacation 2024",
    "description": "Summer trip photos",
    "mediaCount": 42,
    "thumbnailMediaId": 117,
    "createdAt": "2024-08-15T10:30:00Z"
  }
]
```

| Field | Type | Notes |
|-------|------|-------|
| `id` | `long` | Gallery ID. |
| `name` | `string` | Gallery display name. |
| `description` | `string` | Gallery description (may be empty). |
| `mediaCount` | `int` | Total media count (images + videos). |
| `thumbnailMediaId` | `long?` | Randomly selected from `Image`-type media. `null` if the gallery has no image media. |
| `createdAt` | `DateTime` | Gallery creation timestamp (from directory creation time). |

### 5.2 `GET /api/media/{id}/file`

Streams the original media file from its on-disk path.

- **Content-Type:** from `Media.ContentType`.
- **Range support:** enabled (`enableRangeProcessing: true`).
- **404:** if media ID not found in DB or file missing on disk.
- **Content-Disposition:** inline (browser renders directly).

### 5.3 `GET /api/media/{id}/thumbnail`

Serves the generated AVIF thumbnail.

- **Content-Type:** `image/avif`.
- **Fallback:** if the thumbnail file does not exist on disk, returns a **built-in placeholder image** with `200 OK` (not 404). This keeps the client simple — `<img src>` always works.
- **Cache-Control:** short-lived or no-cache (since thumbnail for the same media ID is stable, but gallery-level random selection changes per page load).

---

## 6. Thumbnail Generation — Images

### 6.1 Background service

`ThumbnailBackgroundService` (single `BackgroundService`):

1. Runs on a timer (e.g., every 30 seconds or on-demand after a scan completes).
2. Queries DB for all `Media` records.
3. For each record, checks if `appData/thumbnails/{mediaId}.avif` exists.
4. If missing, generates the thumbnail.

### 6.2 Spec

| Parameter | Value |
|-----------|-------|
| Max dimension | 300 px (longest side) |
| Aspect ratio | Preserved (no crop) |
| Format | AVIF (fallback: WebP if AVIF encoding unavailable) |
| Quality | 63 |
| Library | SkiaSharp |
| Storage path | `appData/thumbnails/{mediaId}.avif` |

### 6.3 Idempotency

If a thumbnail already exists on disk for a given `mediaId`, skip regeneration. Future enhancement: compare file size or hash to detect source changes.

### 6.4 Service design

A `ThumbnailService` class (non-background, injectable) encapsulates the actual resize + encode logic. The `ThumbnailBackgroundService` uses it to process queued items. The thumbnail endpoint can also reference it for path resolution and placeholder logic.

---

## 7. Thumbnail Generation — Videos

### 7.1 Approach

The same `ThumbnailBackgroundService` handles video thumbnails:

1. For `Media` records where `Type == Video` and no thumbnail exists on disk.
2. Uses `FFMpegCore` to extract a single frame at **10 % of the video's duration**.
3. Pipes the extracted frame to SkiaSharp for resize (300 px max) and AVIF encoding.
4. Saves to `appData/thumbnails/{mediaId}.avif`.

### 7.2 Dependencies

| Dependency | Source | Notes |
|------------|--------|-------|
| `FFMpegCore` | NuGet | .NET wrapper for FFmpeg CLI. |
| `ffmpeg` | Dockerfile | `apk add ffmpeg` in the Alpine base image. |
| `SkiaSharp` | NuGet | Already required for image thumbnails. |

### 7.3 Dockerfile changes

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS base
RUN apk add --no-cache ffmpeg
USER $APP_UID
WORKDIR /app
```

### 7.4 Error handling

- If FFmpeg is not installed or extraction fails: log a warning and **skip** the media. The thumbnail endpoint will return the placeholder image.
- No automatic retry. The background service will re-attempt on the next cycle only if the thumbnail file still doesn't exist. This provides natural retry without complexity.

---

## 8. UI Design

### 8.1 Gallery card

```
????????????????????????
?                      ?
?    [ thumbnail ]     ?  ? natural aspect ratio, max-height constrained
?                      ?
????????????????????????
?  Gallery Name        ?
?  42 items            ?
????????????????????????
```

- **Thumbnail area:** `<img>` with `object-fit: contain` and a consistent `max-height` (e.g., 220 px) so cards align visually.
- **Fallback:** If `thumbnailMediaId` is `null`, the `<img src>` still points to `/api/media/0/thumbnail` which returns the placeholder — or the component renders a CSS-only placeholder icon directly.
- **Card click:** `<a href="/gallery/{id}">` wrapping the entire card.

### 8.2 Grid layout

- Bootstrap `row` + `col-md-4 col-lg-3` (4 columns desktop, 3 tablet, 1 mobile).
- Gap/gutter via Bootstrap `g-3` or `g-4`.

### 8.3 Loading state — skeleton cards

While `GET /api/galleries` is in-flight, render 8 skeleton placeholder cards:
- Same card shape as real cards.
- Animated pulsing background (`placeholder-glow` in Bootstrap 5).
- Replaced with real data once the response arrives.

### 8.4 Empty state

If there are **zero galleries**: centered message with an icon — "No galleries yet. Run a media scan to get started."

---

## 9. Data Model Changes

### 9.1 `Gallery` entity — add `CreatedAt`

```csharp
public class Gallery
{
    // ... existing properties ...
    public DateTime CreatedAt { get; private set; }
}
```

- Constructor sets `CreatedAt` from a parameter.
- `MediaScannerService` reads `Directory.GetCreationTimeUtc(galleryDir)` and passes it when creating new galleries.

### 9.2 EF Migration

A new migration to add the `CreatedAt` column with a default value of `DateTime.UtcNow` for existing rows.

---

## 10. Component & File Plan

### Server project (`Gpusoft.Apps.VibeGallery`)

| File | Action | Purpose |
|------|--------|---------|
| `Data/Gallery.cs` | Modify | Add `CreatedAt` property. |
| `Data/Migrations/...` | New | EF migration for `CreatedAt`. |
| `Endpoints/GalleryEndpoints.cs` | New | `GET /api/galleries` minimal API. |
| `Endpoints/MediaEndpoints.cs` | New | `GET /api/media/{id}/file` and `/thumbnail` minimal APIs. |
| `Services/ThumbnailService.cs` | New | SkiaSharp resize + AVIF encode logic. Placeholder image serving. |
| `Services/ThumbnailBackgroundService.cs` | New | `BackgroundService` that generates missing thumbnails (images + videos). |
| `Services/MediaScannerService.cs` | Modify | Pass directory creation time to `Gallery` constructor. |
| `Program.cs` | Modify | Register new services, map new endpoints. |
| `Dockerfile` | Modify | Add `apk add --no-cache ffmpeg`. |
| `wwwroot/images/placeholder.svg` | New | Default placeholder image for galleries with no media / pending thumbnails. |

### Client project (`Gpusoft.Apps.VibeGallery.Client`)

| File | Action | Purpose |
|------|--------|---------|
| `Models/GallerySummary.cs` | New | DTO record for API response. |
| `Pages/Home.razor` | New | Move from Server to Client. Gallery grid (Interactive WASM). |
| `Pages/Home.razor.css` | New | Scoped styles (skeleton, card sizing). |
| `Pages/GalleryDetail.razor` | New | Placeholder gallery detail page. |
| `Components/GalleryCard.razor` | New | Reusable gallery card component. |
| `Components/GalleryCard.razor.css` | New | Scoped card styles. |

### NuGet packages to add

| Package | Project | Reason |
|---------|---------|--------|
| `SkiaSharp` | Server | Image resizing and AVIF encoding. |
| `SkiaSharp.NativeAssets.Linux` | Server | Native libs for Alpine Docker. |
| `FFMpegCore` | Server | Video frame extraction wrapper. |

---

## 11. Implementation Order

```
Phase 1 — Data & Foundation
  1. Add CreatedAt to Gallery entity + EF migration
  2. Update MediaScannerService to populate CreatedAt from directory creation time
  3. Add SkiaSharp + FFMpegCore NuGet packages
  4. Implement ThumbnailService (resize + AVIF encode logic)
  5. Implement ThumbnailBackgroundService (image + video thumbnail generation)

Phase 2 — API Endpoints
  6. Media file serving endpoint (GET /api/media/{id}/file with Range support)
  7. Thumbnail serving endpoint (GET /api/media/{id}/thumbnail with placeholder fallback)
  8. Gallery list endpoint (GET /api/galleries)
  9. Register services and map endpoints in Program.cs

Phase 3 — UI
  10. GallerySummary DTO in Client project
  11. GalleryCard component + scoped CSS
  12. Home page with gallery grid, skeleton loading, empty state
  13. Gallery detail placeholder page

Phase 4 — Docker
  14. Update Dockerfile to install ffmpeg
  15. Add placeholder SVG to wwwroot

Phase 5 — Validation
  16. Build verification
  17. Manual end-to-end test
```

---

## 12. Risks & Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| SkiaSharp AVIF encoding not available on some platforms | Thumbnails fail to generate | Detect at runtime; fall back to WebP (`SKEncodedImageFormat.Webp`). Log a warning on first fallback. |
| FFmpeg not installed (local dev without Docker) | Video thumbnails skip | Graceful skip with warning log. Image thumbnails still work. Developers can install FFmpeg locally or ignore video thumbnails. |
| Large number of media files on first scan | Background service CPU spike | Process thumbnails in batches with `Task.Delay` between batches. Configurable concurrency. |
| Alpine Docker missing SkiaSharp native deps | Runtime crash | `SkiaSharp.NativeAssets.Linux` NuGet handles this. Verified in Dockerfile with `apk add` for any missing system libs (e.g., `fontconfig`, `libstdc++`). |

---

*All decisions resolved. Document ready for implementation.*
