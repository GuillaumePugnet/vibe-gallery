using Microsoft.AspNetCore.SignalR;

namespace Gpusoft.Apps.VibeGallery.Hubs;

/// <summary>
/// SignalR hub for broadcasting media scan progress to connected clients.
/// Clients receive updates via the "ReceiveScanProgress" method.
/// </summary>
public class ScanHub : Hub
{
}
