using Flit.Admin.Application.Companies.VehicleOwnership;
using Flit.Admin.Domain.Companies.Settings;
using Flit.Admin.Domain.Companies.VehicleOwnership;
using Flit.Admin.Domain.Companies.Whitelist;
using FluentAssertions;
using Xunit;

namespace Flit.Admin.Tests.Companies.Whitelist;

/// <summary>
/// Interceptor de propiedad vehicular (HU #10191) — AC1 (bloqueo con mensaje exacto),
/// AC2 (exención por whitelist) y AC3 (un trámite in-flight usa el snapshot de
/// política, no el valor live). Ejercita <see cref="VehicleOwnershipGuard"/> con
/// dobles en memoria del checker de propiedad y del repositorio de whitelist.
/// </summary>
public sealed class VehicleOwnershipGuardTests
{
    private static readonly Guid TenantId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    // ---------- AC1: bloqueo cuando el vehículo no es del tenant ----------

    [Fact]
    public async Task AC1_BlocksTransfer_WithExactMessage_WhenVehicleNotOwned()
    {
        var guard = new VehicleOwnershipGuard(
            new FakeOwnershipChecker(isOwned: false),
            new FakeWhitelistRepository());

        var context = new VehicleOwnershipCheckContext(
            TenantId, "ABC123", "operador@empresa.com",
            PolicyWithOnlyOwnVehicles(true));

        var result = await guard.ValidateTransferStartAsync(context, TestContext.Current.CancellationToken);

        result.IsAllowed.Should().BeFalse();
        result.Error.Should().Be("Vehículo no es propiedad de la compañía jurídica del tenant");
    }

    [Fact]
    public async Task AC1_AllowsTransfer_WhenVehicleIsOwned()
    {
        var guard = new VehicleOwnershipGuard(
            new FakeOwnershipChecker(isOwned: true),
            new FakeWhitelistRepository());

        var context = new VehicleOwnershipCheckContext(
            TenantId, "ABC123", "operador@empresa.com",
            PolicyWithOnlyOwnVehicles(true));

        var result = await guard.ValidateTransferStartAsync(context, TestContext.Current.CancellationToken);

        result.IsAllowed.Should().BeTrue();
        result.Error.Should().BeNull();
    }

    [Fact]
    public async Task AC1_AllowsTransfer_WhenPolicyDoesNotRestrict()
    {
        // only_own_vehicles=false: la regla no aplica aunque el vehículo no sea del tenant.
        var guard = new VehicleOwnershipGuard(
            new FakeOwnershipChecker(isOwned: false),
            new FakeWhitelistRepository());

        var context = new VehicleOwnershipCheckContext(
            TenantId, "ABC123", "operador@empresa.com",
            PolicyWithOnlyOwnVehicles(false));

        var result = await guard.ValidateTransferStartAsync(context, TestContext.Current.CancellationToken);

        result.IsAllowed.Should().BeTrue();
    }

    // ---------- AC2: exención por whitelist ----------

    [Fact]
    public async Task AC2_AllowsTransfer_WhenUserEmailIsWhitelisted()
    {
        var whitelist = new FakeWhitelistRepository("vip@empresa.com");
        var guard = new VehicleOwnershipGuard(
            new FakeOwnershipChecker(isOwned: false), // el vehículo NO es del tenant…
            whitelist);

        var context = new VehicleOwnershipCheckContext(
            TenantId, "ABC123", "VIP@empresa.com", // …pero el correo está exento (case-insensitive)
            PolicyWithOnlyOwnVehicles(true));

        var result = await guard.ValidateTransferStartAsync(context, TestContext.Current.CancellationToken);

        result.IsAllowed.Should().BeTrue();
        result.Error.Should().BeNull();
    }

    // ---------- AC3: el trámite in-flight usa el snapshot, no la política live ----------

    [Fact]
    public async Task AC3_InFlightProcedure_UsesSnapshotPolicy_NotLive()
    {
        var resolver = new SnapshotTenantPolicyResolver();

        // El trámite se inició con only_own_vehicles=false.
        var snapshot = PolicyWithOnlyOwnVehicles(false);
        // El SuperAdmin activó la regla durante el trámite (política live).
        var live = PolicyWithOnlyOwnVehicles(true);

        var effective = resolver.ResolveForInFlightProcedure(snapshot, live);

        var guard = new VehicleOwnershipGuard(
            new FakeOwnershipChecker(isOwned: false), // no es del tenant
            new FakeWhitelistRepository());           // ni está en whitelist

        var context = new VehicleOwnershipCheckContext(
            TenantId, "ABC123", "operador@empresa.com", effective);

        var result = await guard.ValidateTransferStartAsync(context, TestContext.Current.CancellationToken);

        // El trámite continúa porque su snapshot tenía la regla desactivada.
        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task AC3_NewProcedure_UsesLivePolicy_AndBlocks()
    {
        var resolver = new SnapshotTenantPolicyResolver();
        var live = PolicyWithOnlyOwnVehicles(true);

        var effective = resolver.ResolveForNewProcedure(live);

        var guard = new VehicleOwnershipGuard(
            new FakeOwnershipChecker(isOwned: false),
            new FakeWhitelistRepository());

        var context = new VehicleOwnershipCheckContext(
            TenantId, "ABC123", "operador@empresa.com", effective);

        var result = await guard.ValidateTransferStartAsync(context, TestContext.Current.CancellationToken);

        // Una radicación nueva sí aplica la política live (regla activa) → bloquea.
        result.IsAllowed.Should().BeFalse();
    }

    [Fact]
    public async Task Family_MatriculasFlag_BlocksWhenOnlyOwnOnMatriculas()
    {
        var guard = new VehicleOwnershipGuard(
            new FakeOwnershipChecker(isOwned: false),
            new FakeWhitelistRepository());

        var policy = new TenantSettings
        {
            TenantId = TenantId,
            AllowInitialRegistration = true,
            AllowMiscNewVehicles = true,
            OnlyOwnVehicles = false,
            OnlyOwnVehiclesMatriculas = true,
            SignatureVaultEnabled = false,
            NotificationChannel = NotificationChannel.FlitSmtp,
            NotificationTarget = NotificationTarget.Radicador,
            PaymentMethods = [],
        };

        var context = new VehicleOwnershipCheckContext(
            TenantId, "VIN123", "operador@empresa.com", policy, "MATRICULAS");

        var result = await guard.ValidateTransferStartAsync(context, TestContext.Current.CancellationToken);

        result.IsAllowed.Should().BeFalse();
    }

    [Fact]
    public async Task Family_MatriculasFlag_DoesNotAffectTraspasoWhenTraspasoOff()
    {
        var guard = new VehicleOwnershipGuard(
            new FakeOwnershipChecker(isOwned: false),
            new FakeWhitelistRepository());

        var policy = new TenantSettings
        {
            TenantId = TenantId,
            AllowInitialRegistration = true,
            AllowMiscNewVehicles = true,
            OnlyOwnVehicles = false,
            OnlyOwnVehiclesMatriculas = true,
            SignatureVaultEnabled = false,
            NotificationChannel = NotificationChannel.FlitSmtp,
            NotificationTarget = NotificationTarget.Radicador,
            PaymentMethods = [],
        };

        var context = new VehicleOwnershipCheckContext(
            TenantId, "ABC123", "operador@empresa.com", policy, "TRASPASO");

        var result = await guard.ValidateTransferStartAsync(context, TestContext.Current.CancellationToken);

        result.IsAllowed.Should().BeTrue();
    }

    // ---------- Helpers / dobles ----------

    private static TenantSettings PolicyWithOnlyOwnVehicles(bool onlyOwn) => new()
    {
        TenantId = TenantId,
        AllowInitialRegistration = true,
        AllowMiscNewVehicles = true,
        OnlyOwnVehicles = onlyOwn,
        SignatureVaultEnabled = false,
        NotificationChannel = NotificationChannel.FlitSmtp,
        NotificationTarget = NotificationTarget.Radicador,
        PaymentMethods = [],
    };

    private sealed class FakeOwnershipChecker : IVehicleTenantOwnershipChecker
    {
        private readonly bool _isOwned;

        public FakeOwnershipChecker(bool isOwned) => _isOwned = isOwned;

        public Task<bool> IsVehicleOwnedByTenantAsync(
            Guid tenantId, string vehicleIdentifier, CancellationToken cancellationToken = default) =>
            Task.FromResult(_isOwned);
    }

    private sealed class FakeWhitelistRepository : IWhitelistRepository
    {
        private readonly HashSet<string> _emails;

        public FakeWhitelistRepository(params string[] whitelisted) =>
            _emails = new HashSet<string>(
                whitelisted.Select(WhitelistEmail.Normalize), StringComparer.Ordinal);

        public Task<bool> IsEmailWhitelistedAsync(
            Guid tenantId, string email, CancellationToken cancellationToken = default) =>
            Task.FromResult(_emails.Contains(WhitelistEmail.Normalize(email)));

        public Task<WhitelistAddOutcome> AddEmailsAsync(
            Guid tenantId, IReadOnlyList<string> normalizedEmails, Guid? addedBy, Guid? correlationId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<TenantWhitelistEntry>> ListAsync(
            Guid tenantId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
