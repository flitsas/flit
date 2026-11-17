using System.Security.Claims;
using Flit.Platform.Sdk.Authentication;
using Microsoft.AspNetCore.Authorization;

namespace Flit.Diagnostico.Api.Modulos.Backoffice;

/// <summary>
/// Política del backoffice de usuarios (HU #14587, contrato-api §3.2, SP-13, SP-18): <b>solo el SuperAdmin</b>, de cualquier compañía. A
/// diferencia de <c>PoliticaBackoffice</c>, un usuario de la compañía FLIT con <c>diagnostico.backoffice.ver</c> no pasa: gestionar los
/// usuarios de otra compañía no es parte del backoffice de lectura. Un token de servicio nunca pasa (los servicios entran por gRPC).
/// </summary>
public static class PoliticaUsuariosBackoffice
{
    public const string Nombre = "diagnostico.backoffice.usuarios";

    public static bool Cumple(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return user.Identity?.IsAuthenticated == true && !PlatformPrincipal.IsService(user) && PlatformPrincipal.IsSuperAdmin(user);
    }

    internal static AuthorizationBuilder AddPoliticaUsuariosBackoffice(this AuthorizationBuilder builder) =>
        builder.AddPolicy(Nombre, p => p.RequireAuthenticatedUser().RequireAssertion(c => Cumple(c.User)));
}
