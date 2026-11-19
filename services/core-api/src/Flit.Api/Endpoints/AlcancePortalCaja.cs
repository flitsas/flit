using System.Security.Claims;
using Flit.Api.Authorization;
using Flit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Flit.Api.Endpoints;

/// <summary>Organismo sobre el que trabaja el administrador: su tenant (donde viven los usuarios de caja) y el organismo del catálogo.</summary>
internal sealed record AlcanceCaja(Guid TenantId, Guid TransitOfficeId);

/// <summary>
/// HU #14454 / #14455 / #14461 (Épica #14210, ADR-0082) — alcance de «Usuarios de caja» y «Cobros de caja». Mismo criterio que
/// <c>AdminPortalCobroEndpoints</c>: el <c>ot_admin</c> trabaja con el organismo de su token y un identificador ajeno da 403 sin
/// ejecutar nada; el SuperAdmin envía <c>?transitOfficeId=</c>, obligatorio. Los errores son <c>{ code, message }</c>.
/// </summary>
internal static class AlcancePortalCaja
{
    public const string OrganismoRequerido = "ORGANISMO_REQUERIDO";
    public const string OrganismoNoExiste = "ORGANISMO_NO_EXISTE";
    public const string SinOrganismo = "SIN_ORGANISMO";
    public const string OtroOrganismo = "OTRO_ORGANISMO";

    public static async Task<(AlcanceCaja? Alcance, IResult? Error)> ResolverAsync(
        ClaimsPrincipal user, Guid? transitOfficeId, FlitDbContext db, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(db);
        var pedido = transitOfficeId is { } t && t != Guid.Empty ? t : (Guid?)null;

        if (EsSuperAdmin(user))
        {
            if (pedido is not { } organismo)
                return (null, Error(StatusCodes.Status400BadRequest, OrganismoRequerido, "Indique el organismo (transitOfficeId)."));

            var tenant = await db.TransitOfficeProfiles.AsNoTracking()
                .Where(p => p.TransitOfficeId == organismo).Select(p => (Guid?)p.TenantId)
                .FirstOrDefaultAsync(ct).ConfigureAwait(false);
            return tenant is { } tenantId
                ? (new AlcanceCaja(tenantId, organismo), null)
                : (null, Error(StatusCodes.Status404NotFound, OrganismoNoExiste, "El organismo no existe o no tiene un perfil de tránsito."));
        }

        if (!RequestTenantResolver.TryResolveTenantId(user, out var callerTenant)
            || await db.TransitOfficeProfiles.AsNoTracking().Where(p => p.TenantId == callerTenant)
                .Select(p => new { p.TransitOfficeId }).FirstOrDefaultAsync(ct).ConfigureAwait(false) is not { } perfil)
            return (null, Error(StatusCodes.Status403Forbidden, SinOrganismo, "Su usuario no tiene un organismo de tránsito asociado."));

        if (pedido is { } enviado && enviado != perfil.TransitOfficeId)
            return (null, Error(StatusCodes.Status403Forbidden, OtroOrganismo, "No puede ver ni cambiar la caja de otro organismo."));

        return (new AlcanceCaja(callerTenant, perfil.TransitOfficeId), null);
    }

    public static IResult Error(int status, string code, string message) => Results.Json(new { code, message }, statusCode: status);

    private static bool EsSuperAdmin(ClaimsPrincipal user) =>
        user.IsInRole(AdminAuthorization.SuperAdminRole)
        || user.Claims.Any(c => c.Type == AdminAuthorization.RoleClaimType
            && string.Equals(c.Value, AdminAuthorization.SuperAdminRole, StringComparison.OrdinalIgnoreCase));
}
