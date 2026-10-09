using System.Security.Claims;
using Flit.Platform.Sdk.Authentication;
using Flit.Platform.Sdk.Tenancy;
using Microsoft.AspNetCore.Authorization;

namespace Flit.Comparendos.Api.Plataforma;

/// <summary>Permiso del manifiesto (<c>comparendos.&lt;modulo&gt;.&lt;accion&gt;</c>) que exige un endpoint.</summary>
public sealed record PermisoRequerido(string Slug) : IAuthorizationRequirement;

/// <summary>
/// Misma regla que core-api (HU #10165): el SuperAdmin pasa siempre (cualquier claim <c>role_code</c> o <c>role</c>
/// igual a SuperAdmin, el token trae uno por rol); los demás, si el claim <c>permissions</c> trae el slug.
/// Un token de servicio nunca pasa por aquí: los servicios entran por gRPC.
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

/// <summary>Solo el SuperAdmin de FLIT (catálogos globales, corridas, calendario, log de migración).</summary>
public sealed record SoloSuperAdmin : IAuthorizationRequirement;

internal sealed class SoloSuperAdminHandler : AuthorizationHandler<SoloSuperAdmin>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, SoloSuperAdmin requirement)
    {
        if (PlatformPrincipal.IsSuperAdmin(context.User))
            context.Succeed(requirement);
        return Task.CompletedTask;
    }
}

public static class AutorizacionExtensions
{
    public const string PoliticaUsuario = "comparendos.usuario";
    public const string PoliticaSuperAdmin = "comparendos.superadmin";

    public static IServiceCollection AddAutorizacionComparendos(this IServiceCollection services)
    {
        services.AddSingleton<IAuthorizationHandler, PermisoRequeridoHandler>();
        services.AddSingleton<IAuthorizationHandler, SoloSuperAdminHandler>();
        services.AddHttpContextAccessor();
        services.AddScoped<UsuarioActual>();
        services.AddAuthorizationBuilder()
            .AddPolicy(PoliticaUsuario, p => p.RequireAuthenticatedUser().RequireAssertion(c => !PlatformPrincipal.IsService(c.User)))
            .AddPolicy(PoliticaSuperAdmin, p => p.RequireAuthenticatedUser().AddRequirements(new SoloSuperAdmin()));
        return services;
    }

    /// <summary>Exige el permiso del manifiesto (o ser SuperAdmin).</summary>
    public static TBuilder RequirePermiso<TBuilder>(this TBuilder builder, string slug)
        where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(p => p.RequireAuthenticatedUser().AddRequirements(new PermisoRequerido(slug)));
}

/// <summary>
/// Quién hace la petición, leído del token de producto (aud=comparendos). La empresa sale del claim <c>tenant_id</c>
/// y un usuario nunca la elige; el SuperAdmin, que ve todas, puede acotar a una con <c>?empresa=</c> en los endpoints
/// que lo permiten (ADO #12982: filtro maestro por gestora).
/// </summary>
public sealed class UsuarioActual(IHttpContextAccessor http, IPlatformTenantAccessor tenants)
{
    private ClaimsPrincipal User => http.HttpContext?.User ?? new ClaimsPrincipal();

    public PlatformTenantContext Tenant => tenants.Current;

    public Guid Id => Guid.TryParse(User.FindFirstValue("sub"), out var id) ? id : Guid.Empty;

    public string Email => User.FindFirstValue("email") ?? string.Empty;

    /// <summary>Nombre que se muestra en el timeline y en la auditoría: el correo (el token no trae nombre).</summary>
    public string Nombre => Email.Length > 0 ? Email : Id.ToString();

    public bool EsSuperAdmin => PlatformPrincipal.IsSuperAdmin(User);

    public Guid? EmpresaId => Tenant.TenantId;

    public string? EmpresaNombre => User.FindFirstValue("company_name") ?? User.FindFirstValue("tenant_name");

    public bool Tiene(string slug) =>
        EsSuperAdmin || User.FindAll("permissions").Any(c => string.Equals(c.Value, slug, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Empresa sobre la que actúa una ESCRITURA: la del token; el SuperAdmin debe decir cuál. Null = petición inválida.
    /// </summary>
    public Guid? EmpresaParaEscribir(Guid? pedida) =>
        EsSuperAdmin ? pedida : EmpresaId is { } propia && (pedida is null || pedida == propia) ? propia : null;

    /// <summary>Empresa para LEER: un usuario, la suya; el SuperAdmin, la pedida o todas (null).</summary>
    public (bool Valida, Guid? Empresa) EmpresaParaLeer(Guid? pedida) =>
        EsSuperAdmin ? (true, pedida)
        : EmpresaId is { } propia && (pedida is null || pedida == propia) ? (true, propia)
        : (false, null);
}
