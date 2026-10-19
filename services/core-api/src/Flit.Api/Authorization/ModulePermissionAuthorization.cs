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
    /// Apagado por defecto: las rutas con <see cref="ModulePermissionRequirement"/> se comportan como antes (solo su policy
    /// de rol). Encendido, exigen el slug en el claim <c>permissions</c>. Encender al menos 12 h después del despliegue que
    /// asigna los slugs (los permisos viajan en el JWT), igual que <see cref="OtPermissionEnforcementOptions"/>.
    /// </summary>
    public bool Enabled { get; set; }
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
