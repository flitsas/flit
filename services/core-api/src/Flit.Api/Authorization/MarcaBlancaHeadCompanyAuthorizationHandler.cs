using Flit.Admin.Domain.Companies;
using Flit.Admin.Domain.Companies.Create;
using Microsoft.AspNetCore.Authorization;

namespace Flit.Api.Authorization;

/// <summary>
/// HU #12429 (endurecimiento del hecho 88) — concede acceso si el JWT tiene AdminCompany, el tenant
/// del caller es cabeza de grupo (<c>is_group_parent = true</c>) Y su clase es
/// <see cref="HeadTenantTypes.MarcaBlanca"/>. Se creó como policy separada de
/// <see cref="GroupHeadCompanyAuthorizationHandler"/> (que sigue sirviendo a las rutas de jerarquía de
/// #12345, donde una Concesión con hijas SÍ debe operar) para no romper CF de #12345 al restringir
/// solo las rutas de marca/dominio (<c>/company/branding*</c>, <c>/company/domain*</c>).
/// </summary>
public sealed class MarcaBlancaHeadCompanyAuthorizationHandler(
    ICompanyHierarchyRepository hierarchy)
    : AuthorizationHandler<MarcaBlancaHeadCompanyRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        MarcaBlancaHeadCompanyRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(hierarchy);

        var roleCodes = context.User.Claims
            .Where(c => c.Type == AdminAuthorization.RoleClaimType)
            .Select(c => c.Value)
            .ToList();

        if (roleCodes.Contains(AdminAuthorization.SuperAdminRole, StringComparer.Ordinal))
        {
            context.Succeed(requirement);
            return;
        }

        if (!roleCodes.Contains(AdminAuthorization.AdminCompanyRole, StringComparer.Ordinal))
        {
            return;
        }

        if (!RequestTenantResolver.TryResolveTenantId(context.User, out var tenantId))
        {
            return;
        }

        var info = await hierarchy
            .GetHierarchyInfoAsync(tenantId, CancellationToken.None)
            .ConfigureAwait(false);

        if (info is { IsGroupParent: true, ParentTenantId: null }
            && string.Equals(info.TenantType, HeadTenantTypes.MarcaBlanca, StringComparison.Ordinal))
        {
            context.Succeed(requirement);
        }
    }
}
