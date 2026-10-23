using Microsoft.AspNetCore.Authorization;

namespace Flit.Api.Authorization;

/// <summary>
/// Concede acceso al módulo OT si el JWT tiene rol SuperAdmin u <c>ot_admin</c>, o si el
/// tenant autenticado es un organismo de tránsito (<c>entity_type = TRANSIT_OFFICE</c>).
/// El tipo de entidad es la señal que decide: un rol con TargetEntityType TRANSIT_OFFICE solo
/// puede asignarse en tenants OT, así que todo usuario de un organismo pertenece al módulo.
/// El alcance por tenant lo siguen imponiendo los endpoints (resuelven la OT por el JWT).
/// </summary>
public sealed class OtModuleAuthorizationHandler
    : AuthorizationHandler<OtModuleRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        OtModuleRequirement requirement)
    {
        // Multi-rol (HU #10506): un claim "role" por cada rol activo — se evalúan todos.
        var roleCodes = context.User.Claims
            .Where(c => c.Type == AdminAuthorization.RoleClaimType)
            .Select(c => c.Value);

        var isTransitOfficeTenant = context.User.Claims.Any(c =>
            c.Type == AdminAuthorization.EntityTypeClaimType
            && string.Equals(c.Value, AdminAuthorization.TransitOfficeEntityType, StringComparison.OrdinalIgnoreCase));

        if (roleCodes.Contains(AdminAuthorization.SuperAdminRole)
            || roleCodes.Contains(AdminAuthorization.OtAdminRole)
            || isTransitOfficeTenant)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
