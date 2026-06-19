namespace Flit.Admin.Domain.Companies.Settings;

/// <summary>
/// Implementación por snapshot de <see cref="ITenantPolicyResolver"/> (AC4).
/// Un trámite in-flight usa la política capturada al crearse; las nuevas
/// radicaciones usan la política live. Sin estado: thread-safe / singleton.
/// </summary>
public sealed class SnapshotTenantPolicyResolver : ITenantPolicyResolver
{
    public TenantSettings ResolveForInFlightProcedure(TenantSettings snapshotAtCreation, TenantSettings liveSettings)
    {
        ArgumentNullException.ThrowIfNull(snapshotAtCreation);
        return snapshotAtCreation;
    }

    public TenantSettings ResolveForNewProcedure(TenantSettings liveSettings)
    {
        ArgumentNullException.ThrowIfNull(liveSettings);
        return liveSettings;
    }
}
