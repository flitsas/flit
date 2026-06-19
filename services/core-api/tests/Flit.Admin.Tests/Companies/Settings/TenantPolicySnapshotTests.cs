using Flit.Admin.Domain.Companies.Settings;
using FluentAssertions;
using Xunit;

namespace Flit.Admin.Tests.Companies.Settings;

/// <summary>
/// AC4 (RF03) — test de dominio que documenta el contrato de snapshot de política:
/// un trámite in-flight conserva la regla vigente al momento de su creación; las
/// nuevas radicaciones usan la configuración live. La entidad EF de
/// <c>tramites.procedure_instances</c> no existe aún en esta HU, por lo que la
/// regla se valida sobre <see cref="ITenantPolicyResolver"/>.
/// </summary>
public sealed class TenantPolicySnapshotTests
{
    [Fact]
    public void AC4_InFlightProcedure_KeepsInitialRule_WhenLivePolicyChanges()
    {
        var resolver = new SnapshotTenantPolicyResolver();
        var tenantId = Guid.NewGuid();

        // Snapshot al crear el trámite: switchMatricula activo (true).
        var snapshot = WithInitialRegistration(tenantId, allowInitialRegistration: true);

        // El SuperAdmin desactiva el switch (false) → política live.
        var live = WithInitialRegistration(tenantId, allowInitialRegistration: false);

        var effective = resolver.ResolveForInFlightProcedure(snapshot, live);

        // El trámite en curso sigue con la regla inicial (true), ignorando el cambio live.
        effective.AllowInitialRegistration.Should().BeTrue();
        effective.Should().BeSameAs(snapshot);
    }

    [Fact]
    public void AC4_NewProcedure_UsesLivePolicy()
    {
        var resolver = new SnapshotTenantPolicyResolver();
        var tenantId = Guid.NewGuid();

        var live = WithInitialRegistration(tenantId, allowInitialRegistration: false);

        var effective = resolver.ResolveForNewProcedure(live);

        // Las nuevas radicaciones usan la configuración vigente (false).
        effective.AllowInitialRegistration.Should().BeFalse();
        effective.Should().BeSameAs(live);
    }

    private static TenantSettings WithInitialRegistration(Guid tenantId, bool allowInitialRegistration) => new()
    {
        TenantId = tenantId,
        AllowInitialRegistration = allowInitialRegistration,
        AllowMiscNewVehicles = true,
        OnlyOwnVehicles = false,
        SignatureVaultEnabled = false,
        NotificationChannel = NotificationChannel.FlitSmtp,
        NotificationTarget = NotificationTarget.Radicador,
        PaymentMethods = [],
    };
}
