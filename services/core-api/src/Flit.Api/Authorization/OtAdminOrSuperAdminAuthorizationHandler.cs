using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace Flit.Api.Authorization;

/// <summary>
/// Concede acceso si el JWT tiene rol SuperAdmin u <c>ot_admin</c>. HU #12859 (Feature #12848,
/// Épica #12751): a diferencia de <see cref="OtModuleAuthorizationHandler"/>, NO evalúa el claim
/// <c>entity_type</c> — un rol OT personalizado (p. ej. <c>gestor_tramites_ot</c>) sin uno de los
/// dos roles exactos recibe 403 incluso en Prelación documental, que es la única superficie del
/// módulo OT donde el Admin OT conserva acceso ("solo ordena") pero el resto de roles OT no.
/// <para>
/// HU #13639 (Anexo A §1.4): con <c>Security:OtPermissionEnforcement:Enabled=true</c>, en las rutas de
/// <see cref="OtPermissionMap"/> (mandatarios, baúl de firmas, prelación documental) también pasa cualquier usuario del
/// organismo: el que decide es su slug OT, que exige <see cref="OtPermissionEnforcementMiddleware"/>. Así el Admin OT
/// puede delegar esas pantallas por usuario. Con el flag apagado la regla no cambia.
/// </para>
/// </summary>
public sealed class OtAdminOrSuperAdminAuthorizationHandler(IOptionsMonitor<OtPermissionEnforcementOptions>? otPermissions = null)
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
            || roleCodes.Contains(AdminAuthorization.OtAdminRole)
            || DelegaEnElSlugOt(context))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }

    private bool DelegaEnElSlugOt(AuthorizationHandlerContext context)
    {
        if (otPermissions?.CurrentValue.Enabled != true
            || context.Resource is not HttpContext http
            || http.GetEndpoint() is not RouteEndpoint endpoint
            || endpoint.Metadata.GetMetadata<IHttpMethodMetadata>() is not { } methods)
        {
            return false;
        }

        var esUsuarioDelOrganismo = context.User.Claims.Any(c =>
            c.Type == AdminAuthorization.EntityTypeClaimType
            && string.Equals(c.Value, AdminAuthorization.TransitOfficeEntityType, StringComparison.OrdinalIgnoreCase));

        return esUsuarioDelOrganismo
            && methods.HttpMethods.Any(m => OtPermissionMap.BySignature.ContainsKey(OtPermissionMap.Signature(m, endpoint.RoutePattern.RawText)));
    }
}
