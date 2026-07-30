using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;

namespace Flit.Infrastructure.Hubs;

/// <summary>
/// Mapea JWT <c>sub</c> / NameIdentifier → SignalR UserId para Clients.User(ownerId) (HU #11107 / AC1).
/// </summary>
public sealed class ExportJobsUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) =>
        ResolveUserId(connection.User);

    /// <summary>Resuelve el user id GUID desde claims JWT (testeable sin HubConnectionContext).</summary>
    public static string? ResolveUserId(ClaimsPrincipal? user)
    {
        var raw = user?.FindFirstValue("sub")
            ?? user?.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out _) ? raw : null;
    }
}
