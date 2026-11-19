using System.Security.Claims;
using Flit.Api.Identity;
using Flit.Platform.Sdk;
using Flit.Platform.Sdk.Grpc;
using Grpc.Core;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;

namespace Flit.Api.Authorization;

/// <summary>
/// Quién puede llamar a la fachada gRPC del portal de persona natural (HU #14033, Épica #14021, ADR-0075): únicamente
/// el cliente de servicio <c>svc-personanatural</c> de Identidad, con <c>aud=tramites</c> y el scope
/// <c>platform.tramites.persona_natural</c>. Otro cliente de servicio, aunque tenga un scope de trámites, no entra.
/// <para>
/// A diferencia de ICT, que acepta el <c>tenant_id</c> que le mande cualquier token válido de ese servicio, aquí el
/// token solo vale para UNA empresa: la de <see cref="PlatformTenants.PersonaNatural"/> (<see cref="VerificarEmpresa"/>).
/// Un token robado del portal no sirve para crear ni leer trámites de otra compañía.
/// </para>
/// </summary>
internal static class PersonaNaturalServicioAuthorization
{
    public const string Scope = "platform.tramites.persona_natural";

    /// <summary>El <c>sub</c> del cliente de servicio (<c>svc-</c> + código del servicio).</summary>
    public const string ClientId = OidcDefaults.ServiceClientPrefix + "personanatural";

    /// <summary>Código de error estable cuando la empresa de la llamada no es la del portal.</summary>
    public const string InvalidTenant = "invalid_tenant";

    public static void Configure(AuthorizationPolicyBuilder policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        policy.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
            .RequireAuthenticatedUser()
            .RequireAssertion(context => context.User.Identities.Any(EsTokenDelPortal));
    }

    internal static bool EsTokenDelPortal(ClaimsIdentity identity) =>
        identity.IsAuthenticated
        && string.Equals(identity.FindFirst("sub")?.Value, ClientId, StringComparison.Ordinal)
        && identity.HasClaim("aud", ServiceAudiences.Tramites)
        && identity.FindAll("scope").SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Contains(Scope, StringComparer.Ordinal);

    /// <summary>
    /// Candado de empresa: el llamante tiene que ser el portal y la empresa de la llamada, exactamente la de Persona Natural.
    /// Se invoca al inicio de cada método de la fachada y del consumidor, con el llamante que dejó el interceptor del SDK.
    /// </summary>
    /// <exception cref="RpcException"><c>invalid_tenant</c> (<c>INVALID_ARGUMENT</c>) si la empresa es otra; <c>PERMISSION_DENIED</c> si el llamante no es el portal.</exception>
    public static void VerificarEmpresa(PlatformServiceCaller caller)
    {
        ArgumentNullException.ThrowIfNull(caller);
        if (!string.Equals(caller.ClientId, ClientId, StringComparison.Ordinal))
            throw new RpcException(new Status(StatusCode.PermissionDenied, "Este servicio solo lo atiende el portal de persona natural."));
        if (caller.TenantId != PlatformTenants.PersonaNatural)
            throw PlatformRpcErrors.ToRpcException(InvalidTenant, "La empresa de la llamada no es la de Persona Natural.", StatusCode.InvalidArgument);
    }
}
