using Flit.Tramites.Application.Storage;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Application.UseCases.RevocationRequests;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.RevocationRequests;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.RevocationRequests;

/// <summary>
/// HU #12572 (Feature #12565) — «Solicitar revocatoria»: motivo + documento de soporte (PDF) + 2 checks
/// de confirmación sobre un trámite <see cref="TramiteEstado.Aprobado"/>.
///
/// <para>
/// Uso de ejemplo:
/// <code>
/// var (result, error, detail) = await Handler().HandleAsync(command, CancellationToken.None);
/// </code>
/// </para>
///
/// <para>
/// <see cref="RevocationRequestGate"/> ya tiene su propia suite (<c>RevocationRequestGateTests</c>,
/// HU #12571) — aquí solo se cubre UN caso de rechazo del gate (solicitud activa existente) para probar
/// el WIRING del handler con el gate, no reevaluar toda su matriz de combinaciones.
/// </para>
/// </summary>
public sealed class RequestRevocationHandlerTests
{
    private readonly IProcedureInstanceRepository _instanceRepo = Substitute.For<IProcedureInstanceRepository>();
    private readonly IProcedureRevocationRequestRepository _revocationRepo = Substitute.For<IProcedureRevocationRequestRepository>();
    private readonly IAttachmentStorage _attachmentStorage = Substitute.For<IAttachmentStorage>();
    private readonly IBusinessDayCalculator _businessDayCalculator = Substitute.For<IBusinessDayCalculator>();
    private readonly IRevocationRequestNotifier _notifier = Substitute.For<IRevocationRequestNotifier>();
    private readonly ILogger<RequestRevocationHandler> _logger = Substitute.For<ILogger<RequestRevocationHandler>>();

    private static ProcedureInstance Instance(Guid id, Guid tenantId, string status, Guid? transitOfficeId = null) => new()
    {
        Id = id,
        TenantId = tenantId,
        ProcedureTypeId = Guid.NewGuid(),
        ReferenceNumber = "TRM-2026-001200",
        Status = status,
        TransitOfficeId = transitOfficeId,
        Origin = null,
        IsMigrated = false,
        CreatedAt = DateTimeOffset.UtcNow.AddDays(-10),
        UpdatedAt = DateTimeOffset.UtcNow.AddDays(-5),
    };

    private static RevocationSupportDocumentInput ValidDocument() => new(
        "soporte.pdf", "application/pdf", 1024, new MemoryStream([1, 2, 3, 4]));

    private static RequestRevocationCommand ValidCommand(Guid instanceId, Guid tenantId, Guid userId) => new(
        instanceId,
        tenantId,
        "El trámite se aprobó con datos incorrectos del propietario.",
        ConfirmAccuracy: true,
        ConfirmConsequences: true,
        ValidDocument(),
        userId);

    private RequestRevocationHandler Handler() => new(
        _instanceRepo, _revocationRepo, _attachmentStorage, _businessDayCalculator, _notifier, _logger);

    /// <summary>Deja pasar el gate: sin ventana configurada (sin límite) y sin solicitud activa.</summary>
    private void StubGatePasses(Guid tenantId, Guid instanceId, DateTimeOffset approvedAt)
    {
        _revocationRepo.GetFirstApprovedAtAsync(tenantId, instanceId, Arg.Any<CancellationToken>())
            .Returns(approvedAt);
        _revocationRepo.GetRevocationWindowBusinessDaysAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((int?)null);
        _revocationRepo.FindActiveAsync(tenantId, instanceId, Arg.Any<CancellationToken>())
            .Returns((ProcedureRevocationRequest?)null);
        _revocationRepo.GetNextAttemptNumberAsync(tenantId, instanceId, Arg.Any<CancellationToken>())
            .Returns(1);
    }

    // ── AC1 — envío exitoso ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC1_MotivoDocumentoYChecksValidos_CreaLaSolicitudYRegistraElTimeline()
    {
        var instanceId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var approvedAt = DateTimeOffset.UtcNow.AddDays(-3);
        var instance = Instance(instanceId, tenantId, TramiteEstado.Aprobado);
        _instanceRepo.GetByIdAsync(instanceId, tenantId, Arg.Any<CancellationToken>()).Returns(instance);
        StubGatePasses(tenantId, instanceId, approvedAt);
        _attachmentStorage.SaveAsync(instanceId, RequestRevocationHandler.SupportDocumentTipo, "soporte.pdf", Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(new StoredFile("storage/soporte.pdf", "abc123", 1024));
        var antes = DateTimeOffset.UtcNow;

        var command = ValidCommand(instanceId, tenantId, userId);
        var (result, error, errorDetail) = await Handler().HandleAsync(command, CancellationToken.None);

        error.Should().BeNull();
        errorDetail.Should().BeNull();
        result.Should().NotBeNull();
        result!.ProcedureInstanceId.Should().Be(instanceId);
        result.AttemptNumber.Should().Be(1);
        result.Status.Should().Be(ProcedureRevocationRequestStatus.Solicitada);
        result.RequestedAt.Should().BeOnOrAfter(antes);

        // Fila de la solicitud persistida.
        _revocationRepo.Received(1).Add(Arg.Is<ProcedureRevocationRequest>(r =>
            r.TenantId == tenantId
            && r.ProcedureInstanceId == instanceId
            && r.AttemptNumber == 1
            && r.Status == ProcedureRevocationRequestStatus.Solicitada
            && r.RequestedBy == userId
            && r.SupportDocumentId != Guid.Empty));
        await _revocationRepo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());

        // Adjunto del documento de soporte.
        _instanceRepo.Received(1).Add(Arg.Is<ProcedureInstanceAttachment>(a =>
            a.TenantId == tenantId
            && a.ProcedureInstanceId == instanceId
            && a.Tipo == RequestRevocationHandler.SupportDocumentTipo
            && a.Mimetype == "application/pdf"
            && a.UploadedBy == userId));

        // Timeline — evento PROPIO (AC1), no toca procedure_instance_status_history.
        await _instanceRepo.Received(1).AddEventAsync(
            Arg.Is<ProcedureInstanceEvent>(e =>
                e.Tipo == RequestRevocationHandler.EventoTipo
                && e.TenantId == tenantId
                && e.ProcedureInstanceId == instanceId
                && e.CreatedBy == userId
                && e.CreatedAt >= antes),
            Arg.Any<CancellationToken>());

        instance.Status.Should().Be(TramiteEstado.Aprobado, "el trámite permanece Aprobado durante todo el sub-flujo (ADR-0022)");
    }

    [Fact]
    public async Task AC1_InstanciaNoExiste_DevuelveNotFound()
    {
        var instanceId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        _instanceRepo.GetByIdAsync(instanceId, tenantId, Arg.Any<CancellationToken>()).Returns((ProcedureInstance?)null);

        var command = ValidCommand(instanceId, tenantId, Guid.NewGuid());
        var (result, error, _) = await Handler().HandleAsync(command, CancellationToken.None);

        error.Should().Be(TramiteEstadoErrores.NoEncontrado);
        result.Should().BeNull();
        await _revocationRepo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ── AC2 — campos faltantes (422/409, fail-fast sin tocar BD) ──────────────────────────────────

    [Fact]
    public async Task AC2_MotivoVacio_Devuelve422SinTocarRepositorios()
    {
        var instanceId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var command = ValidCommand(instanceId, tenantId, Guid.NewGuid()) with { Reason = "   " };

        var (result, error, errorDetail) = await Handler().HandleAsync(command, CancellationToken.None);

        error.Should().Be(TramiteEstadoErrores.MotivoRequerido);
        errorDetail.Should().NotBeNullOrWhiteSpace();
        result.Should().BeNull();
        await _instanceRepo.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC2_FaltaCheckDeExactitud_Devuelve422()
    {
        var instanceId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var command = ValidCommand(instanceId, tenantId, Guid.NewGuid()) with { ConfirmAccuracy = false };

        var (result, error, _) = await Handler().HandleAsync(command, CancellationToken.None);

        error.Should().Be(RequestRevocationHandler.ConfirmacionExactitudRequerida);
        result.Should().BeNull();
    }

    [Fact]
    public async Task AC2_FaltaCheckDeConsecuencias_Devuelve422()
    {
        var instanceId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var command = ValidCommand(instanceId, tenantId, Guid.NewGuid()) with { ConfirmConsequences = false };

        var (result, error, _) = await Handler().HandleAsync(command, CancellationToken.None);

        error.Should().Be(RequestRevocationHandler.ConfirmacionConsecuenciasRequerida);
        result.Should().BeNull();
    }

    [Fact]
    public async Task AC2_SinDocumentoDeSoporte_Devuelve422()
    {
        var instanceId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var command = ValidCommand(instanceId, tenantId, Guid.NewGuid()) with { SupportDocument = null };

        var (result, error, _) = await Handler().HandleAsync(command, CancellationToken.None);

        error.Should().Be(RequestRevocationHandler.DocumentoRequerido);
        result.Should().BeNull();
    }

    [Fact]
    public async Task AC2_DocumentoNoEsPdf_Devuelve422()
    {
        var instanceId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var documento = new RevocationSupportDocumentInput("foto.png", "image/png", 1024, new MemoryStream([1]));
        var command = ValidCommand(instanceId, tenantId, Guid.NewGuid()) with { SupportDocument = documento };

        var (result, error, _) = await Handler().HandleAsync(command, CancellationToken.None);

        error.Should().Be(RequestRevocationHandler.DocumentoFormatoInvalido);
        result.Should().BeNull();
    }

    [Fact]
    public async Task AC2_DocumentoExcedeElTamanoMaximo_Devuelve422()
    {
        var instanceId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var documento = new RevocationSupportDocumentInput(
            "soporte.pdf", "application/pdf", AttachmentRules.MaxSizeBytes + 1, new MemoryStream([1]));
        var command = ValidCommand(instanceId, tenantId, Guid.NewGuid()) with { SupportDocument = documento };

        var (result, error, _) = await Handler().HandleAsync(command, CancellationToken.None);

        error.Should().Be(RequestRevocationHandler.DocumentoMuyGrande);
        result.Should().BeNull();
    }

    [Theory]
    [InlineData(TramiteEstado.Borrador)]
    [InlineData(TramiteEstado.Entregado)]
    [InlineData(TramiteEstado.Rechazado)]
    [InlineData(TramiteEstado.Revocado)]
    public async Task AC2_TramiteNoAprobado_Devuelve409SinInvocarElGate(string estado)
    {
        var instanceId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = Instance(instanceId, tenantId, estado);
        _instanceRepo.GetByIdAsync(instanceId, tenantId, Arg.Any<CancellationToken>()).Returns(instance);

        var command = ValidCommand(instanceId, tenantId, Guid.NewGuid());
        var (result, error, errorDetail) = await Handler().HandleAsync(command, CancellationToken.None);

        error.Should().Be(RequestRevocationHandler.TramiteNoAprobado);
        errorDetail.Should().Contain(estado);
        result.Should().BeNull();
        // No debe consultar el gate (approvedAt/ventana) si el trámite ni siquiera está Aprobado.
        await _revocationRepo.DidNotReceive().GetFirstApprovedAtAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _revocationRepo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC2_GateRechazaPorSolicitudActivaExistente_Devuelve409SinSubirElDocumento()
    {
        // Wiring con RevocationRequestGate (suite propia en RevocationRequestGateTests, HU #12571):
        // aquí solo se prueba que el handler respeta el veredicto del gate, no la matriz completa.
        var instanceId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = Instance(instanceId, tenantId, TramiteEstado.Aprobado);
        _instanceRepo.GetByIdAsync(instanceId, tenantId, Arg.Any<CancellationToken>()).Returns(instance);
        StubGatePasses(tenantId, instanceId, DateTimeOffset.UtcNow.AddDays(-1));
        _revocationRepo.FindActiveAsync(tenantId, instanceId, Arg.Any<CancellationToken>())
            .Returns(new ProcedureRevocationRequest { Status = ProcedureRevocationRequestStatus.Solicitada });

        var command = ValidCommand(instanceId, tenantId, Guid.NewGuid());
        var (result, error, _) = await Handler().HandleAsync(command, CancellationToken.None);

        error.Should().Be(RevocationRequestGate.SolicitudActivaExistente);
        result.Should().BeNull();
        await _attachmentStorage.DidNotReceive().SaveAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC4_CarreraConcurrente_TraduceLaExcepcionDeUnicidadA409()
    {
        // AC4 — el índice único parcial de BD cierra la carrera que el gate en memoria no vio
        // (HU #12570/#12571): mismo código de negocio que el rechazo del gate.
        var instanceId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = Instance(instanceId, tenantId, TramiteEstado.Aprobado);
        _instanceRepo.GetByIdAsync(instanceId, tenantId, Arg.Any<CancellationToken>()).Returns(instance);
        StubGatePasses(tenantId, instanceId, DateTimeOffset.UtcNow.AddDays(-1));
        _attachmentStorage.SaveAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(new StoredFile("storage/soporte.pdf", "abc123", 1024));
        _revocationRepo.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new ActiveRevocationRequestExistsException()));

        var command = ValidCommand(instanceId, tenantId, Guid.NewGuid());
        var (result, error, _) = await Handler().HandleAsync(command, CancellationToken.None);

        error.Should().Be(RevocationRequestGate.SolicitudActivaExistente);
        result.Should().BeNull();
        await _notifier.DidNotReceive().NotifyAsync(Arg.Any<RevocationRequestSolicitadaEvent>(), Arg.Any<CancellationToken>());
    }

    // ── AC3 — notificación encolada (best-effort, después del commit) ─────────────────────────────

    [Fact]
    public async Task AC3_SolicitudCreada_EncolaLaNotificacionDeSolicitudRecibida()
    {
        var instanceId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var instance = Instance(instanceId, tenantId, TramiteEstado.Aprobado);
        _instanceRepo.GetByIdAsync(instanceId, tenantId, Arg.Any<CancellationToken>()).Returns(instance);
        StubGatePasses(tenantId, instanceId, DateTimeOffset.UtcNow.AddDays(-1));
        _attachmentStorage.SaveAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(new StoredFile("storage/soporte.pdf", "abc123", 1024));

        var command = ValidCommand(instanceId, tenantId, userId);
        var (result, error, _) = await Handler().HandleAsync(command, CancellationToken.None);

        error.Should().BeNull();
        await _notifier.Received(1).NotifyAsync(
            Arg.Is<RevocationRequestSolicitadaEvent>(e =>
                e.TenantId == tenantId
                && e.ProcedureInstanceId == instanceId
                && e.RevocationRequestId == result!.Id
                && e.AttemptNumber == 1
                && e.RequestedByUserId == userId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC3_FalloDelNotificador_NoRevierteLaSolicitudYaPersistida()
    {
        // ADR-0046 Opción B — best-effort: un fallo de notificación nunca tumba la respuesta 201.
        var instanceId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = Instance(instanceId, tenantId, TramiteEstado.Aprobado);
        _instanceRepo.GetByIdAsync(instanceId, tenantId, Arg.Any<CancellationToken>()).Returns(instance);
        StubGatePasses(tenantId, instanceId, DateTimeOffset.UtcNow.AddDays(-1));
        _attachmentStorage.SaveAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(new StoredFile("storage/soporte.pdf", "abc123", 1024));
        _notifier.NotifyAsync(Arg.Any<RevocationRequestSolicitadaEvent>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("cola de correo caída")));

        var command = ValidCommand(instanceId, tenantId, Guid.NewGuid());
        var (result, error, _) = await Handler().HandleAsync(command, CancellationToken.None);

        error.Should().BeNull("un fallo de notificación best-effort no debe propagarse ni tumbar la solicitud");
        result.Should().NotBeNull();
        await _revocationRepo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
