using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace Flit.Api.Authorization;

/// <summary>Opciones de <c>Security:ModulePermissionEnforcement</c> (Feature #13105, Épica #12753).</summary>
public sealed class ModulePermissionEnforcementOptions
{
    public const string Section = "Security:ModulePermissionEnforcement";

    /// <summary>
    /// Encendido por defecto (decisión del usuario, 2026-10-21): las rutas con <see cref="ModulePermissionRequirement"/>
    /// exigen el slug en el claim <c>permissions</c>. Apagarlo es el freno de emergencia: vuelven a exigir solo su policy de
    /// rol. Los permisos viajan en el JWT: en el primer despliegue hay que cerrar las sesiones abiertas para que todos
    /// reciban los slugs nuevos.
    /// </summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>Slug que un módulo instrumentado por el Feature #13105 exige solo con el flag encendido.</summary>
public sealed class ModulePermissionRequirement(string slug) : IAuthorizationRequirement
{
    public string Slug { get; } = slug;
}

/// <summary>Flag apagado → concede; encendido → misma regla que <see cref="PermissionAuthorizationHandler"/> (bypass SuperAdmin).</summary>
public sealed class ModulePermissionAuthorizationHandler(IOptionsMonitor<ModulePermissionEnforcementOptions> options)
    : AuthorizationHandler<ModulePermissionRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, ModulePermissionRequirement requirement)
    {
        if (!options.CurrentValue.Enabled || PermissionAuthorizationHandler.Grants(context.User, requirement.Slug))
            context.Succeed(requirement);
        return Task.CompletedTask;
    }
}

/// <summary>
/// <c>.RequireModulePermission(slug)</c>: como <see cref="RequirePermissionExtensions"/>, en AND con la policy existente,
/// pero detrás de <see cref="ModulePermissionEnforcementOptions"/>. Cuenta como punto de enforcement para el marcador
/// <c>enforced</c> (HU #13641).
/// </summary>
public static class RequireModulePermissionExtensions
{
    public static RouteHandlerBuilder RequireModulePermission(this RouteHandlerBuilder builder, string slug)
        => builder.RequireAuthorization(policy => policy.RequireAuthenticatedUser().AddRequirements(new ModulePermissionRequirement(slug)));

    public static RouteGroupBuilder RequireModulePermission(this RouteGroupBuilder builder, string slug)
        => builder.RequireAuthorization(policy => policy.RequireAuthenticatedUser().AddRequirements(new ModulePermissionRequirement(slug)));
}
