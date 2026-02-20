---
description: Add an API endpoint to the server project
mode: agent
---

# Add API Endpoint

Add a new minimal API endpoint to the Vibe Gallery server.

## Endpoint Details

- **Route**: {{route}}
- **HTTP Method**: {{method}}
- **Description**: {{description}}

## Instructions

1. Add the endpoint mapping in `Gpusoft.Apps.VibeGallery/Program.cs` using minimal API syntax (`app.MapGet`, `app.MapPost`, etc.) before `app.Run()`.
2. If the endpoint logic is non-trivial, extract a handler or service class:
   - Create the service under a `Services/` folder in the server project.
   - Register it in DI in `Program.cs`.
3. Use `TypedResults` for return types to get OpenAPI metadata.
4. Accept `CancellationToken` as the last parameter in async handlers.
5. Validate inputs using `ArgumentNullException.ThrowIfNull()` or parameter validation attributes.
6. If the client project needs to call this endpoint, create a typed client service in `Gpusoft.Apps.VibeGallery.Client/` using `HttpClient`.
