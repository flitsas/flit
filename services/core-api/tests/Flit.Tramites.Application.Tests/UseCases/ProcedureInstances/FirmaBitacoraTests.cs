using System.Text.Json;
using Flit.Tramites.Application.Tracking;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Catalog;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// HU #14175 — bitácora de firma electrónica.
/// Cuando un documento del trámite pasa a <c>firmada</c>, el historial gana UNA entrada con
/// el documento, el estado y el firmante. Registrar la misma firma dos veces NO deja una segunda fila.
/// </summary>
public sealed class FirmaBitacoraTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly SimularFirmaHandler _handler;

    public FirmaBitacoraTests()
    {
        _handler = new SimularFirmaHandler(_repo);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────

    private static ProcedureInstance BuildInstance(
        Guid id, Guid tenantId, string? firmante = "Ana Gómez")
    {
        var instance = new ProcedureInstance
        {
            Id = id,
            TenantId = tenantId,
            ProcedureType = ProcedureTypeFixture.Traspaso,
            ProcedureTypeId = ProcedureTypeFixture.Traspaso.Id,
            ReferenceNumber = "TRM-2026-000001",
            Status = TramiteEstado.Borrador,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        if (!string.IsNullOrWhiteSpace(firmante))
        {
            instance.Actors.Add(new ProcedureInstanceActor
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ProcedureInstanceId = id,
                ActorType = SignatureRules.ParteComprador,   // "comprador"
                FullName = firmante,
                DocumentType = "CC",
                DocumentNumber = "12345678",
                ProcedureEntityId = Guid.NewGuid(),
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }

        return instance;
    }

    private static ProcedureInstanceSignature BuildSignature(
        Guid tenantId, Guid instanceId,
        string estado = SignatureEstados.Enviada,
        string parte  = SignatureRules.ParteComprador,
        string docTipo = SignatureDocTipos.Compraventa)
        => new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureInstanceId = instanceId,
            Parte = parte,
            DocTipo = docTipo,
            Estado = estado,
            EnvelopeId = "env-bitacora-test",
            SolicitadoAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    // ── AC1 ───────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// AC1 — Pendiente → Firmado deja un evento <c>dato_modificado</c> con:
    /// - tipo "dato_modificado"
    /// - campo "firma"
    /// - etiqueta "Firma"
    /// - anterior conteniendo "Pendiente", el doc_tipo y el firmante
    /// - nuevo  conteniendo "Firmada",   el doc_tipo y el firmante
    /// </summary>
    [Fact]
    public async Task AC1_TransicionEnviadaAFirmada_RegistraEventoBitacoraConDocEstadoYFirmante()
    {
        var ct = TestContext.Current.CancellationToken;
        var id     = Guid.NewGuid();
        var tenant = Guid.NewGuid();

        // Configurar captura del evento ANTES del act
        ProcedureInstanceEvent? capturedEvent = null;
        _repo.When(r => r.AddEventAsync(Arg.Any<ProcedureInstanceEvent>(), Arg.Any<CancellationToken>()))
             .Do(call => capturedEvent = call.Arg<ProcedureInstanceEvent>());

        var instance = BuildInstance(id, tenant, firmante: "Ana Gómez");
        var sig      = BuildSignature(tenant, id);
        instance.Signatures.Add(sig);
        _repo.GetByIdWithFurGraphAsync(id, tenant, ct).Returns(instance);

        // Act
        var (result, error) = await _handler.HandleAsync(id, tenant, sig.Id, ct);

        // Assert resultado
        error.Should().BeNull();
        result!.Estado.Should().Be(SignatureEstados.Firmada);

        // Assert: AddEventAsync llamado exactamente una vez
        await _repo.Received(1).AddEventAsync(Arg.Any<ProcedureInstanceEvent>(), ct);

        // Assert: el evento capturado tiene los campos correctos
        capturedEvent.Should().NotBeNull();
        capturedEvent!.TenantId.Should().Be(tenant);
        capturedEvent.ProcedureInstanceId.Should().Be(id);
        capturedEvent.Tipo.Should().Be(ProcedureFieldChangeRecorder.EventType);
        capturedEvent.CreatedBy.Should().BeNull("la llamada sin actorUserId deja CreatedBy null");

        using var doc = JsonDocument.Parse(capturedEvent.Payload);
        var root = doc.RootElement;

        root.GetProperty("etiqueta").GetString().Should().Be("Firma");
        root.GetProperty("campo").GetString().Should().Be("firma");

        var anterior = root.GetProperty("anterior").GetString()!;
        var nuevo    = root.GetProperty("nuevo").GetString()!;

        anterior.Should().Contain("Pendiente",    because: "el estado antes de la firma es Pendiente");
        anterior.Should().Contain("Compraventa",  because: "el doc_tipo del fixture es compraventa");
        anterior.Should().Contain("Ana Gómez",    because: "el firmante es el actor comprador");

        nuevo.Should().Contain("Firmada",         because: "el estado nuevo es Firmada");
        nuevo.Should().Contain("Compraventa",     because: "el doc_tipo del fixture es compraventa");
        nuevo.Should().Contain("Ana Gómez",       because: "el firmante es el actor comprador");

        anterior.Should().NotBe(nuevo,            because: "anterior y nuevo deben diferir para que haya evento");
    }

    // ── AC2 ───────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// AC2 — Volver a registrar la misma firma (llamar HandleAsync cuando la firma ya está
    /// <c>firmada</c>) no llama <see cref="IProcedureInstanceRepository.AddEventAsync"/> de nuevo.
    /// </summary>
    [Fact]
    public async Task AC2_RegistrarMismaFirmaSegundaVez_NoLlamaAddEventAsync()
    {
        var ct     = TestContext.Current.CancellationToken;
        var id     = Guid.NewGuid();
        var tenant = Guid.NewGuid();

        var instance = BuildInstance(id, tenant, firmante: "Ana Gómez");
        var sig      = BuildSignature(tenant, id);
        instance.Signatures.Add(sig);
        _repo.GetByIdWithFurGraphAsync(id, tenant, ct).Returns(instance);

        // Primera llamada: transiciona a Firmada → escribe el evento y guarda.
        var (result1, error1) = await _handler.HandleAsync(id, tenant, sig.Id, ct);
        error1.Should().BeNull();
        result1!.Estado.Should().Be(SignatureEstados.Firmada);

        await _repo.Received(1).AddEventAsync(Arg.Any<ProcedureInstanceEvent>(), ct);
        await _repo.Received(1).SaveChangesAsync(ct);

        // Segunda llamada: ya está Firmada → salida temprana sin nuevo evento ni SaveChanges.
        var (result2, error2) = await _handler.HandleAsync(id, tenant, sig.Id, ct);
        error2.Should().BeNull();
        result2!.Estado.Should().Be(SignatureEstados.Firmada);

        // AddEventAsync sigue habiendo sido llamado solo 1 vez en total (AC2).
        await _repo.Received(1).AddEventAsync(Arg.Any<ProcedureInstanceEvent>(), ct);
        // SaveChangesAsync tampoco se repite.
        await _repo.Received(1).SaveChangesAsync(ct);
    }

    // ── BuildFirmaLabel — unitarios del helper estático ──────────────────────────────────────────

    [Fact]
    public void BuildFirmaLabel_Enviada_DevuelvePendienteConDocYFirmante()
    {
        var id       = Guid.NewGuid();
        var instance = BuildInstance(id, Guid.NewGuid(), firmante: "Juan Pérez");

        var label = SimularFirmaHandler.BuildFirmaLabel(
            SignatureEstados.Enviada, SignatureDocTipos.Compraventa, instance, SignatureRules.ParteComprador);

        label.Should().Be("Pendiente · Compraventa · Juan Pérez");
    }

    [Fact]
    public void BuildFirmaLabel_Firmada_DevuelveFirmadaConDocYFirmante()
    {
        var id       = Guid.NewGuid();
        var instance = BuildInstance(id, Guid.NewGuid(), firmante: "Juan Pérez");

        var label = SimularFirmaHandler.BuildFirmaLabel(
            SignatureEstados.Firmada, SignatureDocTipos.Compraventa, instance, SignatureRules.ParteComprador);

        label.Should().Be("Firmada · Compraventa · Juan Pérez");
    }

    [Fact]
    public void BuildFirmaLabel_SinFirmante_OmiteFirmanteDelLabel()
    {
        var id       = Guid.NewGuid();
        var instance = BuildInstance(id, Guid.NewGuid(), firmante: null);

        var label = SimularFirmaHandler.BuildFirmaLabel(
            SignatureEstados.Firmada, SignatureDocTipos.Compraventa, instance, SignatureRules.ParteComprador);

        label.Should().Be("Firmada · Compraventa");
        label.Should().NotContain(" · · ", because: "sin firmante no debe quedar un separador vacío extra");
    }

    [Fact]
    public void BuildFirmaLabel_DocTipoMinuscula_CapitalizaPrimeraLetra()
    {
        var id       = Guid.NewGuid();
        var instance = BuildInstance(id, Guid.NewGuid(), firmante: null);

        var label = SimularFirmaHandler.BuildFirmaLabel(
            SignatureEstados.Enviada, "mandato", instance, SignatureRules.ParteComprador);

        label.Should().Contain("Mandato", because: "doc_tipo debe capitalizarse");
    }
}
