using System.Security.Claims;
using Flit.Platform.Sdk.Authentication;
using Flit.Platform.Sdk.Tenancy;
using Microsoft.AspNetCore.Authorization;

namespace Flit.Diagnostico.Api.Plataforma;

/// <summary>Permiso del manifiesto (<c>diagnostico.&lt;modulo&gt;.&lt;accion&gt;</c>) que exige un endpoint.</summary>
public sealed record PermisoRequerido(string Slug) : IAuthorizationRequirement;

/// <summary>
/// Misma regla que core-api y Comparendos (HU #13724): el SuperAdmin pasa siempre; los demás, si el claim
/// <c>permissions</c> trae el slug. Un token de servicio nunca pasa por aquí: los servicios entran por gRPC.
/// </summary>
internal sealed class PermisoRequeridoHandler : AuthorizationHandler<PermisoRequerido>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermisoRequerido requirement)
    {
        var user = context.User;
        if (PlatformPrincipal.IsService(user))
            return Task.CompletedTask;

        if (PlatformPrincipal.IsSuperAdmin(user)
            || user.FindAll("permissions").Any(c => string.Equals(c.Value, requirement.Slug, StringComparison.OrdinalIgnoreCase)))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

public static class AutorizacionExtensions
{
    /// <summary>
    /// Usuario del producto: autenticado, nunca un token de servicio, y con al menos un permiso de Diagnóstico (o
    /// SuperAdmin). Un rol de otro producto, sin permisos <c>diagnostico.*</c>, no abre la sesión (HU #13726, R-12).
    /// </summary>
    public const string PoliticaUsuario = "diagnostico.usuario";

    public static IServiceCollection AddAutorizacionDiagnostico(this IServiceCollection services)
    {
        services.AddSingleton<IAuthorizationHandler, PermisoRequeridoHandler>();
        services.AddHttpContextAccessor();
        services.AddScoped<UsuarioActual>();
        services.AddAuthorizationBuilder()
            .AddPolicy(PoliticaUsuario, p => p.RequireAuthenticatedUser().RequireAssertion(c =>
                !PlatformPrincipal.IsService(c.User)
                && (PlatformPrincipal.IsSuperAdmin(c.User) || c.User.FindAll("permissions").Any(claim => EsDelManifiesto(claim.Value)))))
            .AddPoliticaBackoffice();
        return services;
    }

    /// <summary>
    /// Exige el permiso del manifiesto (o ser SuperAdmin). Solo acepta slugs de <see cref="Permisos.Todos"/>: una ruta de
    /// Diagnóstico no puede quedar protegida con un permiso de otro producto (HU #13726); otro slug falla al mapear.
    /// </summary>
    public static TBuilder RequirePermiso<TBuilder>(this TBuilder builder, string slug)
        where TBuilder : IEndpointConventionBuilder
    {
        if (!EsDelManifiesto(slug))
            throw new ArgumentException($"'{slug}' no es un permiso del manifiesto de Diagnóstico (diagnostico.*).", nameof(slug));
        return builder.RequireAuthorization(p => p.RequireAuthenticatedUser().AddRequirements(new PermisoRequerido(slug)));
    }

    private static bool EsDelManifiesto(string? slug) =>
        slug is not null && Permisos.Todos.Contains(slug, StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Quién hace la petición, leído del token de producto (aud=diagnostico). Una sesión = una compañía (D-03): la empresa
/// sale del claim <c>tenant_id</c> (vía el SDK, que para un usuario ignora la cabecera) y nunca de un parámetro.
/// </summary>
public sealed class UsuarioActual(IHttpContextAccessor http, IPlatformTenantAccessor tenants)
{
    /// <summary>Valor de <c>tenant_type</c> de la compañía FLIT (<c>ck_tenants_tenant_type</c>, DDL 109).</summary>
    public const string TipoEmpresaFlit = "FLIT";

    private ClaimsPrincipal User => http.HttpContext?.User ?? new ClaimsPrincipal();

    public Guid Id => Guid.TryParse(User.FindFirstValue("sub"), out var id) ? id : Guid.Empty;

    public string Email => User.FindFirstValue("email") ?? string.Empty;

    public bool EsSuperAdmin => PlatformPrincipal.IsSuperAdmin(User);

    public Guid? EmpresaId => tenants.Current.TenantId;

    public string? EmpresaNombre => User.FindFirstValue("company_name") ?? User.FindFirstValue("tenant_name");

    public string? TipoEmpresa => User.FindFirstValue("tenant_type");

    /// <summary>La compañía del token es FLIT (requisito del backoffice, D-16).</summary>
    public bool EsEmpresaFlit => string.Equals(TipoEmpresa, TipoEmpresaFlit, StringComparison.OrdinalIgnoreCase);

    /// <summary>Códigos de rol del token (un claim <c>role_code</c> por rol).</summary>
    public IReadOnlyList<string> Roles => User.FindAll("role_code").Select(c => c.Value).Distinct(StringComparer.Ordinal).ToList();

    public bool Tiene(string slug) =>
        EsSuperAdmin || User.FindAll("permissions").Any(c => string.Equals(c.Value, slug, StringComparison.OrdinalIgnoreCase));

    /// <summary>Permisos de Diagnóstico que tiene (el token de producto trae también los de plataforma).</summary>
    public IReadOnlyList<string> PermisosDelProducto => Permisos.Todos.Where(Tiene).ToList();

    /// <summary>Compañía del token; 403 <c>SIN_EMPRESA</c> si no tiene.</summary>
    public Guid EmpresaPropia() =>
        EmpresaId ?? throw ReglaException.Prohibido("SIN_EMPRESA", "Tu usuario no tiene una empresa asignada.");
}
