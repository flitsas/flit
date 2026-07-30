using Flit.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Hubs;

/// <summary>Hub de progreso/finalización de export jobs (Feature #11076 / ADR-0037 / HU #11107).</summary>
[Authorize]
public sealed class ExportJobsHub(FlitDbContext db) : Hub
{
    public async Task Subscribe(Guid jobId)
    {
        var userId = ResolveUserId();
        if (userId is null)
            throw new HubException("unauthorized");

        var owns = await db.ExportJobs.AsNoTracking()
            .AnyAsync(j => j.Id == jobId && j.OwnerUserId == userId.Value && j.DeletedAt == null)
            .ConfigureAwait(false);
        if (!owns)
            throw new HubException("forbidden");

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(jobId)).ConfigureAwait(false);
    }

    public Task Unsubscribe(Guid jobId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(jobId));

    public static string GroupName(Guid jobId) => $"export-job:{jobId:D}";

    private Guid? ResolveUserId()
    {
        var raw = ExportJobsUserIdProvider.ResolveUserId(Context.User);
        return Guid.TryParse(raw, out var id) ? id : null;
    }
}
