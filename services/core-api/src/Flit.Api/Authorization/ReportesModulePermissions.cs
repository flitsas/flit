using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Flit.Api.Authorization;

/// <summary>Slugs del módulo Reportes que exige la API (HU #13659, Feature #13105 F-E, Épica #12753).</summary>
public static class ReportesPermissions
{
    public const string Read = "reportes.read";
    public const string Resumen = "reportes.resumen.read";
    public const string Operacion = "reportes.operacion.read";
    public const string Ot = "reportes.ot.read";
    public const string Uso = "reportes.uso.read";
    public const string Productividad = "reportes.productividad.read";
    public const string Consultas = "reportes.consultas.read";
    public const string Programacion = "reportes.programacion.manage";

    /// <summary>
    /// Rutas transversales a las pestañas (detalle, exportaciones, panorama en vivo): basta el slug base del módulo o el de
    /// cualquier pestaña, porque hoy las usa quien vea cualquiera de ellas.
    /// </summary>
    public static readonly string[] CualquierPestana = [Read, Resumen, Operacion, Ot, Uso, Productividad, Consultas];
}

/// <summary>
/// Variante «alguno de»: el flag apagado concede; encendido, exige al menos uno de los slugs (bypass SuperAdmin). Es a la vez
/// requisito y handler (ASP.NET Core invoca los requisitos que implementan <see cref="IAuthorizationHandler"/>), así no
/// necesita registro en DI; lee el flag desde el <see cref="HttpContext"/> del recurso.
/// </summary>
public sealed class ModuleAnyPermissionRequirement(params string[] slugs)
    : AuthorizationHandler<ModuleAnyPermissionRequirement>, IAuthorizationRequirement
{
    public IReadOnlyList<string> Slugs { get; } = slugs;

    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, ModuleAnyPermissionRequirement requirement)
    {
        var enabled = (context.Resource as HttpContext)?.RequestServices
            .GetRequiredService<IOptionsMonitor<ModulePermissionEnforcementOptions>>().CurrentValue.Enabled ?? true;
        if (!enabled || requirement.Slugs.Any(s => PermissionAuthorizationHandler.Grants(context.User, s)))
            context.Succeed(requirement);
        return Task.CompletedTask;
    }
}

/// <summary><c>.RequireAnyModulePermission(slugs)</c>: como <c>RequireModulePermission</c> pero basta uno de los slugs.</summary>
public static class RequireAnyModulePermissionExtensions
{
    public static RouteHandlerBuilder RequireAnyModulePermission(this RouteHandlerBuilder builder, params string[] slugs)
        => builder.RequireAuthorization(policy => policy.RequireAuthenticatedUser().AddRequirements(new ModuleAnyPermissionRequirement(slugs)));
}
