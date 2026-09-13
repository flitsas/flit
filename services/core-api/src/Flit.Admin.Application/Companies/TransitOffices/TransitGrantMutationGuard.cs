using Flit.Admin.Domain.Companies;
using Flit.Admin.Domain.Companies.Create;
using Flit.Admin.Domain.Companies.TransitOffices;

namespace Flit.Admin.Application.Companies.TransitOffices;

/// <summary>
/// Reglas de mutabilidad de habilitaciones OT (HU #12346 AC2/AC3/AC7).
/// </summary>
public static class TransitGrantMutationGuard
{
    public const string GrantInmutableMessage =
        "Esta habilitación está gobernada por la plataforma y no puede modificarse desde la compañía.";

    public const string CabezaAsignadaPorSuperAdminMessage =
        "La lista de organismos de tránsito de una Concesión la asigna el administrador de la plataforma.";

    public const string HijoNoPuedeAgregarMessage =
        "Las compañías hijas de una Concesión no pueden agregar habilitaciones propias de organismos de tránsito.";

    public sealed record GuardResult(bool IsAllowed, string? Message)
    {
        public static GuardResult Allowed() => new(true, null);

        public static GuardResult Denied(string message) => new(false, message);
    }

    public static async Task<GuardResult> ValidateAddAsync(
        Guid tenantId,
        bool isSuperAdmin,
        ICompanyHierarchyRepository hierarchy,
        CancellationToken cancellationToken = default)
    {
        if (isSuperAdmin)
        {
            return GuardResult.Allowed();
        }

        var info = await hierarchy.GetHierarchyInfoAsync(tenantId, cancellationToken).ConfigureAwait(false);
        if (info is null)
        {
            return GuardResult.Denied("La compañía no existe.");
        }

        if (info.ParentTenantId is not null)
        {
            var head = await hierarchy
                .GetHierarchyInfoAsync(info.ParentTenantId.Value, cancellationToken)
                .ConfigureAwait(false);

            if (head is not null
                && string.Equals(head.TenantType, CompanyTenantTypes.Concesion, StringComparison.Ordinal))
            {
                return GuardResult.Denied(HijoNoPuedeAgregarMessage);
            }
        }

        if (info is { IsGroupParent: true, ParentTenantId: null }
            && string.Equals(info.TenantType, CompanyTenantTypes.Concesion, StringComparison.Ordinal))
        {
            return GuardResult.Denied(CabezaAsignadaPorSuperAdminMessage);
        }

        return GuardResult.Allowed();
    }

    public static async Task<GuardResult> ValidateRemoveAsync(
        Guid tenantId,
        bool isSuperAdmin,
        ICompanyHierarchyRepository hierarchy,
        string grantSource,
        CancellationToken cancellationToken = default)
    {
        if (isSuperAdmin)
        {
            return GuardResult.Allowed();
        }

        if (string.Equals(grantSource, TransitGrantSources.System, StringComparison.Ordinal))
        {
            return GuardResult.Denied(GrantInmutableMessage);
        }

        var info = await hierarchy.GetHierarchyInfoAsync(tenantId, cancellationToken).ConfigureAwait(false);
        if (info is null)
        {
            return GuardResult.Denied("La compañía no existe.");
        }

        if (info.ParentTenantId is not null)
        {
            return GuardResult.Denied(GrantInmutableMessage);
        }

        if (info is { IsGroupParent: true, ParentTenantId: null }
            && string.Equals(info.TenantType, CompanyTenantTypes.Concesion, StringComparison.Ordinal))
        {
            return GuardResult.Denied(CabezaAsignadaPorSuperAdminMessage);
        }

        return GuardResult.Allowed();
    }
}
