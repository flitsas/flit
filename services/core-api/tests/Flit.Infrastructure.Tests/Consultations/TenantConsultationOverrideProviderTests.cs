using Flit.Admin.Domain.Companies.Settings;
using Flit.Infrastructure.Consultations;
using Flit.Tramites.Application.UseCases.Consultations;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Infrastructure.Tests.Consultations;

/// <summary>
/// HU #10478 — puente Admin→Trámites: jsonb <c>consultation_provider_config</c> +
/// <c>runt_failover_timeout_ms</c> → <see cref="ConsultationTenantOverride"/>.
/// </summary>
public sealed class TenantConsultationOverrideProviderTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static TenantSettings BaseSettings(
        ConsultationProviderConfig? config = null,
        int failoverMs = 4000) =>
        new()
        {
            TenantId = TenantId,
            AllowInitialRegistration = true,
            AllowMiscNewVehicles = true,
            OnlyOwnVehicles = false,
            SignatureVaultEnabled = false,
            NotificationChannel = NotificationChannel.FlitSmtp,
            NotificationTarget = NotificationTarget.Radicador,
            PaymentMethods = [],
            RuntFailoverTimeoutMs = failoverMs,
            ConsultationProviderConfig = config ?? ConsultationProviderConfig.Empty,
        };

    [Fact]
    public async Task GetAsync_SinFilaTenant_DevuelveNull()
    {
        var ct = TestContext.Current.CancellationToken;
        var repo = Substitute.For<ITenantSettingsRepository>();
        repo.GetAsync(TenantId, ct).Returns((TenantSettings?)null);
        var sut = new TenantConsultationOverrideProvider(repo);

        var result = await sut.GetAsync(TenantId, ct);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_ConfigVacia_PropagaTimeoutSinCadenas()
    {
        var ct = TestContext.Current.CancellationToken;
        var repo = Substitute.For<ITenantSettingsRepository>();
        repo.GetAsync(TenantId, ct).Returns(BaseSettings(failoverMs: 6000));
        var sut = new TenantConsultationOverrideProvider(repo);

        var result = await sut.GetAsync(TenantId, ct);

        result.Should().NotBeNull();
        result!.Chains.Should().BeNull();
        result.FailoverTimeoutMs.Should().Be(6000);
    }

    [Fact]
    public async Task GetAsync_ConfigPoblada_MapeaCadenasYTimeout()
    {
        var ct = TestContext.Current.CancellationToken;
        var config = new ConsultationProviderConfig(
            new Dictionary<string, ConsultationProviderSelection>(StringComparer.OrdinalIgnoreCase)
            {
                [ConsultationKindKeys.VehicleVin] =
                    new("verifik", ["kyverum_runt"]),
                [ConsultationKindKeys.VehiclePlate] =
                    new("kyverum_runt", ["verifik"]),
                [ConsultationKindKeys.Conductor] =
                    new("verifik_conductor", ["kyverum_runt_conductor"]),
            });
        var repo = Substitute.For<ITenantSettingsRepository>();
        repo.GetAsync(TenantId, ct).Returns(BaseSettings(config, failoverMs: 5500));
        var sut = new TenantConsultationOverrideProvider(repo);

        var result = await sut.GetAsync(TenantId, ct);

        result.Should().NotBeNull();
        result!.FailoverTimeoutMs.Should().Be(5500);
        result.Chains.Should().NotBeNull();
        result.Chains![ConsultationKindKeys.VehicleVin].Primary.Should().Be("verifik");
        result.Chains[ConsultationKindKeys.VehicleVin].Fallback.Should().BeEquivalentTo("kyverum_runt");
        result.Chains[ConsultationKindKeys.VehiclePlate].Primary.Should().Be("kyverum_runt");
        result.Chains[ConsultationKindKeys.Conductor].Fallback.Should()
            .BeEquivalentTo("kyverum_runt_conductor");
    }

    [Fact]
    public async Task GetAsync_SoloUnTipo_MapeaEseKind()
    {
        var ct = TestContext.Current.CancellationToken;
        var config = new ConsultationProviderConfig(
            new Dictionary<string, ConsultationProviderSelection>(StringComparer.OrdinalIgnoreCase)
            {
                [ConsultationKindKeys.Conductor] =
                    new("verifik_conductor", ["kyverum_runt_conductor"]),
            });
        var repo = Substitute.For<ITenantSettingsRepository>();
        repo.GetAsync(TenantId, ct).Returns(BaseSettings(config));
        var sut = new TenantConsultationOverrideProvider(repo);

        var result = await sut.GetAsync(TenantId, ct);

        result!.Chains.Should().ContainKey(ConsultationKindKeys.Conductor);
        result.Chains!.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetAsync_PropagaBlockYOnlyOwnPorFamilia()
    {
        var ct = TestContext.Current.CancellationToken;
        var settings = new TenantSettings
        {
            TenantId = TenantId,
            AllowInitialRegistration = false, // ⇒ bloquea MATRICULAS
            AllowMiscNewVehicles = true,
            OnlyOwnVehicles = true,
            OnlyOwnVehiclesMatriculas = true,
            OnlyOwnVehiclesOtros = false,
            BlockProcedureFamilyTraspaso = true,
            BlockProcedureFamilyOtros = false,
            SignatureVaultEnabled = false,
            NotificationChannel = NotificationChannel.FlitSmtp,
            NotificationTarget = NotificationTarget.Radicador,
            PaymentMethods = [],
            RuntFailoverTimeoutMs = 4000,
            ConsultationProviderConfig = ConsultationProviderConfig.Empty,
        };
        var repo = Substitute.For<ITenantSettingsRepository>();
        repo.GetAsync(TenantId, ct).Returns(settings);
        var sut = new TenantConsultationOverrideProvider(repo);

        var result = await sut.GetAsync(TenantId, ct);

        result.Should().NotBeNull();
        result!.OnlyOwnVehicles.Should().BeTrue();
        result.OnlyOwnVehiclesMatriculas.Should().BeTrue();
        result.OnlyOwnVehiclesOtros.Should().BeFalse();
        result.BlockProcedureFamilyMatriculas.Should().BeTrue();
        result.BlockProcedureFamilyTraspaso.Should().BeTrue();
        result.BlockProcedureFamilyOtros.Should().BeFalse();
    }
}
