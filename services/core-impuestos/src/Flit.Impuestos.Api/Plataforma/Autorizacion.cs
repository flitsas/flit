using System.Security.Claims;
using Flit.Platform.Sdk.Authentication;
using Flit.Platform.Sdk.Tenancy;
using Microsoft.AspNetCore.Authorization;

namespace Flit.Impuestos.Api.Plataforma;

/// <summary>Permiso del manifiesto (<c>impuestos.&lt;modulo&gt;.&lt;accion&gt;</c>) que exige un endpoint.</summary>
public sealed record PermisoRequerido(string Slug) : IAuthorizationRequirement;

/// <summary>
/// Misma regla que la plataforma: el Super Admin pasa siempre; los demás, si el claim <c>permissions</c> trae el slug. Los permisos
/// de <see cref="Permisos.SoloSuperAdmin"/> exigen además <c>tenant_type=FLIT</c> si alguien arma un rol a mano (plan §5.2). Un
/// token de servicio nunca pasa por aquí: los servicios entran por gRPC.
/// </summary>
internal sealed class PermisoRequeridoHandler : AuthorizationHandler<PermisoRequerido>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermisoRequerido requirement)
    {
        var user = context.User;
        if (PlatformPrincipal.IsService(user))
            return Task.CompletedTask;

        if (PlatformPrincipal.IsSuperAdmin(user))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        var tiene = user.FindAll("permissions").Any(c => string.Equals(c.Value, requirement.Slug, StringComparison.OrdinalIgnoreCase));
        var soloFlit = Permisos.SoloSuperAdmin.Contains(requirement.Slug, StringComparer.OrdinalIgnoreCase);
        if (tiene && (!soloFlit || UsuarioActual.EsDeFlit(user)))
            context.Succeed(requirement);
        return Task.CompletedTask;
    }
}

public static class AutorizacionExtensions
{
    /// <summary>
    /// Usuario del producto: autenticado, nunca un token de servicio, y con al menos un permiso de Impuestos (o Super Admin). Un
    /// rol de otro producto, sin permisos <c>impuestos.*</c>, no abre la sesión (F6-CF2).
    /// </summary>
    public const string PoliticaUsuario = "impuestos.usuario";

    public static IServiceCollection AddAutorizacionImpuestos(this IServiceCollection services)
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

    /// <summary>Exige el permiso del manifiesto (o ser Super Admin). Un slug ajeno al manifiesto de Impuestos falla al mapear.</summary>
    public static TBuilder RequirePermiso<TBuilder>(this TBuilder builder, string slug)
        where TBuilder : IEndpointConventionBuilder
    {
        if (!EsDelManifiesto(slug))
            throw new ArgumentException($"'{slug}' no es un permiso del manifiesto de Impuestos (impuestos.*).", nameof(slug));
        return builder.RequireAuthorization(p => p.RequireAuthenticatedUser().AddRequirements(new PermisoRequerido(slug)));
    }

    private static bool EsDelManifiesto(string? slug) =>
        slug is not null && Permisos.Todos.Contains(slug, StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Quién hace la petición, leído del token de producto (<c>aud=impuestos</c>). Una sesión = una compañía: la empresa sale del claim
/// <c>tenant_id</c> (vía el SDK, que para un usuario ignora la cabecera) y nunca de un parámetro.
/// </summary>
public sealed class UsuarioActual(IHttpContextAccessor http, IPlatformTenantAccessor tenants)
{
    /// <summary>Valor de <c>tenant_type</c> de la compañía FLIT.</summary>
    public const string TipoEmpresaFlit = "FLIT";

    private ClaimsPrincipal User => http.HttpContext?.User ?? new ClaimsPrincipal();

    public Guid Id => Guid.TryParse(User.FindFirstValue("sub"), out var id) ? id : Guid.Empty;

    public string Email => User.FindFirstValue("email") ?? string.Empty;

    public bool EsSuperAdmin => PlatformPrincipal.IsSuperAdmin(User);

    public Guid? EmpresaId => tenants.Current.TenantId;

    public string? EmpresaNombre => User.FindFirstValue("company_name") ?? User.FindFirstValue("tenant_name");

    /// <summary>Códigos de rol del token (un claim <c>role_code</c> por rol).</summary>
    public IReadOnlyList<string> Roles => User.FindAll("role_code").Select(c => c.Value).Distinct(StringComparer.Ordinal).ToList();

    public bool Tiene(string slug) =>
        EsSuperAdmin || User.FindAll("permissions").Any(c => string.Equals(c.Value, slug, StringComparison.OrdinalIgnoreCase));

    /// <summary>Permisos de Impuestos que tiene (el token de producto trae también los de plataforma).</summary>
    public IReadOnlyList<string> PermisosDelProducto => Permisos.Todos.Where(Tiene).ToList();

    /// <summary>Compañía del token; 403 <c>SIN_EMPRESA</c> si no tiene.</summary>
    public Guid EmpresaPropia() =>
        EmpresaId ?? throw ReglaException.Prohibido("SIN_EMPRESA", "Tu usuario no tiene una compañía asociada.");

    /// <summary>La compañía del token es FLIT (<c>tenant_type=FLIT</c>).</summary>
    internal static bool EsDeFlit(ClaimsPrincipal user) =>
        string.Equals(user.FindFirstValue("tenant_type"), TipoEmpresaFlit, StringComparison.OrdinalIgnoreCase);
}
