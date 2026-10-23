using System.Security.Claims;
using Flit.Admin.Application.Companies.MandateSigners;
using Flit.Admin.Domain.Companies.MandateSigners;

namespace Flit.Api.Authorization;

/// <summary>
/// HU #13134 (Feature #13115) — traduce el principal HTTP al rol con el que se aplica la regla única de permisos por
/// origen (<see cref="MandateSignerOriginRules"/>) y arma las respuestas 403/404 de esa regla. El rol lo fija la
/// RUTA (el hub OT lo gobierna <c>OtAdminOrSuperAdminPolicy</c>; las de compañía, <c>AdminCompanyPolicy</c> o la
/// policy de cabeza de red), de modo que un usuario con varios roles actúa con el de la superficie que usa.
/// </summary>
public static class MandateSignerActors
{
    /// <summary>Rol en las rutas del hub OT: Super Admin o, si no, Admin OT.</summary>
    public static MandateSignerActorKind ForHub(ClaimsPrincipal user) =>
        RequestTenantResolver.IsSuperAdmin(user)
            ? MandateSignerActorKind.SuperAdmin
            : RequestTenantResolver.HasRole(user, AdminAuthorization.OtAdminRole)
                ? MandateSignerActorKind.OtAdmin
                : MandateSignerActorKind.None;

    /// <summary>Rol en las rutas de la compañía (propias o de hijas): Super Admin o, si no, Admin de Compañía.</summary>
    public static MandateSignerActorKind ForCompany(ClaimsPrincipal user) =>
        RequestTenantResolver.IsSuperAdmin(user)
            ? MandateSignerActorKind.SuperAdmin
            : RequestTenantResolver.HasRole(user, AdminAuthorization.AdminCompanyRole)
                ? MandateSignerActorKind.CompanyAdmin
                : MandateSignerActorKind.None;

    /// <summary>
    /// Comprobación previa a escribir sobre un mandatario desde una ruta de compañía. <c>null</c> = adelante;
    /// si no, la respuesta: 403 con <c>mandatario_configurado_por_organismo</c> (candado), 403
    /// <c>mandatario_sin_permiso</c> o 404 sin revelar nada más.
    /// </summary>
    public static async Task<IResult?> CheckCompanyWriteAsync(
        MandateSignerAccessGuard guard,
        ClaimsPrincipal user,
        Guid companyTenantId,
        Guid mandateSignerId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(guard);

        var access = await guard
            .CheckCompanyWriteAsync(ForCompany(user), companyTenantId, mandateSignerId, cancellationToken)
            .ConfigureAwait(false);

        return ToResult(access, mandateSignerId);
    }

    private static IResult? ToResult(MandateSignerAccess access, Guid mandateSignerId) =>
        access switch
        {
            MandateSignerAccess.Allowed => null,
            MandateSignerAccess.NotFound =>
                Results.NotFound(new { error = $"No existe el mandatario {mandateSignerId} en esta compañía." }),
            MandateSignerAccess.LockedByOtOrigin => Results.Json(
                new { code = MandateSignerOriginRules.LockedErrorCode, error = MandateSignerOriginRules.LockedMessage },
                statusCode: StatusCodes.Status403Forbidden),
            _ => Results.Json(
                new { code = MandateSignerOriginRules.ForbiddenErrorCode, error = MandateSignerOriginRules.ForbiddenMessage },
                statusCode: StatusCodes.Status403Forbidden),
        };

    /// <summary>
    /// Comprobación previa a «Consultar estado» de la validación desde una ruta de compañía. Igual que
    /// <see cref="CheckCompanyWriteAsync"/> salvo el candado por origen: consultar no cambia la ficha, solo
    /// sincroniza lo que el proveedor ya resolvió, así que la compañía también puede hacerlo con un mandatario
    /// que configuró el organismo.
    /// </summary>
    public static async Task<IResult?> CheckCompanyConsultAsync(
        MandateSignerAccessGuard guard,
        ClaimsPrincipal user,
        Guid companyTenantId,
        Guid mandateSignerId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(guard);

        var access = await guard
            .CheckCompanyWriteAsync(ForCompany(user), companyTenantId, mandateSignerId, cancellationToken)
            .ConfigureAwait(false);

        return access is MandateSignerAccess.LockedByOtOrigin ? null : ToResult(access, mandateSignerId);
    }
}
