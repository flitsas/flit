using System.Text.Json;
using Flit.Tramites.Application.UseCases.ConsolidadoLotes;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Entities.ConsolidadoLotes;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Flit.Tramites.Application.Tests.ConsolidadoLotes;

/// <summary>
/// HU #13371 — <see cref="ConsolidadoLoteEntregador"/>: «el lote nunca regenera». Los generadores son los
/// REALES (<see cref="GenerarConsolidadoHandler"/> / <see cref="GenerarConsolidadoMaestroHandler"/>) sobre un
/// repositorio sustituto y un storage en memoria, para que «no regenera» y «no toca el FUR» se verifiquen
/// contra el código que está en PDN y no contra un doble.
/// <para>Uso de ejemplo:
/// <c>var r = await entregador.EntregarAsync(new LoteItemEntregaRequest(id, tenantId, LoteTipoDocumento.Consolidado));</c>
/// ⇒ <c>r.Estado</c>, <c>r.DeliveryMode</c> (<c>existente</c> | <c>generado</c>) y <c>r.Codigo</c> si se omite.</para>
/// </summary>
public sealed class ConsolidadoLoteEntregadorTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly LoteFakeStorage _storage = new();
    private readonly IExpedienteHotDocumentsRegenerator _hotDocs = Substitute.For<IExpedienteHotDocumentsRegenerator>();
    private readonly IImprontaAutoGenerator _impronta = Substitute.For<IImprontaAutoGenerator>();
    private readonly IMaestroRadicadoLookup _radicado = Substitute.For<IMaestroRadicadoLookup>();
    private readonly ConsolidadoLoteEntregador _entregador;

    public ConsolidadoLoteEntregadorTests()
    {
        var merger = new LoteFakeMerger();
        _radicado.AttachmentRadicadoAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Guid?)null);
        _radicado.AttachmentsProtegidosAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlySet<Guid>)new HashSet<Guid>());
        _entregador = new ConsolidadoLoteEntregador(
            _repo,
            new GenerarConsolidadoHandler(_repo, merger, _storage, hotDocsRegenerator: _hotDocs, improntaGenerator: _impronta),
            new GenerarConsolidadoMaestroHandler(_repo, merger, _storage, maestroRadicado: _radicado, hotDocsRegenerator: _hotDocs),
            maestroRadicado: _radicado);
    }

    public static TheoryData<string> AmbosTipos() => new() { LoteTipoDocumento.Consolidado, LoteTipoDocumento.ConsolidadoMaestro };

    public static TheoryData<string, string> FinalesPorTipo()
    {
        var data = new TheoryData<string, string>();
        foreach (var estado in new[] { TramiteEstado.Aprobado, TramiteEstado.Anulado, TramiteEstado.Revocado })
        {
            data.Add(estado, LoteTipoDocumento.Consolidado);
            data.Add(estado, LoteTipoDocumento.ConsolidadoMaestro);
        }

        return data;
    }

    // ── AC1 — consolidado desactualizado se entrega tal cual ────────────────────────────────────

    [Theory]
    [MemberData(nameof(AmbosTipos))]
    public async Task AC1_ExistenteDesactualizado_SeEntregaTalCual_SinEscribirNada(string tipo)
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instancia(TramiteEstado.Entregado);
        var existente = AddAttachment(instance, tipo, "previo.pdf", "system");
        instance.ConsolidadoWizardVigente = false;
        instance.ConsolidadoMaestroVigente = false;
        instance.ExpedienteActualizadoEn = DateTimeOffset.UtcNow.AddHours(1); // FUR desactualizado también
        var adjuntosAntes = instance.Attachments.Select(a => (a.Id, a.Sha256, a.UploadedAt)).ToList();
        Wire(instance);

        var r = await _entregador.EntregarAsync(new LoteItemEntregaRequest(instance.Id, instance.TenantId, tipo), ct);

        r.Estado.Should().Be(LoteItemEntregaEstado.Incluido);
        r.DeliveryMode.Should().Be(LoteDeliveryMode.Existente);
        r.Adjunto!.AttachmentId.Should().Be(existente.Id);
        r.Adjunto.Sha256.Should().Be(existente.Sha256);
        r.Adjunto.StoragePath.Should().Be(existente.StoragePath);
        r.Adjunto.SizeBytes.Should().Be(existente.SizeBytes);
        instance.Events.Should().NotContain(e => e.Tipo == "consolidado_generado" || e.Tipo == "fur_generado"
            || e.Tipo == "consolidado_maestro_generado");
        instance.Attachments.Select(a => (a.Id, a.Sha256, a.UploadedAt)).Should().Equal(adjuntosAntes);
        _storage.Saved.Should().BeEmpty();
        _storage.Deleted.Should().BeEmpty();
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _repo.DidNotReceive().GetByIdWithChecklistGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _hotDocs.DidNotReceive().RegenerateHotDocumentsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC1_Contrato_ExistenteCargadoPorUsuario_TambienSeTomaTalCual()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instancia(TramiteEstado.Borrador);
        var cargado = AddAttachment(instance, "consolidado", "cargado_admin.pdf", "user");
        Wire(instance);

        var r = await _entregador.EntregarAsync(new LoteItemEntregaRequest(instance.Id, instance.TenantId, LoteTipoDocumento.Consolidado), ct);

        r.Should().Be(LoteItemEntregaResult.Incluido(
            new LoteItemAdjunto(cargado.Id, cargado.StoragePath, cargado.SizeBytes, cargado.Sha256, cargado.Filename),
            LoteDeliveryMode.Existente));
    }

    [Fact]
    public async Task AC1_Edge_DosFilasDelTipo_SeTomaLaMasReciente()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instancia(TramiteEstado.Entregado);
        var viejo = AddAttachment(instance, "consolidado", "viejo.pdf", "system");
        viejo.UploadedAt = DateTimeOffset.UtcNow.AddDays(-2);
        var nuevo = AddAttachment(instance, "consolidado", "nuevo.pdf", "system");
        Wire(instance);

        var r = await _entregador.EntregarAsync(new LoteItemEntregaRequest(instance.Id, instance.TenantId, LoteTipoDocumento.Consolidado), ct);

        r.Adjunto!.AttachmentId.Should().Be(nuevo.Id);
    }

    // ── AC2 — aprobado o rechazado con consolidado ──────────────────────────────────────────────

    [Theory]
    [InlineData(TramiteEstado.Aprobado, LoteTipoDocumento.Consolidado)]
    [InlineData(TramiteEstado.Rechazado, LoteTipoDocumento.Consolidado)]
    [InlineData(TramiteEstado.Aprobado, LoteTipoDocumento.ConsolidadoMaestro)]
    [InlineData(TramiteEstado.Rechazado, LoteTipoDocumento.ConsolidadoMaestro)]
    public async Task AC2_AprobadoORechazado_ConConsolidado_EntregaElDeBd_SinRegenerar(string estado, string tipo)
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instancia(estado);
        var existente = AddAttachment(instance, tipo, "decision_final.pdf", "system");
        Wire(instance);

        var r = await _entregador.EntregarAsync(new LoteItemEntregaRequest(instance.Id, instance.TenantId, tipo), ct);

        r.Estado.Should().Be(LoteItemEntregaEstado.Incluido);
        r.DeliveryMode.Should().Be(LoteDeliveryMode.Existente);
        r.Adjunto!.AttachmentId.Should().Be(existente.Id);
        _storage.Saved.Should().BeEmpty();
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC2_Edge_MigradoV1EnFinal_ConConsolidado_TambienSeEntrega()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instancia(TramiteEstado.Aprobado);
        instance.IsMigrated = true;
        var v1 = AddAttachment(instance, "consolidado", "expediente_v1.pdf", "migration");
        Wire(instance);

        var r = await _entregador.EntregarAsync(new LoteItemEntregaRequest(instance.Id, instance.TenantId, LoteTipoDocumento.Consolidado), ct);

        r.Adjunto!.AttachmentId.Should().Be(v1.Id);
        r.DeliveryMode.Should().Be(LoteDeliveryMode.Existente);
    }

    // ── AC3 — estado final sin consolidado y con FUR ────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(FinalesPorTipo))]
    public async Task AC3_EstadoFinal_SinConsolidado_ConFur_GeneraConElGeneradorOficial_SinRefecharFur(string estado, string tipo)
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instancia(estado);
        var fur = instance.Attachments.Single(a => a.Tipo == "fur");
        var furAntes = (fur.Id, fur.Sha256, fur.UploadedAt, fur.StoragePath);
        Wire(instance);

        var r = await _entregador.EntregarAsync(new LoteItemEntregaRequest(instance.Id, instance.TenantId, tipo), ct);

        r.Estado.Should().Be(LoteItemEntregaEstado.Incluido);
        r.DeliveryMode.Should().Be(LoteDeliveryMode.Generado);
        var nuevo = instance.Attachments.Single(a => a.Tipo == tipo);
        r.Adjunto!.AttachmentId.Should().Be(nuevo.Id);
        r.Adjunto.StoragePath.Should().Be(nuevo.StoragePath, "el snapshot sale del adjunto persistido");
        _storage.Saved.Should().ContainSingle("solo se sube el consolidado nuevo, nunca un FUR");
        var furDespues = instance.Attachments.Where(a => a.Tipo == "fur").Should().ContainSingle().Which;
        (furDespues.Id, furDespues.Sha256, furDespues.UploadedAt, furDespues.StoragePath).Should().Be(furAntes,
            "el FUR del trámite no se re-fecha ni se sustituye");
        instance.Events.Should().NotContain(e => e.Tipo == "fur_generado");
        await _hotDocs.DidNotReceive().RegenerateHotDocumentsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _impronta.DidNotReceive().TryGenerateAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC3_Contrato_LaGeneracionEsLaDelGeneradorOficial_EventoConsolidadoGenerado()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instancia(TramiteEstado.Aprobado);
        Wire(instance);

        await _entregador.EntregarAsync(new LoteItemEntregaRequest(instance.Id, instance.TenantId, LoteTipoDocumento.Consolidado), ct);

        instance.Events.Should().ContainSingle(e => e.Tipo == "consolidado_generado",
            "el lote no reimplementa la generación: el evento lo escribe GenerarConsolidadoHandler");
        instance.ConsolidadoWizardVigente.Should().BeTrue();
    }

    [Fact]
    public async Task AC3_Edge_RechazadoSinConsolidado_NoEsFinal_TambienSeGenera()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instancia(TramiteEstado.Rechazado);
        Wire(instance);

        var r = await _entregador.EntregarAsync(new LoteItemEntregaRequest(instance.Id, instance.TenantId, LoteTipoDocumento.Consolidado), ct);

        r.DeliveryMode.Should().Be(LoteDeliveryMode.Generado);
    }

    // ── AC4 — excepciones que se omiten ─────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(FinalesPorTipo))]
    public async Task AC4_MigradoV1EnFinal_SinConsolidado_SeOmite_MigradoSoloLectura(string estado, string tipo)
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instancia(estado);
        instance.IsMigrated = true;
        Wire(instance);

        var r = await _entregador.EntregarAsync(new LoteItemEntregaRequest(instance.Id, instance.TenantId, tipo), ct);

        r.Should().Be(LoteItemEntregaResult.Omitido(ConsolidadoLoteOmisiones.MigradoSoloLectura));
        _storage.Saved.Should().BeEmpty();
        await _repo.DidNotReceive().GetByIdWithChecklistGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(TramiteEstado.Aprobado)]
    [InlineData(TramiteEstado.Anulado)]
    [InlineData(TramiteEstado.Revocado)]
    public async Task AC4_EstadoFinal_SinConsolidado_SinFur_SeOmite_FurRequerido(string estado)
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instancia(estado, conFur: false);
        Wire(instance);

        var r = await _entregador.EntregarAsync(new LoteItemEntregaRequest(instance.Id, instance.TenantId, LoteTipoDocumento.Consolidado), ct);

        r.Should().Be(LoteItemEntregaResult.Omitido(ConsolidadoLoteOmisiones.FurRequerido));
        _storage.Saved.Should().BeEmpty();
        await _hotDocs.DidNotReceive().RegenerateHotDocumentsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _repo.DidNotReceive().GetByIdWithChecklistGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC4_Edge_TramiteInexistenteOBorrado_SeOmite_AccesoRevocado()
    {
        var ct = TestContext.Current.CancellationToken;
        var borrado = Instancia(TramiteEstado.Entregado);
        borrado.DeletedAt = DateTimeOffset.UtcNow;
        Wire(borrado);

        var rBorrado = await _entregador.EntregarAsync(new LoteItemEntregaRequest(borrado.Id, borrado.TenantId, LoteTipoDocumento.Consolidado), ct);
        var rInexistente = await _entregador.EntregarAsync(new LoteItemEntregaRequest(Guid.NewGuid(), borrado.TenantId, LoteTipoDocumento.Consolidado), ct);

        rBorrado.Should().Be(LoteItemEntregaResult.Omitido(ConsolidadoLoteOmisiones.AccesoRevocado));
        rInexistente.Should().Be(LoteItemEntregaResult.Omitido(ConsolidadoLoteOmisiones.AccesoRevocado));
    }

    [Fact]
    public async Task AC4_Edge_ErrorDePrecondicionDelGenerador_SeOmiteConSuCodigo()
    {
        var ct = TestContext.Current.CancellationToken;
        // Maestro de un trámite no final sin ningún documento: el generador responde sin_adjuntos.
        var instance = Instancia(TramiteEstado.Borrador, conFur: false, conFactura: false);
        Wire(instance);

        var r = await _entregador.EntregarAsync(new LoteItemEntregaRequest(instance.Id, instance.TenantId, LoteTipoDocumento.ConsolidadoMaestro), ct);

        r.Should().Be(LoteItemEntregaResult.Omitido(ConsolidadoLoteOmisiones.SinAdjuntos));
    }

    [Fact]
    public async Task AC4_Edge_FalloDeAlmacenamiento_EsErrorTecnico_NoOmision()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instancia(TramiteEstado.Entregado);
        _storage.FallarAlGuardar = true;
        Wire(instance);

        var r = await _entregador.EntregarAsync(new LoteItemEntregaRequest(instance.Id, instance.TenantId, LoteTipoDocumento.Consolidado), ct);

        r.Should().Be(LoteItemEntregaResult.Fallo("storage_unavailable"));
    }

    [Theory]
    [InlineData("fur_requerido", LoteItemEntregaEstado.Omitido, "fur_requerido")]
    [InlineData("migrado_solo_lectura", LoteItemEntregaEstado.Omitido, "migrado_solo_lectura")]
    [InlineData("sin_adjuntos", LoteItemEntregaEstado.Omitido, "sin_adjuntos")]
    [InlineData("adjunto_no_disponible", LoteItemEntregaEstado.Omitido, "adjunto_no_disponible")]
    [InlineData("mimetype_no_soportado", LoteItemEntregaEstado.Omitido, "mimetype_no_soportado")]
    [InlineData("organismo_requerido", LoteItemEntregaEstado.Omitido, "organismo_requerido")]
    [InlineData("modalidad_no_soportada", LoteItemEntregaEstado.Omitido, "modalidad_no_soportada")]
    [InlineData("not_found", LoteItemEntregaEstado.Omitido, "acceso_revocado")]
    [InlineData("consolidado_no_generado", LoteItemEntregaEstado.Omitido, "adjunto_no_disponible")]
    [InlineData("storage_unavailable", LoteItemEntregaEstado.ErrorTecnico, "storage_unavailable")]
    [InlineData("codigo_desconocido", LoteItemEntregaEstado.ErrorTecnico, "codigo_desconocido")]
    public void AC4_Contrato_ClasificacionDeErroresDelGenerador(string error, LoteItemEntregaEstado estado, string codigo)
    {
        var r = ConsolidadoLoteEntregador.Clasificar(error);

        r.Estado.Should().Be(estado);
        r.Codigo.Should().Be(codigo);
        if (estado == LoteItemEntregaEstado.Omitido)
            ConsolidadoLoteOmisiones.EsValido(r.Codigo).Should().BeTrue("toda omisión cabe en el CHECK del DDL");
    }

    [Fact]
    public void AC4_Contrato_VocabularioDeOmision_EsExactamenteElDelDdl()
    {
        ConsolidadoLoteOmisiones.Todos.Should().Equal(
            "fur_requerido", "migrado_solo_lectura", "sin_adjuntos", "adjunto_no_disponible", "mimetype_no_soportado",
            "organismo_requerido", "modalidad_no_soportada", "quipux_solo_lectura", "acceso_revocado", "error_tecnico",
            "red_sin_consolidado");
        ConsolidadoLoteOmisiones.EsValido("en_regeneracion").Should().BeFalse("v2 eliminó ese motivo");
        ConsolidadoLoteOmisiones.EsValido(null).Should().BeFalse();
    }

    [Fact]
    public async Task Contrato_TipoNoAdmitido_Lanza()
    {
        var act = () => _entregador.EntregarAsync(new LoteItemEntregaRequest(Guid.NewGuid(), Guid.NewGuid(), "fur"));

        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ── AC6 — carrera con una generación simultánea ─────────────────────────────────────────────

    [Fact]
    public async Task AC6_OtroFlujoLoCreaEntreLecturaYGeneracion_SeTomaEseComoExistente_SinGenerarOtro()
    {
        var ct = TestContext.Current.CancellationToken;
        var lectura = Instancia(TramiteEstado.Entregado);
        var grafo = Clonar(lectura);
        var ganador = AddAttachment(grafo, "consolidado", "del_otro_flujo.pdf", "system");
        _repo.GetByIdWithAttachmentsAsync(lectura.Id, lectura.TenantId, Arg.Any<CancellationToken>()).Returns(lectura, grafo);
        _repo.GetByIdWithChecklistGraphAsync(lectura.Id, lectura.TenantId, Arg.Any<CancellationToken>()).Returns(grafo);

        var r = await _entregador.EntregarAsync(new LoteItemEntregaRequest(lectura.Id, lectura.TenantId, LoteTipoDocumento.Consolidado), ct);

        r.Estado.Should().Be(LoteItemEntregaEstado.Incluido);
        r.DeliveryMode.Should().Be(LoteDeliveryMode.Existente, "Regenerado=false del generador ⇒ existente");
        r.Adjunto!.AttachmentId.Should().Be(ganador.Id);
        _storage.Saved.Should().BeEmpty();
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC6_Edge_ConflictoDeConcurrenciaAlGuardar_SeTomaElQueQuedo()
    {
        var ct = TestContext.Current.CancellationToken;
        var lectura = Instancia(TramiteEstado.Entregado);
        var tras = Clonar(lectura);
        var ganador = AddAttachment(tras, "consolidado", "ganador.pdf", "system");
        _repo.GetByIdWithAttachmentsAsync(lectura.Id, lectura.TenantId, Arg.Any<CancellationToken>()).Returns(lectura, tras);
        _repo.GetByIdWithChecklistGraphAsync(lectura.Id, lectura.TenantId, Arg.Any<CancellationToken>()).Returns(Clonar(lectura));
        var conflicto = new InvalidOperationException("conflicto simulado");
        _repo.SaveChangesAsync(Arg.Any<CancellationToken>()).ThrowsAsync(conflicto);
        _repo.IsConcurrencyConflict(conflicto).Returns(true);

        var r = await _entregador.EntregarAsync(new LoteItemEntregaRequest(lectura.Id, lectura.TenantId, LoteTipoDocumento.Consolidado), ct);

        r.Estado.Should().Be(LoteItemEntregaEstado.Incluido);
        r.DeliveryMode.Should().Be(LoteDeliveryMode.Existente);
        r.Adjunto!.AttachmentId.Should().Be(ganador.Id);
    }

    // ── AC8 — maestro en estado final sin maestro y sin FUR ─────────────────────────────────────

    [Theory]
    [InlineData(TramiteEstado.Aprobado)]
    [InlineData(TramiteEstado.Anulado)]
    [InlineData(TramiteEstado.Revocado)]
    public async Task AC8_MaestroEnFinal_SinMaestro_SinFur_SeOmite_FurRequerido_SinPdf(string estado)
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instancia(estado, conFur: false);
        Wire(instance);

        var r = await _entregador.EntregarAsync(new LoteItemEntregaRequest(instance.Id, instance.TenantId, LoteTipoDocumento.ConsolidadoMaestro), ct);

        r.Should().Be(LoteItemEntregaResult.Omitido(ConsolidadoLoteOmisiones.FurRequerido));
        _storage.Saved.Should().BeEmpty("no se genera ningún PDF");
        instance.Attachments.Should().NotContain(a => a.Tipo == "consolidado_maestro" || a.Tipo == "fur");
        await _hotDocs.DidNotReceive().RegenerateHotDocumentsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC8_Edge_MaestroRadicadoAnteQuipux_SeEntregaElRadicado_AunqueHayaUnoMasReciente()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instancia(TramiteEstado.Aprobado);
        var radicado = AddAttachment(instance, "consolidado_maestro", "radicado.pdf", "system");
        radicado.UploadedAt = DateTimeOffset.UtcNow.AddDays(-1);
        AddAttachment(instance, "consolidado_maestro", "posterior.pdf", "system");
        _radicado.AttachmentRadicadoAsync(instance.TenantId, instance.Id, Arg.Any<CancellationToken>()).Returns(radicado.Id);
        Wire(instance);

        var r = await _entregador.EntregarAsync(new LoteItemEntregaRequest(instance.Id, instance.TenantId, LoteTipoDocumento.ConsolidadoMaestro), ct);

        r.Adjunto!.AttachmentId.Should().Be(radicado.Id);
        r.DeliveryMode.Should().Be(LoteDeliveryMode.Existente);
    }

    // ── AC9 — gancho antes de generar y precedencia de matriz ───────────────────────────────────

    [Theory]
    [MemberData(nameof(AmbosTipos))]
    public async Task AC9_ConExistente_ElGanchoNoSeInvoca(string tipo)
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instancia(TramiteEstado.Entregado);
        AddAttachment(instance, tipo, "existente.pdf", "system");
        Wire(instance);
        var invocaciones = 0;

        var r = await _entregador.EntregarAsync(
            new LoteItemEntregaRequest(instance.Id, instance.TenantId, tipo, ["factura"], _ =>
            {
                invocaciones++;
                return Task.FromResult<string?>(null);
            }), ct);

        r.DeliveryMode.Should().Be(LoteDeliveryMode.Existente);
        invocaciones.Should().Be(0);
    }

    [Fact]
    public async Task AC9_HayQueGenerar_ElGanchoSeInvocaAntesDeLaGeneracion()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instancia(TramiteEstado.Entregado);
        Wire(instance);
        int? subidosAlInvocar = null;

        var r = await _entregador.EntregarAsync(
            new LoteItemEntregaRequest(instance.Id, instance.TenantId, LoteTipoDocumento.ConsolidadoMaestro, null, _ =>
            {
                subidosAlInvocar = _storage.Saved.Count;
                return Task.FromResult<string?>(null);
            }), ct);

        subidosAlInvocar.Should().Be(0, "el gancho corre antes de generar");
        r.DeliveryMode.Should().Be(LoteDeliveryMode.Generado);
        _storage.Saved.Should().ContainSingle();
    }

    [Fact]
    public async Task AC9_GanchoRechaza_SeOmiteConSuMotivo_YNoSeGeneraNada()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instancia(TramiteEstado.Entregado);
        Wire(instance);

        var r = await _entregador.EntregarAsync(
            new LoteItemEntregaRequest(instance.Id, instance.TenantId, LoteTipoDocumento.ConsolidadoMaestro, ["factura"],
                _ => Task.FromResult<string?>(ConsolidadoLoteOmisiones.QuipuxSoloLectura)), ct);

        r.Should().Be(LoteItemEntregaResult.Omitido(ConsolidadoLoteOmisiones.QuipuxSoloLectura));
        _storage.Saved.Should().BeEmpty();
        await _repo.DidNotReceive().GetByIdWithChecklistGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC9_Edge_GanchoNoSeInvoca_SiAntesSeOmitePorFurEnFinal()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instancia(TramiteEstado.Aprobado, conFur: false);
        Wire(instance);
        var invocado = false;

        var r = await _entregador.EntregarAsync(
            new LoteItemEntregaRequest(instance.Id, instance.TenantId, LoteTipoDocumento.ConsolidadoMaestro, null, _ =>
            {
                invocado = true;
                return Task.FromResult<string?>(null);
            }), ct);

        r.Codigo.Should().Be(ConsolidadoLoteOmisiones.FurRequerido);
        invocado.Should().BeFalse();
    }

    [Fact]
    public async Task AC9_Contrato_GanchoConMotivoFueraDelVocabulario_Lanza()
    {
        var instance = Instancia(TramiteEstado.Entregado);
        Wire(instance);

        var act = () => _entregador.EntregarAsync(
            new LoteItemEntregaRequest(instance.Id, instance.TenantId, LoteTipoDocumento.ConsolidadoMaestro, null,
                _ => Task.FromResult<string?>("otro_motivo")));

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task AC9_Edge_GanchoLanza_EsErrorTecnico()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instancia(TramiteEstado.Entregado);
        Wire(instance);

        var r = await _entregador.EntregarAsync(
            new LoteItemEntregaRequest(instance.Id, instance.TenantId, LoteTipoDocumento.ConsolidadoMaestro, null,
                _ => throw new TimeoutException()), ct);

        r.Should().Be(LoteItemEntregaResult.Fallo(ConsolidadoLoteEntregador.CausaExcepcion));
        _storage.Saved.Should().BeEmpty();
    }

    [Theory]
    [InlineData("soat", "factura")]
    [InlineData("factura", "soat")]
    public async Task AC9_PrecedenciaDeMatriz_OrdenaElMaestroGenerado(string primero, string segundo)
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instancia(TramiteEstado.Entregado);
        AddAttachment(instance, "soat", "soat.pdf", "user");
        Wire(instance);

        await _entregador.EntregarAsync(
            new LoteItemEntregaRequest(instance.Id, instance.TenantId, LoteTipoDocumento.ConsolidadoMaestro, [primero, segundo]), ct);

        var evento = instance.Events.Single(e => e.Tipo == "consolidado_maestro_generado");
        var paginas = JsonDocument.Parse(evento.Payload).RootElement.GetProperty("paginas_incluidas")
            .EnumerateArray().Select(e => e.GetString()).ToList();
        paginas.IndexOf(primero).Should().BeLessThan(paginas.IndexOf(segundo), "manda la matriz recibida");
    }

    // ── Infraestructura del test ────────────────────────────────────────────────────────────────

    private void Wire(ProcedureInstance instance)
    {
        _repo.GetByIdWithAttachmentsAsync(instance.Id, instance.TenantId, Arg.Any<CancellationToken>()).Returns(instance);
        _repo.GetByIdWithChecklistGraphAsync(instance.Id, instance.TenantId, Arg.Any<CancellationToken>()).Returns(instance);
    }

    private ProcedureInstance Instancia(string estado, bool conFur = true, bool conFactura = true)
    {
        var instance = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.For("matricula_inicial"),
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-013371",
            Status = estado,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        if (conFur)
            AddAttachment(instance, "fur", "fur.pdf", "system").UploadedAt = DateTimeOffset.UtcNow.AddDays(-3);
        if (conFactura)
            AddAttachment(instance, "factura", "factura.pdf", "user");
        return instance;
    }

    private static ProcedureInstance Clonar(ProcedureInstance origen)
    {
        var copia = new ProcedureInstance
        {
            ProcedureType = origen.ProcedureType,
            Id = origen.Id,
            TenantId = origen.TenantId,
            ProcedureTypeId = origen.ProcedureTypeId,
            ReferenceNumber = origen.ReferenceNumber,
            Status = origen.Status,
            CreatedAt = origen.CreatedAt,
        };
        foreach (var a in origen.Attachments)
            copia.Attachments.Add(a);
        return copia;
    }

    private ProcedureInstanceAttachment AddAttachment(ProcedureInstance instance, string tipo, string filename, string source)
    {
        var path = $"{instance.Id:D}/{tipo}-{Guid.NewGuid():N}";
        var content = System.Text.Encoding.UTF8.GetBytes($"%PDF-{filename}");
        _storage.Files[path] = content;
        var attachment = new ProcedureInstanceAttachment
        {
            Id = Guid.NewGuid(),
            TenantId = instance.TenantId,
            ProcedureInstanceId = instance.Id,
            Tipo = tipo,
            Filename = filename,
            Mimetype = "application/pdf",
            SizeBytes = content.Length,
            Sha256 = $"sha-{tipo}-{Guid.NewGuid():N}",
            StoragePath = path,
            Source = source,
            UploadedAt = DateTimeOffset.UtcNow,
        };
        instance.Attachments.Add(attachment);
        return attachment;
    }
}
