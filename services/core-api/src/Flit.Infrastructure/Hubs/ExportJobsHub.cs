using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Flit.Infrastructure.Hubs;

/// <summary>Hub de progreso/finalización de export jobs (Feature #11076 / ADR-0037).</summary>
[Authorize]
public sealed class ExportJobsHub : Hub
{
    public Task Subscribe(Guid jobId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, GroupName(jobId));

    public Task Unsubscribe(Guid jobId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(jobId));

    public static string GroupName(Guid jobId) => $"export-job:{jobId:D}";
}
