using Flit.Tramites.Application.UseCases.ProcedureInstances.Estados;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Catalog;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances.Estados;

/// <summary>
/// Servicio único de ciclo de vida (N 03, ADR-0022): máquina RF02, finales RF04, gate de
/// preparación RF03 con causa exacta, motivo obligatorio RF05, y atomicidad RNF01 (recorder y
/// publisher exactamente una vez por transición exitosa; cero en fallo; conflicto → 409).
/// </summary>
public sealed class TramiteLifecycleServiceTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly IProcedureTypeRepository _typeRepo = Substitute.For<IProcedureTypeRepository>();
    private readonly ITransitOfficeGrantGate _grantGate = Substitute.For<ITransitOfficeGrantGate>();
    private readonly RecordingTransitionRecorder _recorder = new();
    private readonly RecordingTransitionPublisher _publisher = new();
    private readonly TramiteLifecycleService _sut;

    public TramiteLifecycleServiceTests()
    {
        _grantGate
            .IsEnabledForTenantAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _repo.SaveChangesWithConcurrencyGuardAsync(Arg.Any<CancellationToken>()).Returns(true);
        _sut = new TramiteLifecycleService(
            _repo, _typeRepo, _grantGate, NullOtRuleGate.Instance, _recorder, _publisher);
    }

    private ProcedureInstance Wire(string status, bool conGates = false)
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var i = new ProcedureInstance
        {
            Id = id,
            TenantId = tenantId,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000001",
            Status = status,
            ModalidadEntrada = "matricula_inicial",
            TipologiaCodigo = TramiteTipologiaCatalog.CodigoMatriculaInicial,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        if (conGates)
        {
            foreach (var t in new[] { "factura", "aduana", "impronta" })
            {
                i.Attachments.Add(new ProcedureInstanceAttachment
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    ProcedureInstanceId = id,
                    Tipo = t,
                    StoragePath = $"p/{t}",
                    UploadedAt = DateTimeOffset.UtcNow,
                });
            }
            i.BiometricValidations.Add(new ProcedureInstanceBiometricValidation
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ProcedureInstanceId = id,
                PartyRole = "comprador",
                Status = BiometricEstados.Aprobado,
                Name = "X",
                DocumentType = "CC",
                DocumentNumber = "1",
                Email = "x@y.com",
                TokenHash = Guid.NewGuid().ToString("N"),
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }
        _repo.GetByIdWithWizardGraphAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(i);
        _typeRepo.GetByIdAsync(i.ProcedureTypeId, Arg.Any<CancellationToken>()).Returns(new ProcedureType
        {
            Id = i.ProcedureTypeId,
            Code = "X",
            Name = "X",
            Family = "matriculas",
            PublicationStatus = PublicationStatus.Published,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        return i;
    }

    private Task<TramiteTransitionOutcome> Transition(
        ProcedureInstance i, string to, string? reason = null, Guid? changedBy = null) =>
        _sut.TransitionAsync(
            new TramiteTransitionCommand(i.Id, i.TenantId, to, reason, changedBy),
            TestContext.Current.CancellationToken);

    [Fact]
    public async Task NotFound_CuandoLaInstanciaNoExiste()
    {
        var outcome = await _sut.TransitionAsync(
            new TramiteTransitionCommand(Guid.NewGuid(), Guid.NewGuid(), TramiteEstado.Anulado, "x", null),
            TestContext.Current.CancellationToken);

        outcome.Success.Should().BeFalse();
        outcome.ErrorCode.Should().Be(TramiteEstadoErrores.NoEncontrado);
    }

    [Fact]
    public async Task EstadoDesconocido_RechazaVocabularioViejo()
    {
        var i = Wire(TramiteEstado.Borrador);

        var outcome = await Transition(i, "submitted");

        outcome.ErrorCode.Should().Be(TramiteEstadoErrores.EstadoDesconocido);
        i.Status.Should().Be(TramiteEstado.Borrador);
    }

    [Theory]
    [InlineData("aprobado")]
    [InlineData("anulado")]
    public async Task EstadoFinal_NoAdmiteNingunaTransicion(string estadoFinal)
    {
        var i = Wire(estadoFinal);

        foreach (var destino in TramiteEstado.Todos)
        {
            var outcome = await Transition(i, destino, reason: "x");
            outcome.ErrorCode.Should().Be(TramiteEstadoErrores.EstadoFinal, $"destino {destino}");
        }

        i.Status.Should().Be(estadoFinal);
        _recorder.Records.Should().BeEmpty();
        _publisher.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task TransicionNoPermitida_ConDetalleDesdeHacia()
    {
        var i = Wire(TramiteEstado.Borrador);

        var outcome = await Transition(i, TramiteEstado.Aprobado);

        outcome.ErrorCode.Should().Be(TramiteEstadoErrores.TransicionNoPermitida);
        outcome.ErrorDetail.Should().Contain("borrador").And.Contain("aprobado");
    }

    [Theory]
    [InlineData("anulado")]
    [InlineData("rechazado")]
    public async Task MotivoRequerido_ParaAnularYRechazar(string destino)
    {
        // borrador→anulado y entregado→rechazado son válidas en máquina; sin motivo → error RF05.
        var from = destino == "anulado" ? TramiteEstado.Borrador : TramiteEstado.Entregado;
        var i = Wire(from);

        var sinMotivo = await Transition(i, destino, reason: "  ");
        sinMotivo.ErrorCode.Should().Be(TramiteEstadoErrores.MotivoRequerido);

        var conMotivo = await Transition(i, destino, reason: "Motivo de negocio");
        conMotivo.Success.Should().BeTrue();
    }

    [Fact]
    public async Task GatePreparacion_IdentidadNoAprobada_ConCausaExacta()
    {
        var i = Wire(TramiteEstado.Borrador, conGates: true);
        i.BiometricValidations.Clear();

        var outcome = await Transition(i, TramiteEstado.Preparado);

        outcome.ErrorCode.Should().Be(TramiteEstadoErrores.IdentidadNoAprobada);
        outcome.ErrorDetail.Should().Contain("identidad");
        i.Status.Should().Be(TramiteEstado.Borrador);
    }

    [Fact]
    public async Task GatePreparacion_DocumentosIncompletos_ConCausaExacta()
    {
        var i = Wire(TramiteEstado.Borrador, conGates: true);
        i.Attachments.Clear();

        var outcome = await Transition(i, TramiteEstado.Preparado);

        outcome.ErrorCode.Should().Be(TramiteEstadoErrores.DocumentosIncompletos);
        outcome.ErrorDetail.Should().Contain("documentos");
    }

    [Fact]
    public async Task TransicionExitosa_RegistraYPublicaExactamenteUnaVez()
    {
        var i = Wire(TramiteEstado.Borrador, conGates: true);
        var userId = Guid.NewGuid();

        var outcome = await Transition(i, TramiteEstado.Preparado, changedBy: userId);

        outcome.Success.Should().BeTrue();
        i.Status.Should().Be(TramiteEstado.Preparado);
        _recorder.Records.Should().ContainSingle(r =>
            r.FromStatus == TramiteEstado.Borrador
            && r.ToStatus == TramiteEstado.Preparado
            && r.ChangedByUserId == userId
            && r.ProcedureInstanceId == i.Id
            && r.TenantId == i.TenantId);
        _publisher.Published.Should().ContainSingle(r => r.ToStatus == TramiteEstado.Preparado);
        await _repo.Received(1).SaveChangesWithConcurrencyGuardAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TransicionFallida_NoRegistraNiPublica()
    {
        var i = Wire(TramiteEstado.Borrador);

        await Transition(i, TramiteEstado.Entregado); // inválida por máquina

        _recorder.Records.Should().BeEmpty();
        _publisher.Published.Should().BeEmpty();
        await _repo.DidNotReceive().SaveChangesWithConcurrencyGuardAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ConflictoConcurrencia_Devuelve409SinEfectosParciales()
    {
        var i = Wire(TramiteEstado.Borrador, conGates: true);
        _repo.SaveChangesWithConcurrencyGuardAsync(Arg.Any<CancellationToken>()).Returns(false);

        var outcome = await Transition(i, TramiteEstado.Preparado);

        outcome.Success.Should().BeFalse();
        outcome.ErrorCode.Should().Be(TramiteEstadoErrores.ConflictoConcurrencia);
        outcome.Instance.Should().BeNull();
    }

    [Fact]
    public async Task EntregadoSellaSubmittedAt()
    {
        var i = Wire(TramiteEstado.Preparado);

        var outcome = await Transition(i, TramiteEstado.Entregado);

        outcome.Success.Should().BeTrue();
        i.SubmittedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task RechazadoVuelveABorradorParaSubsanar()
    {
        var i = Wire(TramiteEstado.Rechazado);

        var outcome = await Transition(i, TramiteEstado.Borrador);

        outcome.Success.Should().BeTrue();
        i.Status.Should().Be(TramiteEstado.Borrador);
    }
}
