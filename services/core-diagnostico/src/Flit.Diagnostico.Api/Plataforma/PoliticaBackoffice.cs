using System.Security.Claims;
using Flit.Platform.Sdk.Authentication;
using Microsoft.AspNetCore.Authorization;

namespace Flit.Diagnostico.Api.Plataforma;

/// <summary>
/// Políticas de las rutas <c>/backoffice/*</c> (HU #13728, D-16, ADR-0073; ajustes 2026-11-11, punto 1). Son dos:
/// <list type="bullet">
/// <item><see cref="Nombre"/> (configuración): resumen, contrato, diagnósticos (listado, grilla, historial y detalle del
/// dictamen), intentos, fuentes obligatorias y la lista de empresas. Pasa un SuperAdmin de <b>cualquier</b> compañía, o un
/// usuario de la compañía FLIT (<c>tenant_type=FLIT</c>) con <see cref="Permisos.BackofficeVer"/>. Un usuario de otra
/// compañía con el permiso pero sin SuperAdmin recibe 403.</item>
/// <item><see cref="NombreOperacion"/> (operación FLIT): override del dictamen, versiones del dictamen y llenado manual. El
/// SuperAdmin pasa siempre, sin mirar <c>tenant_type</c> (decisión D23 de la HU #14563, 2026-11-13: el rol
/// <c>backoffice_diagnostico</c> se retiró y sus capacidades son del SuperAdmin); los demás, solo de la compañía FLIT
/// (<c>tenant_type=FLIT</c>) con <see cref="Permisos.BackofficeVer"/>. Un usuario de otra compañía con el permiso recibe 403.</item>
/// </list>
/// Un token de servicio nunca pasa ninguna: los servicios entran por gRPC. Cada endpoint puede exigir además su permiso.
/// </summary>
public static class PoliticaBackoffice
{
    /// <summary>Política de configuración del backoffice (todo el grupo <c>/backoffice</c>).</summary>
    public const string Nombre = "diagnostico.backoffice";

    /// <summary>Política de operación FLIT (override del dictamen y llenado manual), encima de <see cref="Nombre"/>.</summary>
    public const string NombreOperacion = "diagnostico.backoffice.operacion";

    /// <summary>Configuración: no es un servicio y es SuperAdmin (de cualquier compañía) o es de FLIT con el permiso.</summary>
    public static bool CumpleConfiguracion(ClaimsPrincipal user)
    {
        if (!EsUsuario(user))
            return false;
        return PlatformPrincipal.IsSuperAdmin(user) || (EsFlit(user) && TienePermisoVer(user));
    }

    /// <summary>Operación FLIT: no es un servicio y es SuperAdmin (de cualquier compañía, D23) o es de FLIT con el permiso.</summary>
    public static bool CumpleOperacion(ClaimsPrincipal user)
    {
        if (!EsUsuario(user))
            return false;
        return PlatformPrincipal.IsSuperAdmin(user) || (EsFlit(user) && TienePermisoVer(user));
    }

    /// <summary>La exigen el override del dictamen, las versiones del dictamen y el llenado manual, además de su permiso.</summary>
    public static TBuilder RequiereOperacionFlit<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(NombreOperacion);

    internal static AuthorizationBuilder AddPoliticaBackoffice(this AuthorizationBuilder builder) =>
        builder
            .AddPolicy(Nombre, p => p.RequireAuthenticatedUser().RequireAssertion(c => CumpleConfiguracion(c.User)))
            .AddPolicy(NombreOperacion, p => p.RequireAuthenticatedUser().RequireAssertion(c => CumpleOperacion(c.User)));

    private static bool EsUsuario(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return user.Identity?.IsAuthenticated == true && !PlatformPrincipal.IsService(user);
    }

    private static bool EsFlit(ClaimsPrincipal user) =>
        string.Equals(user.FindFirstValue("tenant_type"), UsuarioActual.TipoEmpresaFlit, StringComparison.OrdinalIgnoreCase);

    private static bool TienePermisoVer(ClaimsPrincipal user) =>
        user.FindAll("permissions").Any(c => string.Equals(c.Value, Permisos.BackofficeVer, StringComparison.OrdinalIgnoreCase));
}
