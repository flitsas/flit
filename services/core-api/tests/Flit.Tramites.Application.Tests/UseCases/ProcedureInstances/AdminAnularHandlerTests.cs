using Flit.Tramites.Application.Tests.UseCases.ProcedureInstances.Estados;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// HU #12160 — anulación administrativa: anula desde CUALQUIER estado (AC1), salvo dos excepciones
/// duras que son autoridad exclusiva del organismo de tránsito: <c>aprobado</c> (AC2) y <c>revocado</c>
/// (AC3, comparado por string porque ese estado todavía no existe en el dominio — HU #12165). AC4
/// (confirmación previa) es responsabilidad del frontend (HU #12163); la contribución de este handler
/// es no aplicar NINGÚN cambio cuando la validación falla.
/// </summary>
public sealed class AdminAnularHandlerTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly RecordingTransitionRecorder _recorder = new();

    private static ProcedureInstance Instance(Guid id, Guid tenantId, string status) => new()
    {
        Id = id,
        TenantId = tenantId,
        ProcedureTypeId = Guid.NewGuid(),
        ReferenceNumber = "TRM-2026-000998",
        Status = status,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private AdminAnularHandler Handler() => new(_repo, _recorder);

    // ── AC1 — anula desde cualquier estado no protegido ───────────────────────────────────────────

    [Theory]
    [InlineData(TramiteEstado.Borrador)]
    [InlineData(TramiteEstado.Preparado)]
    [InlineData(TramiteEstado.Entregado)]
    [InlineData(TramiteEstado.Rechazado)]
    [InlineData(TramiteEstado.Anulado)]
    public async Task AC1_DesdeCualquierEstadoNoProtegido_QuedaEnAnulado(string origen)
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var instance = Instance(id, tenantId, origen);
        _repo.GetByIdAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);
        _repo.SaveChangesWithConcurrencyGuardAsync(Arg.Any<CancellationToken>()).Returns(true);

        var command = new AdminAnularCommand(id, tenantId, "radicación inválida", userId);
        var (result, error, _) = await Handler().HandleAsync(command, CancellationToken.None);

        error.Should().BeNull();
        result.Should().NotBeNull();
        result!.PreviousStatus.Should().Be(origen);
        result.NewStatus.Should().Be(TramiteEstado.Anulado);
        instance.Status.Should().Be(TramiteEstado.Anulado);
    }

    [Fact]
    public async Task AC1_CambioAplicado_QuedaRegistradoConEstadoAnteriorNuevoUsuarioFechaYHora()
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var instance = Instance(id, tenantId, TramiteEstado.Rechazado);
        _repo.GetByIdAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);
        _repo.SaveChangesWithConcurrencyGuardAsync(Arg.Any<CancellationToken>()).Returns(true);
        var antes = DateTimeOffset.UtcNow;

        var command = new AdminAnularCommand(id, tenantId, "radicación inválida", userId);
        var (_, error, _) = await Handler().HandleAsync(command, CancellationToken.None);

        error.Should().BeNull();

        // Historial (misma tabla que las transiciones normales, vía el puerto compartido).
        _recorder.Records.Should().ContainSingle();
        var record = _recorder.Records[0];
        record.TenantId.Should().Be(tenantId);
        record.ProcedureInstanceId.Should().Be(id);
        record.FromStatus.Should().Be(TramiteEstado.Rechazado);
        record.ToStatus.Should().Be(TramiteEstado.Anulado);
        record.ChangedByUserId.Should().Be(userId);
        record.ChangedAt.Should().BeOnOrAfter(antes);
        record.Reason.Should().Be("radicación inválida");

        // Evento PROPIO que diferencia esta anulación de una transición normal del flujo.
        await _repo.Received(1).AddEventAsync(
            Arg.Is<ProcedureInstanceEvent>(e =>
                e.Tipo == AdminAnularHandler.EventoTipo
                && e.TenantId == tenantId
                && e.ProcedureInstanceId == id
                && e.CreatedBy == userId
                && e.CreatedAt >= antes),
            Arg.Any<CancellationToken>());

        await _repo.Received(1).SaveChangesWithConcurrencyGuardAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC1_InstanciaNoExiste_DevuelveNotFound()
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        _repo.GetByIdAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns((ProcedureInstance?)null);

        var command = new AdminAnularCommand(id, tenantId, null, null);
        var (result, error, _) = await Handler().HandleAsync(command, CancellationToken.None);

        error.Should().Be(TramiteEstadoErrores.NoEncontrado);
        result.Should().BeNull();
    }

    [Fact]
    public async Task AC1_ConflictoDeConcurrencia_DevuelveErrorSinResultado()
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = Instance(id, tenantId, TramiteEstado.Borrador);
        _repo.GetByIdAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);
        _repo.SaveChangesWithConcurrencyGuardAsync(Arg.Any<CancellationToken>()).Returns(false);

        var command = new AdminAnularCommand(id, tenantId, null, null);
        var (result, error, _) = await Handler().HandleAsync(command, CancellationToken.None);

        error.Should().Be(TramiteEstadoErrores.ConflictoConcurrencia);
        result.Should().BeNull();
    }

    // ── AC2 — Aprobado como origen: rechazado siempre (422) ───────────────────────────────────────

    [Fact]
    public async Task AC2_Aprobado_SeRechazaSinAplicarNingunCambio()
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = Instance(id, tenantId, TramiteEstado.Aprobado);
        _repo.GetByIdAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);

        var command = new AdminAnularCommand(id, tenantId, null, null);
        var (result, error, errorDetail) = await Handler().HandleAsync(command, CancellationToken.None);

        error.Should().Be(TramiteEstadoErrores.CannotAnnulApproved);
        errorDetail.Should().NotBeNullOrWhiteSpace();
        result.Should().BeNull();
        instance.Status.Should().Be(TramiteEstado.Aprobado, "el rechazo no debe aplicar ningún cambio");
        _recorder.Records.Should().BeEmpty();
        await _repo.DidNotReceive().SaveChangesWithConcurrencyGuardAsync(Arg.Any<CancellationToken>());
        await _repo.DidNotReceive().AddEventAsync(Arg.Any<ProcedureInstanceEvent>(), Arg.Any<CancellationToken>());
    }

    // ── AC3 — Revocado (string, el enum aún no existe — HU #12165) como origen: rechazado (422) ────

    [Theory]
    [InlineData("revocado")]
    [InlineData("REVOCADO")]
    [InlineData("Revocado")]
    public async Task AC3_Revocado_SeRechazaSinImportarElCasing_AunSinExistirComoEnum(string origen)
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = Instance(id, tenantId, origen);
        _repo.GetByIdAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);

        // El estado "revocado" NO existe en TramiteEstado.Todos: si algún día se agrega el enum real
        // (HU #12165) y este assert empieza a fallar, es la señal de venir a limpiar la comparación
        // por string en AdminAnularHandler (ver su XML doc).
        TramiteEstado.EsValido("revocado").Should().BeFalse(
            "revocado' todavía no es un estado de negocio conocido (HU #12165 lo introducirá)");

        var command = new AdminAnularCommand(id, tenantId, null, null);
        var (result, error, errorDetail) = await Handler().HandleAsync(command, CancellationToken.None);

        error.Should().Be(TramiteEstadoErrores.CannotAnnulRevoked);
        errorDetail.Should().NotBeNullOrWhiteSpace();
        result.Should().BeNull();
        instance.Status.Should().Be(origen, "el rechazo no debe aplicar ningún cambio");
        _recorder.Records.Should().BeEmpty();
        await _repo.DidNotReceive().SaveChangesWithConcurrencyGuardAsync(Arg.Any<CancellationToken>());
        await _repo.DidNotReceive().AddEventAsync(Arg.Any<ProcedureInstanceEvent>(), Arg.Any<CancellationToken>());
    }

    // ── AC4 — el backend nunca aplica un cambio accidental cuando la validación falla ─────────────

    [Fact]
    public async Task AC4_TramiteNoEncontrado_NoAplicaNingunCambioNiRegistraHistorial()
    {
        // AC4 es responsabilidad del frontend (confirmación explícita, HU #12163); la contribución de
        // este handler es no aplicar NUNCA un cambio cuando la validación (existencia/exclusiones) falla.
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        _repo.GetByIdAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns((ProcedureInstance?)null);

        var command = new AdminAnularCommand(id, tenantId, null, null);
        var (result, error, _) = await Handler().HandleAsync(command, CancellationToken.None);

        error.Should().Be(TramiteEstadoErrores.NoEncontrado);
        result.Should().BeNull();
        _recorder.Records.Should().BeEmpty();
        await _repo.DidNotReceive().SaveChangesWithConcurrencyGuardAsync(Arg.Any<CancellationToken>());
        await _repo.DidNotReceive().AddEventAsync(Arg.Any<ProcedureInstanceEvent>(), Arg.Any<CancellationToken>());
    }
}
