# Copilot Instructions — Vibe Gallery

## Project Overview

Vibe Gallery is a Blazor Web App (.NET 10) using the **Interactive WebAssembly** render mode. It follows the standard Blazor Web App template structure with two projects:

- **Gpusoft.Apps.VibeGallery** — Server project (hosts the app, SSR, static assets).
- **Gpusoft.Apps.VibeGallery.Client** — Client/WASM project (interactive WebAssembly components).

## Tech Stack

- .NET 10 / C# 14
- Blazor Web App with Interactive WebAssembly rendering
- Bootstrap 5 for styling
- SQLite with Entity Framework Core for data persistence
- Nullable reference types enabled
- Implicit usings enabled

## Project Structure

```
Gpusoft.Apps.VibeGallery/            # Server project
??? Components/
?   ??? App.razor                    # Root component
?   ??? Routes.razor                 # Router
?   ??? _Imports.razor               # Global usings
?   ??? Layout/
?   ?   ??? MainLayout.razor         # Main layout
?   ?   ??? NavMenu.razor            # Navigation
?   ??? Pages/                       # Server-rendered pages
??? Data/                            # EF Core data layer
?   ??? AppDbContext.cs              # Main DbContext
?   ??? Migrations/                  # EF migrations
??? wwwroot/                         # Static assets (CSS, images)
??? Program.cs                       # Host builder

Gpusoft.Apps.VibeGallery.Client/     # WASM client project
??? Pages/                           # Interactive WASM pages
??? _Imports.razor                   # Client global usings
??? Program.cs                       # WASM host builder
```

## Coding Conventions

### C# / .NET

- Use file-scoped namespaces.
- Use modern C# features (pattern matching, switch expressions, records for DTOs, raw string literals).
- Follow `Gpusoft.Apps.VibeGallery` namespace hierarchy.
- Use `ArgumentNullException.ThrowIfNull()` for null guards.
- Async methods must end with `Async` and accept `CancellationToken` where appropriate.
- Prefer `ConfigureAwait(false)` in library/service code.

### Entity Framework Core

- Place entities in the `Data/` folder alongside `AppDbContext.cs`.
- Use records for immutable entities (e.g., `public record Image(int Id, string FileName, DateTime UploadedAt);`).
- Add `DbSet<TEntity>` properties to `AppDbContext` as `public DbSet<T> Ts => Set<T>();`.
- Use EF Core conventions for primary keys (e.g., `Id` property).
- Migrations are stored in `Data/Migrations/` and applied automatically on startup via `Database.Migrate()`.
- Use SQLite for local development; connection string in `appsettings.json`.

### Blazor Components

- Place **server-only** or **static SSR** components in `Gpusoft.Apps.VibeGallery/Components/`.
- Place **interactive WebAssembly** components in `Gpusoft.Apps.VibeGallery.Client/`.
- Every page component must include `<PageTitle>`.
- Use `@page` directive for routable components.
- Prefer component parameters (`[Parameter]`) over cascading values unless context is genuinely shared across deep hierarchies.
- Use `[SupplyParameterFromQuery]` for query string binding.
- Extract reusable UI into standalone components without a `@page` directive.

### CSS

- Use Blazor CSS isolation (`Component.razor.css`) for component-scoped styles.
- Use Bootstrap 5 utility classes where possible before writing custom CSS.

### Services & DI

- Register services in `Program.cs` of the appropriate project.
- Services consumed by WASM components must be registered in the Client's `Program.cs`.
- Services that access server-side resources belong in the Server project only.
- Use `HttpClient` via DI for any API calls from the client.

### Naming

- Projects: `Gpusoft.Apps.VibeGallery[.Layer]`
- Namespaces: match folder structure.
- Components: PascalCase, descriptive noun (e.g., `GalleryGrid`, `ImageCard`).
- Pages: PascalCase, named after the feature (e.g., `Gallery`, `Upload`).
- Entities: PascalCase, singular noun (e.g., `Image`, `User`).

## Do NOT

- Do not edit auto-generated files (`*.g.cs`, `obj/` output, `Data/Migrations/*.Designer.cs`).
- Do not add JavaScript unless absolutely required; prefer Blazor/.NET APIs.
- Do not use `@inject IJSRuntime` for tasks achievable with Blazor APIs.
- Do not add new NuGet packages without justification.
- Do not change the target framework or SDK version.
- Do not commit database files (`*.db`) or build artifacts (`bin/`, `obj/`) to version control.
