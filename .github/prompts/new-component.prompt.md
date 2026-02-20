---
description: Scaffold a new reusable Blazor component
mode: agent
---

# New Blazor Component

Create a new reusable Blazor component for the Vibe Gallery application.

## Component Details

- **Name**: {{component-name}}
- **Description**: {{description}}

## Instructions

1. Determine whether this component needs interactivity:
   - If it is purely presentational or uses only static SSR, create it under `Gpusoft.Apps.VibeGallery/Components/`.
   - If it requires interactive WebAssembly features (event handlers, JS interop, real-time updates), create it under `Gpusoft.Apps.VibeGallery.Client/`.
2. Create `{{component-name}}.razor` with the component markup.
3. If the component needs scoped styles, create `{{component-name}}.razor.css` alongside it.
4. Use `[Parameter]` for all inputs. Mark required parameters with `[EditorRequired]`.
5. Use `EventCallback<T>` for outputs/events.
6. Prefer Bootstrap 5 utility classes for layout and styling.
7. Do **not** add a `@page` directive — this is a reusable component, not a page.
8. Add the component to the relevant `_Imports.razor` if it will be widely used.
