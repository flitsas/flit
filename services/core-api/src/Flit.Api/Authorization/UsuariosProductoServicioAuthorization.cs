using System.Collections.Frozen;
using System.Security.Claims;
using Flit.Api.Identity;
using Flit.Platform.Sdk.Grpc;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;

namespace Flit.Api.Authorization;

/// <summary>
/// HU #14694 — quién puede gestionar usuarios de un producto en la plataforma: ADR-0089 (SP-14) generalizado a un mapa
/// cliente de servicio → producto. Cada cliente (<c>svc-&lt;código&gt;</c>) queda atado a su producto y a sus dos scopes:
/// <c>platform.usuarios.&lt;código&gt;</c> (por empresa) y <c>platform.usuarios.&lt;código&gt;.conteos</c> (global del
/// SuperAdmin), siempre con <c>aud=tramites</c>. Hoy: <c>svc-diagnostico → diagnostico</c> y <c>svc-impuestos → impuestos</c>.
/// La protección contra la escalada sigue en el actor, que core-api reverifica en la base (<c>ActorUsuariosProducto</c>).
/// </summary>
/// <remarks>
/// <see cref="DiagnosticoUsuariosServicioAuthorization"/> conserva sus constantes y su policy y delega aquí la verificación
/// del llamante: para <c>svc-diagnostico</c> el comportamiento no cambia.
/// </remarks>
internal static class UsuariosProductoServicioAuthorization
{
    public const string Policy = "UsuariosProductoServicio";

    public const string PolicyConteos = "UsuariosProductoConteosServicio";

    /// <summary>Metadata con el uuid del usuario que actúa en el producto (contrato §3.4).</summary>
    public const string ActorMetadata = "x-flit-actor-id";

    /// <summary>Cliente de servicio → producto cuyos usuarios gestiona.</summary>
    private static readonly FrozenDictionary<string, string> Productos = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [OidcDefaults.ServiceClientPrefix + "diagnostico"] = "diagnostico",
        [OidcDefaults.ServiceClientPrefix + "impuestos"] = "impuestos",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    public static void Configure(AuthorizationPolicyBuilder policy) => Configure(policy, conteos: false);

    public static void ConfigureConteos(AuthorizationPolicyBuilder policy) => Configure(policy, conteos: true);

    /// <summary>El producto del cliente de servicio, o <c>null</c> si no gestiona usuarios de ningún producto.</summary>
    public static string? ProductoDe(string? clientId) =>
        clientId is not null && Productos.TryGetValue(clientId, out var producto) ? producto : null;

    /// <summary>Scope que debe traer el token del cliente (por empresa o de conteos), o <c>null</c> si no está en el mapa.</summary>
    public static string? ScopeDe(string? clientId, bool conteos) =>
        ProductoDe(clientId) is { } producto ? $"platform.usuarios.{producto}{(conteos ? ".conteos" : string.Empty)}" : null;

    /// <summary>Token de un cliente del mapa, con <c>aud=tramites</c> y el scope de su producto.</summary>
    internal static bool EsTokenDeProducto(ClaimsIdentity identity, bool conteos)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return identity.IsAuthenticated
               && ScopeDe(identity.FindFirst("sub")?.Value, conteos) is { } scope
               && identity.HasClaim("aud", ServiceAudiences.Tramites)
               && identity.FindAll("scope").SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Contains(scope, StringComparer.Ordinal);
    }

    /// <summary>
    /// Segunda barrera, dentro de cada método: el llamante está en el mapa, el producto es el suyo y trae un actor.
    /// <paramref name="exigeEmpresa"/> en false para el servicio global de conteos.
    /// </summary>
    /// <returns>El id del actor de la metadata.</returns>
    /// <exception cref="RpcException"><c>PERMISSION_DENIED</c> si el llamante, el producto o el actor no sirven;
    /// <c>INVALID_ARGUMENT</c> sin empresa.</exception>
    public static Guid VerificarLlamante(PlatformServiceCaller caller, string? producto, Metadata cabeceras, bool exigeEmpresa = true)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(cabeceras);
        var suyo = ProductoDe(caller.ClientId)
            ?? throw new RpcException(new Status(StatusCode.PermissionDenied, "Este servicio solo lo atienden los productos con gestión de usuarios."));
        if (exigeEmpresa && caller.TenantId == Guid.Empty)
            throw new RpcException(new Status(StatusCode.InvalidArgument, $"Falta la metadata {PlatformServiceCallInterceptor.TenantMetadata} con el id de la empresa."));
        if (!string.Equals(producto, suyo, StringComparison.Ordinal))
            throw new RpcException(new Status(StatusCode.PermissionDenied, "El producto no corresponde al cliente de servicio."));
        if (!Guid.TryParse(cabeceras.GetValue(ActorMetadata), out var actor) || actor == Guid.Empty)
            throw new RpcException(new Status(StatusCode.PermissionDenied, $"Falta la metadata {ActorMetadata} con el usuario que actúa."));
        return actor;
    }

    private static void Configure(AuthorizationPolicyBuilder policy, bool conteos)
    {
        ArgumentNullException.ThrowIfNull(policy);
        policy.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
            .RequireAuthenticatedUser()
            .RequireAssertion(context => context.User.Identities.Any(i => EsTokenDeProducto(i, conteos)));
    }
}

/// <summary>
/// HU #14694 — el interceptor del SDK (<see cref="PlatformServiceCallInterceptor"/>) con el scope del cliente que llama:
/// <c>platform.usuarios.&lt;su producto&gt;[.conteos]</c>. Un cliente fuera del mapa se valida con el scope de Diagnóstico,
/// como antes, y lo rechaza el SDK (sin el scope) o la segunda barrera (<see cref="UsuariosProductoServicioAuthorization.VerificarLlamante"/>).
/// </summary>
internal sealed class UsuariosProductoServiceTokenInterceptor(bool conteos) : Interceptor
{
    public override Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request, ServerCallContext context, UnaryServerMethod<TRequest, TResponse> continuation)
    {
        ArgumentNullException.ThrowIfNull(context);
        var sub = context.GetHttpContext().User.FindFirst("sub")?.Value;
        var scope = UsuariosProductoServicioAuthorization.ScopeDe(sub, conteos)
            ?? (conteos ? DiagnosticoUsuariosServicioAuthorization.ScopeConteos : DiagnosticoUsuariosServicioAuthorization.Scope);
        return new PlatformServiceCallInterceptor(scope, ServiceAudiences.Tramites, requireTenant: !conteos)
            .UnaryServerHandler(request, context, continuation);
    }
}
