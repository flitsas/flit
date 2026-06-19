using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Catalog;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// Cableado del estado real de la biométrica (Slice 6) en el wizard server-driven:
/// matrícula paso 4 (identidad) y traspaso paso 6 (FUR, ambas partes).
/// </summary>
public sealed class WizardBiometricaStateTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly GetWizardStateHandler _handler;

    public WizardBiometricaStateTests()
    {
        _handler = new GetWizardStateHandler(_repo);
    }

    private static ProcedureInstance Base(string modalidad, string? tipologia = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000001",
            Status = ProcedureInstanceStatus.Draft,
            ModalidadEntrada = modalidad,
            TipologiaCodigo = tipologia,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private static ProcedureInstanceBiometricValidation Biometria(string? parte, string estado) =>
        new()
        {
            Id = Guid.NewGuid(),
            Parte = parte,
            Estado = estado,
            Nombre = "X", TipoDoc = "CC", Documento = "1", Email = "x@y.com",
            TokenHash = Guid.NewGuid().ToString("N"),
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private void Setup(ProcedureInstance instance) =>
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(instance);

    // ── Matrícula: paso 4 (identidad) refleja biométrica del comprador (parte null) ──

    [Fact]
    public async Task Matricula_NoBiometria_IdentidadIncompleteWithReason()
    {
        var ct = TestContext.Current.CancellationToken;
        Setup(Base("matricula_inicial"));

        var (result, _) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        var s4 = result!.Steps.Single(s => s.Index == 4);
        s4.Status.Should().Be("incomplete");
        s4.Reasons.Should().Contain("identidad_pendiente");
    }

    [Fact]
    public async Task Matricula_BiometriaAprobada_IdentidadFlipsToComplete()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Base("matricula_inicial");
        instance.BiometricValidations.Add(Biometria(parte: null, estado: BiometricEstados.Aprobado));
        Setup(instance);

        var (result, _) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        var s4 = result!.Steps.Single(s => s.Index == 4);
        s4.Status.Should().Be("complete");
        s4.Reasons.Should().BeEmpty();
    }

    [Fact]
    public async Task Matricula_BiometriaRechazada_IdentidadStillIncomplete()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Base("matricula_inicial");
        instance.BiometricValidations.Add(Biometria(parte: null, estado: BiometricEstados.Rechazado));
        Setup(instance);

        var (result, _) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        result!.Steps.Single(s => s.Index == 4).Status.Should().Be("incomplete");
    }

    // ── Traspaso: paso 6 (FUR) exige biométrica de AMBAS partes ──────────────────

    [Fact]
    public async Task Traspaso_NoBiometria_Step6HasBiometriaReason()
    {
        var ct = TestContext.Current.CancellationToken;
        Setup(Base("traspaso"));

        var (result, _) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        result!.Steps.Single(s => s.Index == 6).Reasons
            .Should().Contain(GetWizardStateHandler.PendienteBiometria);
    }

    [Fact]
    public async Task Traspaso_OnlyCompradorAprobado_StillRequiresVendedor()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Base("traspaso", TramiteTipologiaCatalog.CodigoTraspasoStandard);
        instance.BiometricValidations.Add(Biometria(parte: "comprador", estado: BiometricEstados.Aprobado));
        Setup(instance);

        var (result, _) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        // Falta vendedor → biométrica aún pendiente.
        result!.Steps.Single(s => s.Index == 6).Reasons
            .Should().Contain(GetWizardStateHandler.PendienteBiometria);
    }

    [Fact]
    public async Task Traspaso_BothPartesAprobadas_NoBiometriaReason()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Base("traspaso", TramiteTipologiaCatalog.CodigoTraspasoStandard);
        instance.BiometricValidations.Add(Biometria(parte: "comprador", estado: BiometricEstados.Aprobado));
        instance.BiometricValidations.Add(Biometria(parte: "vendedor", estado: BiometricEstados.Aprobado));
        Setup(instance);

        var (result, _) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        var s6 = result!.Steps.Single(s => s.Index == 6);
        s6.Reasons.Should().NotContain(GetWizardStateHandler.PendienteBiometria);
        // La firma (slice 7) sigue diferida.
        s6.Reasons.Should().Contain(GetWizardStateHandler.PendienteFirma);
    }
}
