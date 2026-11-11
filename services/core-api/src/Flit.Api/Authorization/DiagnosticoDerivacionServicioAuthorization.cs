using System.Security.Claims;
using Flit.Api.Identity;
using Flit.Platform.Sdk.Grpc;
using Grpc.Core;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;

namespace Flit.Api.Authorization;

/// <summary>
/// Quién puede crear borradores de trámite desde Diagnóstico Vehicular (ADR-0084, diseño §3 regla 1): únicamente el
/// cliente de servicio <c>svc-diagnostico</c> de Identidad, con <c>aud=tramites</c> y el scope
/// <c>platform.tramites.derivacion</c>. Otro cliente de servicio, aunque tenga el scope, no entra.
/// <para>
/// A diferencia del portal de persona natural, no hay una empresa fija: Diagnóstico actúa por la empresa del lote, que viaja
/// en <c>x-flit-tenant-id</c>. La protección contra una escalada de privilegios no está aquí sino en el autor: el borrador
/// queda a nombre de un usuario real de ESA empresa con permisos de creación (<c>AutorDerivacion</c>).
/// </para>
/// </summary>
internal static class DiagnosticoDerivacionServicioAuthorization
{
    public const string Policy = "DiagnosticoDerivacionServicio";

    public const string Scope = "platform.tramites.derivacion";

    /// <summary>El <c>sub</c> del cliente de servicio (<c>svc-</c> + código del servicio).</summary>
    public const string ClientId = OidcDefaults.ServiceClientPrefix + "diagnostico";

    public static void Configure(AuthorizationPolicyBuilder policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        policy.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
            .RequireAuthenticatedUser()
            .RequireAssertion(context => context.User.Identities.Any(EsTokenDeDiagnostico));
    }

    internal static bool EsTokenDeDiagnostico(ClaimsIdentity identity) =>
        identity.IsAuthenticated
        && string.Equals(identity.FindFirst("sub")?.Value, ClientId, StringComparison.Ordinal)
        && identity.HasClaim("aud", ServiceAudiences.Tramites)
        && identity.FindAll("scope").SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Contains(Scope, StringComparer.Ordinal);

    /// <summary>
    /// Segunda barrera, dentro de cada método (la policy ya filtró el token): el llamante que dejó el interceptor del SDK
    /// tiene que ser Diagnóstico y traer empresa.
    /// </summary>
    /// <exception cref="RpcException"><c>PERMISSION_DENIED</c> si el llamante es otro; <c>INVALID_ARGUMENT</c> sin empresa.</exception>
    public static void VerificarLlamante(PlatformServiceCaller caller)
    {
        ArgumentNullException.ThrowIfNull(caller);
        if (!string.Equals(caller.ClientId, ClientId, StringComparison.Ordinal))
            throw new RpcException(new Status(StatusCode.PermissionDenied, "Este servicio solo lo atiende Diagnóstico Vehicular."));
        if (caller.TenantId == Guid.Empty)
            throw new RpcException(new Status(StatusCode.InvalidArgument, $"Falta la metadata {PlatformServiceCallInterceptor.TenantMetadata} con el id de la empresa."));
    }
}
