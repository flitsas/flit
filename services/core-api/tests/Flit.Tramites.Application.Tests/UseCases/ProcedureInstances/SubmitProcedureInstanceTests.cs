using Flit.Tramites.Application.Tests.UseCases.ProcedureInstances.Estados;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
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

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// Radicar (N 03): el submit orquesta el lifecycle service — borrador→preparado (gate RF03)
/// + preparado→entregado (gates OT). Usa el servicio REAL con puertos fake: los asserts de
/// historial/notificación se hacen sobre los registros capturados (la escritura física del
/// historial es del recorder de HU-2).
/// </summary>
public sealed class SubmitProcedureInstanceTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly IProcedureTypeRepository _typeRepo = Substitute.For<IProcedureTypeRepository>();
    private readonly ITransitOfficeGrantGate _grantGate = Substitute.For<ITransitOfficeGrantGate>();
    private readonly RecordingTransitionRecorder _recorder = new();
    private readonly RecordingTransitionPublisher _publisher = new();
    private readonly SubmitProcedureInstanceHandler _sut;

    public SubmitProcedureInstanceTests()
    {
        // Por defecto, cualquier OT se considera habilitado (la restricción se ejercita
        // explícitamente en los tests que la cubren) y el commit no encuentra conflicto.
        _grantGate
            .IsEnabledForTenantAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _repo.SaveChangesWithConcurrencyGuardAsync(Arg.Any<CancellationToken>()).Returns(true);

        var lifecycle = new TramiteLifecycleService(
            _repo,
            _typeRepo,
            _grantGate,
            NullOtRuleGate.Instance,
            _recorder,
            _publisher);
        _sut = new SubmitProcedureInstanceHandler(lifecycle, _repo);
    }

    private static ProcedureInstance Instance(Guid id, Guid tenantId, string status) =>
        new()
        {
            Id = id,
            TenantId = tenantId,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000001",
            Status = status,
            ModalidadEntrada = "matricula_inicial",
            TipologiaCodigo = TramiteTipologiaCatalog.CodigoMatriculaInicial,
            CreatedAt = DateTimeOffset.UtcNow
        };

    /// <summary>Instancia matrícula que satisface TODOS los gates de radicado (happy path).</summary>
    private static ProcedureInstance FullyGated(Guid id, Guid tenantId)
    {
        var i = Instance(id, tenantId, TramiteEstado.Borrador);
        // Documentos obligatorios matrícula: factura + aduana + impronta.
        foreach (var t in new[] { "factura", "aduana", "impronta", "fur" })
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
        // Biométrica del comprador aprobada.
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
        // Organismo de tránsito seleccionado.
        i.FieldValues.Add(new ProcedureInstanceFieldValue
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureInstanceId = id,
            FieldKey = "transit_office_code",
            ValueText = "11001000",
            Source = "user",
        });
        return i;
    }

    private static ProcedureType PublishedType(Guid id) =>
        new()
        {
            Id = id,
            Code = "X",
            Name = "X",
            Family = "matriculas",
            PublicationStatus = PublicationStatus.Published,
            CreatedAt = DateTimeOffset.UtcNow
        };

    private void Wire(ProcedureInstance instance, CancellationToken ct)
    {
        _repo.GetByIdAsync(instance.Id, instance.TenantId, ct).Returns(instance);
        _repo.GetByIdWithWizardGraphAsync(instance.Id, instance.TenantId, ct).Returns(instance);
        _typeRepo.GetByIdAsync(instance.ProcedureTypeId, ct).Returns(PublishedType(instance.ProcedureTypeId));
    }

    [Fact]
    public async Task HandleAsync_NotFound_ReturnsNotFound()
    {
        var ct = TestContext.Current.CancellationToken;
        _repo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct)
            .Returns((ProcedureInstance?)null);

        var (result, error) = await _sut.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), changedBy: null, ct);

        error.Should().Be("not_found");
        result.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_YaEntregado_ReturnsTransicionNoPermitida()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = Instance(id, tenantId, TramiteEstado.Entregado);
        Wire(instance, ct);

        var (result, error) = await _sut.HandleAsync(id, tenantId, changedBy: null, ct);

        error.Should().Be(TramiteEstadoErrores.TransicionNoPermitida);
        result.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_EstadoFinal_ReturnsEstadoFinal()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = Instance(id, tenantId, TramiteEstado.Aprobado);
        Wire(instance, ct);

        var (result, error) = await _sut.HandleAsync(id, tenantId, changedBy: null, ct);

        error.Should().Be(TramiteEstadoErrores.EstadoFinal);
        result.Should().BeNull();
        instance.Status.Should().Be(TramiteEstado.Aprobado); // RF04: inmutable
    }

    [Fact]
    public async Task HandleAsync_BorradorConGates_EncadenaPreparadoYEntregado()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = FullyGated(id, tenantId);
        Wire(instance, ct);

        var (result, error) = await _sut.HandleAsync(id, tenantId, changedBy: null, ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
        result!.Status.Should().Be(TramiteEstado.Entregado);
        result.SubmittedAt.Should().NotBeNull();
        instance.Status.Should().Be(TramiteEstado.Entregado);
        instance.SubmittedAt.Should().NotBeNull();

        // Dos transiciones = dos registros de historial y dos notificaciones, en orden.
        _recorder.Records.Should().HaveCount(2);
        _recorder.Records[0].Should().Match<Flit.Tramites.Domain.Tramites.Estados.TramiteTransitionRecord>(r =>
            r.FromStatus == TramiteEstado.Borrador && r.ToStatus == TramiteEstado.Preparado);
        _recorder.Records[1].Should().Match<Flit.Tramites.Domain.Tramites.Estados.TramiteTransitionRecord>(r =>
            r.FromStatus == TramiteEstado.Preparado && r.ToStatus == TramiteEstado.Entregado);
        _publisher.Published.Should().HaveCount(2);
        await _repo.Received(2).SaveChangesWithConcurrencyGuardAsync(ct);
    }

    [Fact]
    public async Task HandleAsync_DesdePreparado_SoloEntrega()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = FullyGated(id, tenantId);
        instance.Status = TramiteEstado.Preparado;
        Wire(instance, ct);

        var (result, error) = await _sut.HandleAsync(id, tenantId, changedBy: null, ct);

        error.Should().BeNull();
        result!.Status.Should().Be(TramiteEstado.Entregado);
        _recorder.Records.Should().ContainSingle(r =>
            r.FromStatus == TramiteEstado.Preparado && r.ToStatus == TramiteEstado.Entregado);
    }

    private static readonly Guid BogotaOfficeId =
        Guid.Parse("aaaaaaaa-0001-4000-8000-000000000001");

    private static void SeleccionarOt(ProcedureInstance instance, Guid officeId)
    {
        instance.FieldValues.Add(new ProcedureInstanceFieldValue
        {
            Id = Guid.NewGuid(),
            TenantId = instance.TenantId,
            ProcedureInstanceId = instance.Id,
            FieldKey = "transit_office_id",
            ValueText = officeId.ToString(),
            Source = "user",
        });
    }

    [Fact]
    public async Task HandleAsync_OrganismoNoHabilitado_QuedaEnPreparadoSinEntregar()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = FullyGated(id, tenantId);
        SeleccionarOt(instance, BogotaOfficeId);
        Wire(instance, ct);
        _grantGate.IsEnabledForTenantAsync(tenantId, BogotaOfficeId, Arg.Any<CancellationToken>())
            .Returns(false); // OT NO habilitado para la empresa

        var (result, error) = await _sut.HandleAsync(id, tenantId, changedBy: null, ct);

        error.Should().Be("organismo_no_habilitado");
        result.Should().BeNull();
        // N 03: la preparación (gate RF03) sí ocurrió; la entrega quedó bloqueada. Corregida la
        // causa, un nuevo submit reintenta solo preparado→entregado.
        instance.Status.Should().Be(TramiteEstado.Preparado);
        _recorder.Records.Should().ContainSingle(r => r.ToStatus == TramiteEstado.Preparado);
        await _repo.Received(1).SaveChangesWithConcurrencyGuardAsync(ct);
    }

    [Fact]
    public async Task HandleAsync_OrganismoHabilitado_PromueveTransitOfficeIdYRadica()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = FullyGated(id, tenantId);
        SeleccionarOt(instance, BogotaOfficeId);
        instance.TransitOfficeId.Should().BeNull(); // el FUR solo persistía field_values
        Wire(instance, ct);
        _grantGate.IsEnabledForTenantAsync(tenantId, BogotaOfficeId, Arg.Any<CancellationToken>())
            .Returns(true);

        var (result, error) = await _sut.HandleAsync(id, tenantId, changedBy: null, ct);

        error.Should().BeNull();
        // El id se promueve a la columna para el motor de reglas OT y los listados.
        instance.TransitOfficeId.Should().Be(BogotaOfficeId);
        instance.Status.Should().Be(TramiteEstado.Entregado);
    }

    [Fact]
    public async Task HandleAsync_DocumentosIncompletos_ReturnsGateError()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = FullyGated(id, tenantId);
        instance.Attachments.Clear(); // sin docs ni FUR → primer gate que falla es documentos_incompletos
        Wire(instance, ct);

        var (result, error) = await _sut.HandleAsync(id, tenantId, changedBy: null, ct);

        error.Should().Be(TramiteEstadoErrores.DocumentosIncompletos);
        result.Should().BeNull();
        instance.Status.Should().Be(TramiteEstado.Borrador);
        _recorder.Records.Should().BeEmpty();
        _publisher.Published.Should().BeEmpty();
        await _repo.DidNotReceive().SaveChangesWithConcurrencyGuardAsync(ct);
    }

    [Fact]
    public async Task HandleAsync_IdentidadNoAprobada_ReturnsGateError()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = FullyGated(id, tenantId);
        instance.BiometricValidations.Clear(); // docs+fur+organismo ok, falta biométrica
        Wire(instance, ct);

        var (_, error) = await _sut.HandleAsync(id, tenantId, changedBy: null, ct);

        error.Should().Be(TramiteEstadoErrores.IdentidadNoAprobada);
        instance.Status.Should().Be(TramiteEstado.Borrador);
    }

    [Fact]
    public async Task HandleAsync_SinFur_TransitionsToEntregado()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = FullyGated(id, tenantId);
        instance.Attachments.Remove(instance.Attachments.First(a => a.Tipo == "fur"));
        Wire(instance, ct);

        var (result, error) = await _sut.HandleAsync(id, tenantId, changedBy: null, ct);

        error.Should().BeNull();
        result!.Status.Should().Be(TramiteEstado.Entregado);
    }

    [Fact]
    public async Task HandleAsync_SinOrganismo_TransitionsToEntregado()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = FullyGated(id, tenantId);
        instance.FieldValues.Clear();
        Wire(instance, ct);

        var (result, error) = await _sut.HandleAsync(id, tenantId, changedBy: null, ct);

        error.Should().BeNull();
        result!.Status.Should().Be(TramiteEstado.Entregado);
    }

    // ── HU #10431 — autoría (changed_by) en la radicación ─────────────────────────
    // La guarda FK contra identity.users vive ahora en el RECORDER (HU-2); a este nivel se
    // asegura que la orden de transición viaja con el usuario autenticado.

    [Fact]
    public async Task HandleAsync_ConUsuario_PropagaChangedByAlHistorial()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var instance = FullyGated(id, tenantId);
        Wire(instance, ct);

        var (_, error) = await _sut.HandleAsync(id, tenantId, userId, ct);

        error.Should().BeNull();
        _recorder.Records.Should().OnlyContain(r => r.ChangedByUserId == userId);
        _publisher.Published.Should().OnlyContain(r => r.ChangedByUserId == userId);
    }

    [Fact]
    public async Task HandleAsync_SinUsuario_RegistraChangedByNull()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = FullyGated(id, tenantId);
        Wire(instance, ct);

        var (_, error) = await _sut.HandleAsync(id, tenantId, changedBy: null, ct);

        error.Should().BeNull();
        _recorder.Records.Should().OnlyContain(r => r.ChangedByUserId == null);
    }
}
