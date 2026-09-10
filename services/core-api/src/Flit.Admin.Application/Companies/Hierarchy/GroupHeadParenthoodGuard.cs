using Flit.Admin.Domain.Companies;

namespace Flit.Admin.Application.Companies.Hierarchy;

/// <summary>
/// Comprobaciones explícitas de cabeza de grupo y parentesco (HU #12345 AC2/AC3).
/// La resolución del tenant del JWT ocurre en la capa API antes de invocar estos métodos.
/// </summary>
public static class GroupHeadParenthoodGuard
{
    /// <summary>
    /// Verifica que <paramref name="callerTenantId"/> sea cabeza de grupo y coincida con
    /// <paramref name="headTenantId"/>.
    /// </summary>
    public static async Task<GuardResult> VerifyHeadCallerAsync(
        Guid callerTenantId,
        Guid headTenantId,
        ICompanyHierarchyRepository hierarchy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(hierarchy);

        if (callerTenantId != headTenantId)
        {
            return GuardResult.Forbidden();
        }

        var head = await hierarchy
            .GetHierarchyInfoAsync(headTenantId, cancellationToken)
            .ConfigureAwait(false);

        if (head is not { IsGroupParent: true, ParentTenantId: null })
        {
            return GuardResult.Forbidden();
        }

        return GuardResult.Allowed(head);
    }

    /// <summary>
    /// Verifica que <paramref name="childTenantId"/> sea hijo directo de <paramref name="headTenantId"/>.
    /// No filtra datos ajenos en la respuesta de error (AC2).
    /// </summary>
    public static async Task<GuardResult> VerifyChildOfHeadAsync(
        Guid headTenantId,
        Guid childTenantId,
        ICompanyHierarchyRepository hierarchy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(hierarchy);

        var child = await hierarchy
            .GetHierarchyInfoAsync(childTenantId, cancellationToken)
            .ConfigureAwait(false);

        if (child is null || child.ParentTenantId != headTenantId)
        {
            return GuardResult.Forbidden();
        }

        return GuardResult.Allowed(child);
    }

    public sealed class GuardResult
    {
        private GuardResult(bool isAllowed, CompanyHierarchyInfo? info)
        {
            IsAllowed = isAllowed;
            Info = info;
        }

        public bool IsAllowed { get; }

        public CompanyHierarchyInfo? Info { get; }

        public static GuardResult Allowed(CompanyHierarchyInfo info) => new(true, info);

        public static GuardResult Forbidden() => new(false, null);
    }
}
