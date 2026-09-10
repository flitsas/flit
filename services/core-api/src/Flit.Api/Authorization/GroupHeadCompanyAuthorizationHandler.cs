using Flit.Admin.Domain.Companies;
using Microsoft.AspNetCore.Authorization;

namespace Flit.Api.Authorization;

/// <summary>
/// HU #12345 AC4/AC5 — concede acceso si el JWT tiene AdminCompany y el tenant del caller es
/// cabeza de grupo (<c>is_group_parent = true</c>, leído de BD). No modifica
/// <see cref="AdminCompanyAuthorizationHandler"/> ni <see cref="CompanyOwnTenantFilter"/>.
/// </summary>
public sealed class GroupHeadCompanyAuthorizationHandler(
    ICompanyHierarchyRepository hierarchy)
    : AuthorizationHandler<GroupHeadCompanyRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        GroupHeadCompanyRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(hierarchy);

        var hasAdminCompany = context.User.Claims
            .Where(c => c.Type == AdminAuthorization.RoleClaimType)
            .Any(c => string.Equals(c.Value, AdminAuthorization.AdminCompanyRole, StringComparison.Ordinal));

        if (!hasAdminCompany)
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

        if (info is { IsGroupParent: true, ParentTenantId: null })
        {
            context.Succeed(requirement);
        }
    }
}
