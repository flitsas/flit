using Flit.Infrastructure.Consultations;
using Flit.Tramites.Application.UseCases.Consultations;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.RuntOverrides;

/// <summary>
/// Tests del decorador de cortocircuito <see cref="TenantRuntPersonOverrideChainResolverDecorator"/>
/// (HU #14642): hit/miss de override y mapper de campos hydrated.
/// </summary>
public sealed class TenantRuntPersonOverrideChainResolverDecoratorTests
{
    // ══════════════════════════════════════════════════════════════════════════
    // BuildResult — mapper de HydratedFields
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void BuildResult_HidrataTodasLasClavesNecesarias()
    {
        var result = TenantRuntPersonOverrideChainResolverDecorator.BuildResult(SampleOverride());

        result.Provider.Should().Be("tenant_manual_conductor");
        result.HydratedFields.Should().Contain(f => f.FieldKey == "person_full_name")
            .Which.ValueText.Should().NotBeNullOrWhiteSpace();
        result.HydratedFields.Should().Contain(f => f.FieldKey == "person_first_name")
            .Which.ValueText.Should().Be("JUAN");
        result.HydratedFields.Should().Contain(f => f.FieldKey == "person_first_last_name")
            .Which.ValueText.Should().Be("PEREZ");
        result.HydratedFields.Should().Contain(f => f.FieldKey == "person_citizen_status")
            .Which.ValueText.Should().Be("ACTIVA");
        result.HydratedFields.Should().Contain(f => f.FieldKey == "person_license_status")
            .Which.ValueText.Should().Be("ACTIVO");
        result.HydratedFields.Should().Contain(f => f.FieldKey == "person_has_active_license")
            .Which.ValueText.Should().Be("true");
        result.HydratedFields.Should().Contain(f => f.FieldKey == "person_has_pending_fines")
            .Which.ValueText.Should().Be("false");
    }

    [Fact]
    public void BuildResult_OverallVerde_CuandoTodoOk()
    {
        var result = TenantRuntPersonOverrideChainResolverDecorator.BuildResult(
            SampleOverride() with { CitizenStatus = "ACTIVA", HasActiveLicense = true, HasPendingFines = false });

        result.Overall.Should().Be("green");
        result.Checks.Should().NotContain(c => c.Status == "warn" || c.Status == "fail");
    }

    [Fact]
    public void BuildResult_OverallAmarillo_CuandoLicenciaInactiva()
    {
        var result = TenantRuntPersonOverrideChainResolverDecorator.BuildResult(
            SampleOverride() with { HasActiveLicense = false });

        result.Overall.Should().Be("yellow");
        result.Checks.Should().Contain(c => c.Key == "conductor_licencia" && c.Status == "warn");
    }

    [Fact]
    public void BuildResult_OverallAmarillo_CuandoHayMultas()
    {
        var result = TenantRuntPersonOverrideChainResolverDecorator.BuildResult(
            SampleOverride() with { HasPendingFines = true });

        result.Overall.Should().Be("yellow");
        // Alineado a Verifik/Kyverum: multas pendientes → fail (overall yellow).
        result.Checks.Should().Contain(c => c.Key == "conductor_multas" && c.Status == "fail");
    }

    [Fact]
    public void BuildResult_SiempreIncluyeCheckIdentidadOk()
    {
        var result = TenantRuntPersonOverrideChainResolverDecorator.BuildResult(SampleOverride());

        result.Checks.Should().Contain(c => c.Key == "conductor_identidad" && c.Status == "ok");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Cortocircuito: HIT
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ConsultAsync_CuandoExisteOverride_CortocircuitaSinLlamarAlRemoto()
    {
        var inner = Substitute.For<IConsultationProviderChainResolver>();
        var reader = Substitute.For<ITenantRuntPersonOverrideReader>();
        var tenantId = Guid.NewGuid();
        var @override = SampleOverride() with { TenantId = tenantId };

        reader.FindActiveAsync(tenantId, "CC", "123456789", Arg.Any<CancellationToken>())
            .Returns(@override);

        var sut = new TenantRuntPersonOverrideChainResolverDecorator(inner, reader);

        var ctx = ConductorContext(tenantId, "CC", "123456789");
        var result = await sut.ConsultAsync(ConsultationKind.Conductor, ctx, null, default);

        await inner.DidNotReceive().ConsultAsync(
            Arg.Any<ConsultationKind>(), Arg.Any<ConsultationContext>(),
            Arg.Any<ConsultationTenantOverride?>(), Arg.Any<CancellationToken>());
        result.Provider.Should().Be("tenant_manual_conductor");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Cortocircuito: MISS
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ConsultAsync_CuandoNoExisteOverride_DelegaAlRemoto()
    {
        var inner = Substitute.For<IConsultationProviderChainResolver>();
        var reader = Substitute.For<ITenantRuntPersonOverrideReader>();
        var tenantId = Guid.NewGuid();

        reader.FindActiveAsync(tenantId, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((TenantRuntPersonOverride?)null);

        var expectedResult = new ConsultationResult("kyverum_runt_conductor", "green", [], []);
        inner.ConsultAsync(Arg.Any<ConsultationKind>(), Arg.Any<ConsultationContext>(), null, default)
            .Returns(expectedResult);

        var sut = new TenantRuntPersonOverrideChainResolverDecorator(inner, reader);

        var ctx = ConductorContext(tenantId, "CC", "987654321");
        var result = await sut.ConsultAsync(ConsultationKind.Conductor, ctx, null, default);

        await inner.Received(1).ConsultAsync(ConsultationKind.Conductor, ctx, null, default);
        result.Provider.Should().Be("kyverum_runt_conductor");
    }

    [Fact]
    public async Task ConsultAsync_ParaKindVehiculo_NuncaBuscaOverride()
    {
        var inner = Substitute.For<IConsultationProviderChainResolver>();
        var reader = Substitute.For<ITenantRuntPersonOverrideReader>();

        inner.ConsultAsync(Arg.Any<ConsultationKind>(), Arg.Any<ConsultationContext>(), null, default)
            .Returns(new ConsultationResult("kyverum_runt", "green", [], []));

        var sut = new TenantRuntPersonOverrideChainResolverDecorator(inner, reader);

        var ctx = new ConsultationContext(
            Guid.NewGuid(), Guid.NewGuid(), "vehicle",
            new Dictionary<string, string?> { ["plate"] = "ABC123" });

        await sut.ConsultAsync(ConsultationKind.VehiclePlate, ctx, null, default);

        await reader.DidNotReceive().FindActiveAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ConsultAsync_SinDocumentTypeEnContexto_DelegaSinBuscarOverride()
    {
        var inner = Substitute.For<IConsultationProviderChainResolver>();
        var reader = Substitute.For<ITenantRuntPersonOverrideReader>();

        inner.ConsultAsync(Arg.Any<ConsultationKind>(), Arg.Any<ConsultationContext>(), null, default)
            .Returns(new ConsultationResult("verifik_conductor", "green", [], []));

        var sut = new TenantRuntPersonOverrideChainResolverDecorator(inner, reader);

        // Sin document_type en el contexto
        var ctx = new ConsultationContext(
            Guid.NewGuid(), Guid.NewGuid(), "conductor",
            new Dictionary<string, string?> { ["document_number"] = "123" });

        await sut.ConsultAsync(ConsultationKind.Conductor, ctx, null, default);

        await reader.DidNotReceive().FindActiveAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static TenantRuntPersonOverride SampleOverride() =>
        new(
            Id: Guid.NewGuid(),
            TenantId: Guid.NewGuid(),
            DocumentType: "CC",
            DocumentNumber: "123456789",
            FirstName: "JUAN",
            SecondName: null,
            FirstLastName: "PEREZ",
            SecondLastName: "GARCIA",
            CitizenStatus: "ACTIVA",
            DriverStatus: "ACTIVO",
            HasActiveLicense: true,
            LicenseCategories: "B1",
            HasPendingFines: false,
            NroPazYSalvo: null);

    private static ConsultationContext ConductorContext(Guid tenantId, string docType, string docNumber) =>
        new(Guid.NewGuid(), tenantId, "conductor",
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["document_type"] = docType,
                ["document_number"] = docNumber,
            });
}
