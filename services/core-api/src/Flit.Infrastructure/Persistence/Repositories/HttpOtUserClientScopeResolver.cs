using System.Security.Claims;
using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// <see cref="IOtUserClientScopeResolver"/> sobre el usuario del JWT de la petición en curso. Lee
/// <c>admin.ot_user_client_scopes</c> (sin RLS; el llamador ya está bajo <c>row_security = off</c> cuando
/// corresponde). El SuperAdmin no es usuario del OT: nunca se restringe. Sin petición HTTP o sin <c>sub</c>
/// (jobs, integraciones por API key) tampoco hay usuario al que restringir. Cachea por organismo durante la
/// petición (servicio scoped).
/// </summary>
internal sealed class HttpOtUserClientScopeResolver : IOtUserClientScopeResolver
{
    private const string SuperAdminRole = "SuperAdmin";

    private readonly FlitDbContext _db;
    private readonly IHttpContextAccessor _http;
    private readonly Dictionary<Guid, IReadOnlyList<Guid>?> _cache = [];

    public HttpOtUserClientScopeResolver(FlitDbContext db, IHttpContextAccessor http)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _http = http ?? throw new ArgumentNullException(nameof(http));
    }

    public async Task<IReadOnlyList<Guid>?> GetRestrictionAsync(
        Guid transitOfficeId, CancellationToken cancellationToken = default)
    {
        var principal = _http.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true || principal.IsInRole(SuperAdminRole))
        {
            return null;
        }

        if (!Guid.TryParse(principal.FindFirstValue("sub"), out var userId))
        {
            return null;
        }

        if (_cache.TryGetValue(transitOfficeId, out var cached))
        {
            return cached;
        }

        var restriction = await OtUserClientScopeReader
            .ReadAsync(_db, userId, transitOfficeId, cancellationToken).ConfigureAwait(false);
        _cache[transitOfficeId] = restriction;
        return restriction;
    }
}

/// <summary>Lectura compartida del alcance de un usuario en un organismo.</summary>
internal static class OtUserClientScopeReader
{
    /// <returns><c>null</c> si no hay fila o el modo es <c>todos</c>; si no, las compañías asignadas.</returns>
    public static async Task<IReadOnlyList<Guid>?> ReadAsync(
        FlitDbContext db, Guid userId, Guid transitOfficeId, CancellationToken cancellationToken)
    {
        var scope = await db.OtUserClientScopes
            .AsNoTracking()
            .Where(s => s.UserId == userId && s.TransitOfficeId == transitOfficeId)
            .Select(s => new { s.Id, s.Mode })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (scope is null || scope.Mode != OtUserClientScopeModes.Especificos)
        {
            return null;
        }

        return await db.OtUserClientScopeClients
            .AsNoTracking()
            .Where(c => c.ScopeId == scope.Id)
            .Select(c => c.ClientTenantId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
