using System.Text.Json;
using Flit.Tramites.Application.Tracking;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// HU #14177 — bitácora de cambios de datos campo a campo.
///
/// <para><b>AC1</b>: placa, prioritario, subsanación con motivo y radicación producen
/// cada uno su evento <c>dato_modificado</c> con etiqueta, campo, anterior y nuevo correctos.</para>
///
/// <para><b>AC2</b>: después de radicar, el id de la instancia no cambia y no se llamó a
/// crear otro trámite (el handler de envío no crea <see cref="ProcedureInstance"/> nueva).</para>
/// </summary>
public sealed class BitacoraCambiosDatos14177Tests
{
    private readonly IProcedureInstanceRepository _repo     = Substitute.For<IProcedureInstanceRepository>();
    private readonly ITramiteLifecycleService      _lifecycle = Substitute.For<ITramiteLifecycleService>();

    private static readonly Guid TenantId   = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private static readonly Guid InstanceId = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
    private static readonly Guid UserId     = Guid.Parse("cccccccc-cccc-4ccc-8ccc-cccccccccccc");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ─────────────────────────────────────────────────────────────────────
    // AC1 · Placa (BitacoraPlacaOtHandler)
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC1_PlacaAsignada_ProduceEventoDatoModificadoConEtiquetaYTextos()
    {
        // Arrange
        ProcedureInstanceEvent? captured = null;
        _repo.AddEventAsync(Arg.Do<ProcedureInstanceEvent>(e => captured = e), Arg.Any<CancellationToken>())
             .Returns(Task.CompletedTask);
        _repo.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var sut = new BitacoraPlacaOtHandler(_repo);

        // Act — primera asignación (anterior vacío)
        await sut.HandleAsync(TenantId, InstanceId, previousPlate: null, newPlate: "ABC123", UserId, Ct);

        // Assert
        captured.Should().NotBeNull();
        captured!.Tipo.Should().Be(ProcedureFieldChangeRecorder.EventType);
        captured.TenantId.Should().Be(TenantId);
        captured.ProcedureInstanceId.Should().Be(InstanceId);
        captured.CreatedBy.Should().Be(UserId);
        captured.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, precision: TimeSpan.FromSeconds(5));

        var payload = JsonDocument.Parse(captured.Payload).RootElement;
        payload.GetProperty("etiqueta").GetString().Should().Be("Placa");
        payload.GetProperty("campo").GetString().Should().Be("plate");
        payload.GetProperty("anterior").GetString().Should().Be("");
        payload.GetProperty("nuevo").GetString().Should().Be("ABC123");
    }

    [Fact]
    public async Task AC1_PlacaCorregida_AnteriorEsLaPlacaPrevia()
    {
        // Arrange
        ProcedureInstanceEvent? captured = null;
        _repo.AddEventAsync(Arg.Do<ProcedureInstanceEvent>(e => captured = e), Arg.Any<CancellationToken>())
             .Returns(Task.CompletedTask);
        _repo.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var sut = new BitacoraPlacaOtHandler(_repo);

        // Act — corrección dentro de la ventana (anterior → nuevo)
        await sut.HandleAsync(TenantId, InstanceId, previousPlate: "XYZ999", newPlate: "ABC123", UserId, Ct);

        // Assert
        var payload = JsonDocument.Parse(captured!.Payload).RootElement;
        payload.GetProperty("anterior").GetString().Should().Be("XYZ999");
        payload.GetProperty("nuevo").GetString().Should().Be("ABC123");
    }

    [Fact]
    public async Task AC1_PlacaIgual_NoGeneraEvento()
    {
        // Arrange — misma placa (ambas normalizadas a "ABC123")
        var sut = new BitacoraPlacaOtHandler(_repo);

        // Act
        await sut.HandleAsync(TenantId, InstanceId, previousPlate: "ABC123", newPlate: "ABC123", UserId, Ct);

        // Assert — no se añadió evento
        await _repo.DidNotReceive().AddEventAsync(Arg.Any<ProcedureInstanceEvent>(), Arg.Any<CancellationToken>());
    }

    // ─────────────────────────────────────────────────────────────────────
    // AC1 · Prioritario (SetPriorityProcedureInstanceHandler)
    // ─────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(false, true,  "No",  "Sí")]   // activar prioridad
    [InlineData(true,  false, "Sí",  "No")]   // desactivar prioridad
    public async Task AC1_PrioritarioActivadoODesactivado_ProduceEventoConTextosSiNo(
        bool valorActual, bool nuevoValor, string anteriorEsperado, string nuevoEsperado)
    {
        // Arrange
        var instance = InstanceEnEstado(TramiteEstado.Asignado);
        instance.Prioritario = valorActual;
        _repo.GetByIdAsync(instance.Id, instance.TenantId, Arg.Any<CancellationToken>())
             .Returns(instance);
        _repo.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        ProcedureInstanceEvent? captured = null;
        _repo.AddEventAsync(Arg.Do<ProcedureInstanceEvent>(e => captured = e), Arg.Any<CancellationToken>())
             .Returns(Task.CompletedTask);

        var sut = new SetPriorityProcedureInstanceHandler(_repo);

        // Act
        var (result, error) = await sut.HandleAsync(instance.Id, instance.TenantId, nuevoValor, UserId, Ct);

        // Assert
        error.Should().BeNull();
        result!.Prioritario.Should().Be(nuevoValor);

        captured.Should().NotBeNull();
        captured!.Tipo.Should().Be(ProcedureFieldChangeRecorder.EventType);
        var payload = JsonDocument.Parse(captured.Payload).RootElement;
        payload.GetProperty("etiqueta").GetString().Should().Be("Trámite prioritario");
        payload.GetProperty("campo").GetString().Should().Be("prioritario");
        payload.GetProperty("anterior").GetString().Should().Be(anteriorEsperado);
        payload.GetProperty("nuevo").GetString().Should().Be(nuevoEsperado);
    }

    [Fact]
    public async Task AC1_PrioritarioMismoValor_NoGeneraEvento()
    {
        // Arrange
        var instance = InstanceEnEstado(TramiteEstado.Asignado);
        instance.Prioritario = true;
        _repo.GetByIdAsync(instance.Id, instance.TenantId, Arg.Any<CancellationToken>())
             .Returns(instance);

        var sut = new SetPriorityProcedureInstanceHandler(_repo);

        // Act — mismo valor que ya tiene
        await sut.HandleAsync(instance.Id, instance.TenantId, true, UserId, Ct);

        // Assert — no se añadió evento
        await _repo.DidNotReceive().AddEventAsync(Arg.Any<ProcedureInstanceEvent>(), Arg.Any<CancellationToken>());
    }

    // ─────────────────────────────────────────────────────────────────────
    // AC1 · Subsanación con motivo (StartSubsanacionHandler)
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC1_SubsanacionConMotivo_IncluyeElMotivoEnNuevo()
    {
        // Arrange
        var instance = InstanceEnEstado(TramiteEstado.Rechazado);
        _repo.GetByIdWithDetailsAsync(instance.Id, instance.TenantId, Arg.Any<CancellationToken>())
             .Returns(instance);
        _repo.SaveChangesWithConcurrencyGuardAsync(Arg.Any<CancellationToken>()).Returns(true);

        ProcedureInstanceEvent? captured = null;
        _repo.AddEventAsync(Arg.Do<ProcedureInstanceEvent>(e => captured = e), Arg.Any<CancellationToken>())
             .Returns(Task.CompletedTask);

        var sut = new StartSubsanacionHandler(_repo);

        // Act
        var (result, error) = await sut.HandleAsync(
            instance.Id, instance.TenantId, UserId, reason: "Faltó la licencia de tránsito", ct: Ct);

        // Assert
        error.Should().BeNull();
        captured.Should().NotBeNull();
        captured!.Tipo.Should().Be(ProcedureFieldChangeRecorder.EventType);
        var payload = JsonDocument.Parse(captured.Payload).RootElement;
        payload.GetProperty("etiqueta").GetString().Should().Be("Subsanación");
        payload.GetProperty("campo").GetString().Should().Be("subsanacion");
        payload.GetProperty("anterior").GetString().Should().Be("Desactivada");
        payload.GetProperty("nuevo").GetString().Should().Be("Activada · Faltó la licencia de tránsito");
    }

    [Fact]
    public async Task AC1_SubsanacionSinMotivo_NuevoEsSoloActivada()
    {
        // Arrange
        var instance = InstanceEnEstado(TramiteEstado.Rechazado);
        _repo.GetByIdWithDetailsAsync(instance.Id, instance.TenantId, Arg.Any<CancellationToken>())
             .Returns(instance);
        _repo.SaveChangesWithConcurrencyGuardAsync(Arg.Any<CancellationToken>()).Returns(true);

        ProcedureInstanceEvent? captured = null;
        _repo.AddEventAsync(Arg.Do<ProcedureInstanceEvent>(e => captured = e), Arg.Any<CancellationToken>())
             .Returns(Task.CompletedTask);

        var sut = new StartSubsanacionHandler(_repo);

        // Act — sin motivo (null)
        await sut.HandleAsync(instance.Id, instance.TenantId, UserId, reason: null, ct: Ct);

        var payload = JsonDocument.Parse(captured!.Payload).RootElement;
        payload.GetProperty("anterior").GetString().Should().Be("Desactivada");
        payload.GetProperty("nuevo").GetString().Should().Be("Activada");
    }

    [Fact]
    public async Task AC1_SubsanacionSinMotivoExplicito_UsaElMotivoDelRechazo()
    {
        var instance = InstanceEnEstado(TramiteEstado.Rechazado);
        instance.StatusHistory.Add(new ProcedureInstanceStatusHistory
        {
            ToStatus = TramiteEstado.Rechazado,
            Reason = "Falta el SOAT",
            ChangedAt = DateTimeOffset.UtcNow,
        });
        _repo.GetByIdWithDetailsAsync(instance.Id, instance.TenantId, Arg.Any<CancellationToken>())
             .Returns(instance);
        _repo.SaveChangesWithConcurrencyGuardAsync(Arg.Any<CancellationToken>()).Returns(true);

        ProcedureInstanceEvent? captured = null;
        _repo.AddEventAsync(Arg.Do<ProcedureInstanceEvent>(e => captured = e), Arg.Any<CancellationToken>())
             .Returns(Task.CompletedTask);

        var sut = new StartSubsanacionHandler(_repo);
        var (result, error) = await sut.HandleAsync(instance.Id, instance.TenantId, UserId, ct: Ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
        captured.Should().NotBeNull();
        var payload = JsonDocument.Parse(captured!.Payload).RootElement;
        payload.GetProperty("nuevo").GetString().Should().Be("Activada · Falta el SOAT");
    }

    [Fact]
    public async Task AC1_CancelarSubsanacion_ProduceEventoActivadaADesactivada()
    {
        // Arrange
        var instance = InstanceEnEstado(TramiteEstado.Rechazado);
        instance.SubsanacionActiva = true;
        _repo.GetByIdWithDetailsAsync(instance.Id, instance.TenantId, Arg.Any<CancellationToken>())
             .Returns(instance);
        _repo.SaveChangesWithConcurrencyGuardAsync(Arg.Any<CancellationToken>()).Returns(true);

        ProcedureInstanceEvent? captured = null;
        _repo.AddEventAsync(Arg.Do<ProcedureInstanceEvent>(e => captured = e), Arg.Any<CancellationToken>())
             .Returns(Task.CompletedTask);

        var sut = new CancelSubsanacionHandler(_repo);

        // Act
        var (result, error) = await sut.HandleAsync(instance.Id, instance.TenantId, UserId, Ct);

        // Assert
        error.Should().BeNull();
        captured.Should().NotBeNull();
        var payload = JsonDocument.Parse(captured!.Payload).RootElement;
        payload.GetProperty("etiqueta").GetString().Should().Be("Subsanación");
        payload.GetProperty("campo").GetString().Should().Be("subsanacion");
        payload.GetProperty("anterior").GetString().Should().Be("Activada");
        payload.GetProperty("nuevo").GetString().Should().Be("Desactivada");
    }

    // ─────────────────────────────────────────────────────────────────────
    // AC1 · Radicación (EnviarAlOtHandler)
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC1_Radicacion_ProduceEventoAnteriorVacioNuevoRadicado()
    {
        // Arrange
        var instance = InstanceEnEstado(TramiteEstado.Asignado);
        _repo.GetByIdWithDetailsAsync(instance.Id, instance.TenantId, Arg.Any<CancellationToken>())
             .Returns(instance);
        _repo.SaveChangesWithConcurrencyGuardAsync(Arg.Any<CancellationToken>()).Returns(true);
        _repo.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        _lifecycle.TransitionAsync(Arg.Any<TramiteTransitionCommand>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                instance.Status = call.Arg<TramiteTransitionCommand>().ToStatus;
                return TramiteTransitionOutcome.Ok(instance);
            });

        ProcedureInstanceEvent? captured = null;
        _repo.AddEventAsync(Arg.Do<ProcedureInstanceEvent>(e => captured = e), Arg.Any<CancellationToken>())
             .Returns(Task.CompletedTask);

        var sut = new EnviarAlOtHandler(_repo, _lifecycle);

        // Act
        var (result, error, _) = await sut.HandleAsync(
            instance.Id, instance.TenantId, UserId, new EnviarAlOtRequest(), Ct);

        // Assert
        error.Should().BeNull();
        result.Should().NotBeNull();
        captured.Should().NotBeNull("debe haber un evento de radicación");
        captured!.Tipo.Should().Be(ProcedureFieldChangeRecorder.EventType);
        captured.CreatedBy.Should().Be(UserId);

        var payload = JsonDocument.Parse(captured.Payload).RootElement;
        payload.GetProperty("etiqueta").GetString().Should().Be("Radicación");
        payload.GetProperty("campo").GetString().Should().Be("radicacion");
        payload.GetProperty("anterior").GetString().Should().Be("");
        payload.GetProperty("nuevo").GetString().Should().Be("Radicado");
    }

    // ─────────────────────────────────────────────────────────────────────
    // AC2 · Radicación — el handler NO crea otra ProcedureInstance
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC2_DespuesDeRadicar_IdNoEsNuevoYNoSeLlamoACrearInstancia()
    {
        // Arrange
        var instance = InstanceEnEstado(TramiteEstado.Asignado);
        var originalId = instance.Id;

        _repo.GetByIdWithDetailsAsync(instance.Id, instance.TenantId, Arg.Any<CancellationToken>())
             .Returns(instance);
        _repo.SaveChangesWithConcurrencyGuardAsync(Arg.Any<CancellationToken>()).Returns(true);
        _repo.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _repo.AddEventAsync(Arg.Any<ProcedureInstanceEvent>(), Arg.Any<CancellationToken>())
             .Returns(Task.CompletedTask);

        _lifecycle.TransitionAsync(Arg.Any<TramiteTransitionCommand>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                instance.Status = call.Arg<TramiteTransitionCommand>().ToStatus;
                return TramiteTransitionOutcome.Ok(instance);
            });

        var sut = new EnviarAlOtHandler(_repo, _lifecycle);

        // Act
        var (result, error, _) = await sut.HandleAsync(
            instance.Id, instance.TenantId, UserId, new EnviarAlOtRequest(), Ct);

        // Assert — misma instancia, no una nueva
        error.Should().BeNull();
        result!.Id.Should().Be(originalId, "el handler no debe crear una ProcedureInstance nueva");
        result.Status.Should().Be(TramiteEstado.Entregado);

        // El repositorio NUNCA debe haber recibido AddAsync (crear instancia nueva).
        await _repo.DidNotReceive().AddAsync(Arg.Any<ProcedureInstance>(), Arg.Any<CancellationToken>());
        await _repo.DidNotReceive().AddWithUniqueReferenceAsync(
            Arg.Any<ProcedureInstance>(), Arg.Any<CancellationToken>());
    }

    // ─────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────

    private static ProcedureInstance InstanceEnEstado(string estado) =>
        new()
        {
            ProcedureType   = ProcedureTypeFixture.Matricula,
            Id              = InstanceId,
            TenantId        = TenantId,
            ProcedureTypeId = ProcedureTypeFixture.Matricula.Id,
            ReferenceNumber = "TRM-2026-014177",
            Status          = estado,
            CreatedAt       = DateTimeOffset.UtcNow,
        };
}
