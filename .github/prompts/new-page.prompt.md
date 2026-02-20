---
description: Scaffold a new Blazor page
mode: agent
---

# New Blazor Page

Create a new routable Blazor page for the Vibe Gallery application.

## Page Details

- **Name**: {{page-name}}
- **Route**: {{route}}
- **Description**: {{description}}

## Instructions

1. Determine interactivity needs:
   - **Static SSR or server-only** ? create under `Gpusoft.Apps.VibeGallery/Components/Pages/`.
   - **Interactive WebAssembly** ? create under `Gpusoft.Apps.VibeGallery.Client/Pages/`.
2. Create `{{page-name}}.razor` with:
   - `@page "{{route}}"` directive.
   - `<PageTitle>` set to a user-friendly title.
   - A heading and initial layout using Bootstrap 5.
3. If scoped styles are needed, create `{{page-name}}.razor.css`.
4. Add a navigation entry in `Gpusoft.Apps.VibeGallery/Components/Layout/NavMenu.razor` using a `<NavLink>` inside a `nav-item` div, following the existing pattern.
5. If the page depends on a service, register the service in the appropriate `Program.cs`.
