using Microsoft.AspNetCore.Authorization;

namespace Flit.Api.Authorization;

/// <summary>
/// Concede acceso si el JWT tiene rol SuperAdmin u <c>ot_admin</c>. HU #12859 (Feature #12848,
/// Épica #12751): a diferencia de <see cref="OtModuleAuthorizationHandler"/>, NO evalúa el claim
/// <c>entity_type</c> — un rol OT personalizado (p. ej. <c>gestor_tramites_ot</c>) sin uno de los
/// dos roles exactos recibe 403 incluso en Prelación documental, que es la única superficie del
/// módulo OT donde el Admin OT conserva acceso ("solo ordena") pero el resto de roles OT no.
/// </summary>
public sealed class OtAdminOrSuperAdminAuthorizationHandler
    : AuthorizationHandler<OtAdminOrSuperAdminRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        OtAdminOrSuperAdminRequirement requirement)
    {
        var roleCodes = context.User.Claims
            .Where(c => c.Type == AdminAuthorization.RoleClaimType)
            .Select(c => c.Value);

        if (roleCodes.Contains(AdminAuthorization.SuperAdminRole)
            || roleCodes.Contains(AdminAuthorization.OtAdminRole))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
