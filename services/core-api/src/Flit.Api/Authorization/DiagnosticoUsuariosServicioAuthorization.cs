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
    /// Segunda barrera, dentro de cada método: el llamante es Diagnóstico, el producto es el suyo y trae un actor.
    /// <paramref name="exigeEmpresa"/> en false para el servicio global de conteos.
    /// </summary>
    /// <returns>El id del actor de la metadata.</returns>
    /// <exception cref="RpcException"><c>PERMISSION_DENIED</c> si el llamante, el producto o el actor no sirven;
    /// <c>INVALID_ARGUMENT</c> sin empresa.</exception>
    public static Guid VerificarLlamante(PlatformServiceCaller caller, string? producto, Metadata cabeceras, bool exigeEmpresa = true)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(cabeceras);
        if (!string.Equals(caller.ClientId, ClientId, StringComparison.Ordinal))
            throw new RpcException(new Status(StatusCode.PermissionDenied, "Este servicio solo lo atiende Diagnóstico Vehicular."));
        if (exigeEmpresa && caller.TenantId == Guid.Empty)
            throw new RpcException(new Status(StatusCode.InvalidArgument, $"Falta la metadata {PlatformServiceCallInterceptor.TenantMetadata} con el id de la empresa."));
        if (!string.Equals(producto, Producto, StringComparison.Ordinal))
            throw new RpcException(new Status(StatusCode.PermissionDenied, "El producto no corresponde al cliente de servicio."));
        if (!Guid.TryParse(cabeceras.GetValue(ActorMetadata), out var actor) || actor == Guid.Empty)
            throw new RpcException(new Status(StatusCode.PermissionDenied, $"Falta la metadata {ActorMetadata} con el usuario que actúa."));
        return actor;
    }
}
