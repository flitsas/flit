using System.Security.Claims;
using Flit.Api.Identity;
using Flit.Platform.Sdk.Grpc;
using Grpc.Core;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;

namespace Flit.Api.Authorization;

/// <summary>
/// Quién puede gestionar usuarios de Diagnóstico en la plataforma (ADR-0089 regla 1, SP-14): únicamente el cliente de
/// servicio <c>svc-diagnostico</c> de Identidad, con <c>aud=tramites</c> (la audiencia que core-api valida para sus gRPC de
/// servicio, H-U1) y el scope dedicado. Dos scopes: <see cref="Scope"/> para las operaciones por empresa y
/// <see cref="ScopeConteos"/> para el conteo global del SuperAdmin. Mismo patrón que
/// <see cref="DiagnosticoDerivacionServicioAuthorization"/>; la protección contra la escalada está en el actor, que core-api
/// reverifica en la base (<c>ActorUsuariosProducto</c>).
/// </summary>
internal static class DiagnosticoUsuariosServicioAuthorization
{
    public const string Policy = "DiagnosticoUsuariosServicio";

    public const string PolicyConteos = "DiagnosticoUsuariosConteosServicio";

    public const string Scope = "platform.usuarios.diagnostico";

    public const string ScopeConteos = "platform.usuarios.diagnostico.conteos";

    /// <summary>El <c>sub</c> del cliente de servicio (<c>svc-</c> + código del servicio).</summary>
    public const string ClientId = OidcDefaults.ServiceClientPrefix + "diagnostico";

    /// <summary>El único producto que el scope permite: el cliente queda atado a su producto.</summary>
    public const string Producto = "diagnostico";

    /// <summary>Metadata con el uuid del usuario que actúa en Diagnóstico (contrato §3.4).</summary>
    public const string ActorMetadata = "x-flit-actor-id";

    public static void Configure(AuthorizationPolicyBuilder policy) => Configure(policy, Scope);

    public static void ConfigureConteos(AuthorizationPolicyBuilder policy) => Configure(policy, ScopeConteos);

    private static void Configure(AuthorizationPolicyBuilder policy, string scope)
    {
        ArgumentNullException.ThrowIfNull(policy);
        policy.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
            .RequireAuthenticatedUser()
            .RequireAssertion(context => context.User.Identities.Any(i => EsTokenDeDiagnostico(i, scope)));
    }

    internal static bool EsTokenDeDiagnostico(ClaimsIdentity identity, string scope) =>
        identity.IsAuthenticated
        && string.Equals(identity.FindFirst("sub")?.Value, ClientId, StringComparison.Ordinal)
        && identity.HasClaim("aud", ServiceAudiences.Tramites)
        && identity.FindAll("scope").SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Contains(scope, StringComparer.Ordinal);

    /// <summary>
    /// Segunda barrera, dentro de cada método. HU #14694: delega en
    /// <see cref="UsuariosProductoServicioAuthorization.VerificarLlamante"/> (mapa cliente → producto); para
    /// <c>svc-diagnostico</c> el resultado es el mismo de siempre.
    /// </summary>
    /// <returns>El id del actor de la metadata.</returns>
    /// <exception cref="RpcException"><c>PERMISSION_DENIED</c> si el llamante, el producto o el actor no sirven;
    /// <c>INVALID_ARGUMENT</c> sin empresa.</exception>
    public static Guid VerificarLlamante(PlatformServiceCaller caller, string? producto, Metadata cabeceras, bool exigeEmpresa = true) =>
        UsuariosProductoServicioAuthorization.VerificarLlamante(caller, producto, cabeceras, exigeEmpresa);
}
