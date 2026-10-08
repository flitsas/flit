using Flit.Tramites.Application.UseCases.ConsolidadoLotes;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Entities.ConsolidadoLotes;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.ConsolidadoLotes;

/// <summary>
/// HU #13384 (Épica #13216, CF-05, CF-06) — el lote del Super Admin procesado de punta a punta en la capa de
/// aplicación: <see cref="ProcesarItemLoteHandler"/> + <see cref="SuperAdminLoteItemOrigen"/> + el
/// <see cref="ConsolidadoLoteEntregador"/> REAL con los generadores REALES
/// (<see cref="GenerarConsolidadoHandler"/> / <see cref="GenerarConsolidadoMaestroHandler"/>). Solo son dobles el
/// repositorio de trámites, el almacenamiento, el lookup del maestro radicado, la revalidación de acceso (la real
/// está en <c>AccessCheckerSuperAdminTests</c>) y la persistencia del ítem.
/// <para>Uso de ejemplo:
/// <c>var r = await handler.HandleAsync(ProcesarItemLoteCommand.Con(loteSuperAdmin, item, settings), ct);</c> ⇒ el
/// repositorio y el generador reciben <c>item.TenantId</c>; con <c>consolidado_maestro</c> se entrega el maestro
/// guardado (el radicado primero) y solo se genera, con matriz nula y sin gancho, si no hay ninguno.</para>
/// </summary>
public sealed class ConsolidadoLoteEntregadorMaestroSuperAdminTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 10, 7, 18, 0, 0, TimeSpan.Zero);

    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly LoteFakeStorage _storage = new();
    private readonly IExpedienteHotDocumentsRegenerator _hotDocs = Substitute.For<IExpedienteHotDocumentsRegenerator>();
    private readonly IMaestroRadicadoLookup _radicado = Substitute.For<IMaestroRadicadoLookup>();
    private readonly IConsolidadoLoteAccessChecker _acceso = Substitute.For<IConsolidadoLoteAccessChecker>();
    private readonly IConsolidadoLoteItemProceso _proceso = Substitute.For<IConsolidadoLoteItemProceso>();
    private readonly EntregadorEspia _entregador;
    private readonly ProcesarItemLoteHandler _handler;

    /// <summary>Compañía de la sesión del Super Admin (<c>X-Tenant-Id</c> → <c>scope_tenant_id</c>): nunca se usa al procesar.</summary>
    private readonly Guid _scopeTenant = Guid.NewGuid();

    public ConsolidadoLoteEntregadorMaestroSuperAdminTests()
    {
        var merger = new LoteFakeMerger();
        _radicado.AttachmentRadicadoAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Guid?)null);
        _radicado.AttachmentsProtegidosAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlySet<Guid>)new HashSet<Guid>());
        _acceso.TieneAccesoAsync(Arg.Any<LoteItemContexto>(), Arg.Any<CancellationToken>()).Returns(true);
        _proceso.MarcarIncluidoAsync(Arg.Any<LoteItemIncluido>(), Arg.Any<CancellationToken>()).Returns(true);
        _proceso.MarcarOmitidoAsync(Arg.Any<LoteItemOmitido>(), Arg.Any<CancellationToken>()).Returns(true);
        _proceso.ReprogramarAsync(Arg.Any<LoteItemReintento>(), Arg.Any<CancellationToken>()).Returns(true);

        _entregador = new EntregadorEspia(new ConsolidadoLoteEntregador(
            _repo,
            new GenerarConsolidadoHandler(_repo, merger, _storage, hotDocsRegenerator: _hotDocs),
            new GenerarConsolidadoMaestroHandler(_repo, merger, _storage, maestroRadicado: _radicado, hotDocsRegenerator: _hotDocs),
            maestroRadicado: _radicado));
        _handler = new ProcesarItemLoteHandler(
            new LoteItemOrigenPorOrigen([new SuperAdminLoteItemOrigen(_acceso, _entregador)]),
            _proceso,
            NullLogger<ProcesarItemLoteHandler>.Instance,
            LotesEnEstado.Con(ConsolidadoExportStatus.EnProceso),
            new RelojFijo(Ahora));
    }

    // ── AC1 — contexto por compañía ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC1_LoteConTramitesDeAyB_RepositorioYGeneradorRecibenSiempreLaCompaniaDelItem()
    {
        var ct = TestContext.Current.CancellationToken;
        var lote = LoteSuperAdmin();
        var deA = Instancia(TramiteEstado.Aprobado);                 // sin maestro: se genera con la compañía A
        var deB = Instancia(TramiteEstado.Entregado);
        AddAttachment(deB, LoteTipoDocumento.ConsolidadoMaestro, "maestro_b.pdf", "system"); // existente en B
        Wire(deA);
        Wire(deB);
        var companiaPorTramite = new Dictionary<Guid, Guid> { [deA.Id] = deA.TenantId, [deB.Id] = deB.TenantId };

        var rA = await _handler.HandleAsync(Cmd(lote, Item(lote, deA)), ct);
        var rB = await _handler.HandleAsync(Cmd(lote, Item(lote, deB)), ct);

        rA.DeliveryMode.Should().Be(LoteDeliveryMode.Generado);
        rB.DeliveryMode.Should().Be(LoteDeliveryMode.Existente);

        // Entregador y revalidación: la compañía de cada ítem.
        _entregador.Peticiones.Select(p => (p.ProcedureInstanceId, p.TenantId))
            .Should().Equal((deA.Id, deA.TenantId), (deB.Id, deB.TenantId));
        await _acceso.Received(1).TieneAccesoAsync(
            Arg.Is<LoteItemContexto>(c => c.ProcedureInstanceId == deA.Id && c.CompaniaTramiteId == deA.TenantId && c.CompaniaLoteId == null),
            Arg.Any<CancellationToken>());

        // El generador sí corrió para A, y con A.
        await _repo.Received().GetByIdWithChecklistGraphAsync(deA.Id, deA.TenantId, Arg.Any<CancellationToken>());
        await _radicado.Received().AttachmentRadicadoAsync(deA.TenantId, deA.Id, Arg.Any<CancellationToken>());

        // Ninguna llamada al repositorio ni al lookup del radicado lleva otra compañía que la del trámite del ítem,
        // ni el scope_tenant_id del Super Admin.
        var llamadas = _repo.ReceivedCalls().Concat(_radicado.ReceivedCalls()).ToList();
        llamadas.Should().NotBeEmpty();
        foreach (var llamada in llamadas)
        {
            var guids = llamada.GetArguments().OfType<Guid>().ToList();
            guids.Should().NotContain(_scopeTenant, $"{llamada.GetMethodInfo().Name} no usa el scope_tenant_id");
            var tramite = guids.FirstOrDefault(companiaPorTramite.ContainsKey);
            if (tramite != Guid.Empty)
                guids.Where(g => g != tramite).Should().OnlyContain(g => g == companiaPorTramite[tramite],
                    $"{llamada.GetMethodInfo().Name} recibe la compañía del trámite del ítem");
        }
    }

    // ── AC2 — ítem con compañía manipulada (defensa en profundidad del entregador) ─────────────

    [Fact]
    public async Task AC2_ItemConCompaniaBYTramiteDeA_AunqueLaRevalidacionPasara_SeOmiteYNoSeGeneraConB()
    {
        var ct = TestContext.Current.CancellationToken;
        var lote = LoteSuperAdmin();
        var deA = Instancia(TramiteEstado.Aprobado);
        Wire(deA);
        var companiaB = Guid.NewGuid();
        var manipulado = Item(lote, deA, tenantId: companiaB);

        var r = await _handler.HandleAsync(Cmd(lote, manipulado), ct);

        r.Desenlace.Should().Be(ProcesarItemLoteDesenlace.Omitido);
        r.Codigo.Should().Be(ConsolidadoLoteOmisiones.AccesoRevocado);
        r.Motivo.Should().Be("Acceso revocado");
        _entregador.Peticiones.Should().ContainSingle().Which.TenantId.Should().Be(companiaB);
        await _repo.DidNotReceive().GetByIdWithChecklistGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _radicado.DidNotReceive().AttachmentRadicadoAsync(companiaB, Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        _storage.Saved.Should().BeEmpty("el generador nunca corre con la compañía B");
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ── AC3 — maestro guardado se entrega tal cual ─────────────────────────────────────────────

    [Fact]
    public async Task AC3_MaestroDesactualizado_SeEntregaElMismoIdYSha_Existente_SinTocarFurConsolidadoNiAdjuntos()
    {
        var ct = TestContext.Current.CancellationToken;
        var lote = LoteSuperAdmin();
        var t = Instancia(TramiteEstado.Aprobado);
        AddAttachment(t, LoteTipoDocumento.Consolidado, "consolidado.pdf", "system");
        var maestro = AddAttachment(t, LoteTipoDocumento.ConsolidadoMaestro, "maestro.pdf", "system");
        t.ConsolidadoMaestroVigente = false;
        t.ConsolidadoWizardVigente = false;
        t.ExpedienteActualizadoEn = Ahora.AddHours(1);
        var antes = t.Attachments.Select(a => (a.Id, a.Tipo, a.Sha256, a.StoragePath, a.UploadedAt)).ToList();
        Wire(t);
        var item = Item(lote, t);

        var r = await _handler.HandleAsync(Cmd(lote, item), ct);

        r.Desenlace.Should().Be(ProcesarItemLoteDesenlace.Incluido);
        r.DeliveryMode.Should().Be(LoteDeliveryMode.Existente);
        await _proceso.Received(1).MarcarIncluidoAsync(
            new LoteItemIncluido(lote.Id, item.Id,
                new LoteItemAdjunto(maestro.Id, maestro.StoragePath, maestro.SizeBytes, maestro.Sha256, maestro.Filename),
                LoteDeliveryMode.Existente, Ahora),
            Arg.Any<CancellationToken>());
        t.Attachments.Select(a => (a.Id, a.Tipo, a.Sha256, a.StoragePath, a.UploadedAt)).Should().Equal(antes,
            "FUR, consolidado y adjuntos del trámite no cambian");
        t.ConsolidadoMaestroVigente.Should().BeFalse("la marca de vigencia no se toca");
        t.Events.Should().BeEmpty();
        _storage.Saved.Should().BeEmpty();
        _storage.Deleted.Should().BeEmpty();
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _hotDocs.DidNotReceive().RegenerateHotDocumentsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    // ── AC4 — maestro radicado ante Quipux ──────────────────────────────────────────────────────

    [Fact]
    public async Task AC4_MaestroRadicadoYOtroPosterior_SeEntregaElRadicado()
    {
        var ct = TestContext.Current.CancellationToken;
        var lote = LoteSuperAdmin();
        var t = Instancia(TramiteEstado.Aprobado);
        var radicado = AddAttachment(t, LoteTipoDocumento.ConsolidadoMaestro, "radicado.pdf", "system");
        radicado.UploadedAt = Ahora.AddDays(-2);
        var posterior = AddAttachment(t, LoteTipoDocumento.ConsolidadoMaestro, "posterior.pdf", "system");
        _radicado.AttachmentRadicadoAsync(t.TenantId, t.Id, Arg.Any<CancellationToken>()).Returns(radicado.Id);
        Wire(t);
        var item = Item(lote, t);

        var r = await _handler.HandleAsync(Cmd(lote, item), ct);

        r.Desenlace.Should().Be(ProcesarItemLoteDesenlace.Incluido);
        r.DeliveryMode.Should().Be(LoteDeliveryMode.Existente);
        await _proceso.Received(1).MarcarIncluidoAsync(
            Arg.Is<LoteItemIncluido>(i => i.Adjunto.AttachmentId == radicado.Id && i.Adjunto.Sha256 == radicado.Sha256),
            Arg.Any<CancellationToken>());
        await _proceso.DidNotReceive().MarcarIncluidoAsync(
            Arg.Is<LoteItemIncluido>(i => i.Adjunto.AttachmentId == posterior.Id), Arg.Any<CancellationToken>());
        _storage.Saved.Should().BeEmpty();
    }

    // ── AC5 — trámite sin maestro ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC5_AprobadoSinMaestroConFur_SeGeneraUnaSolaVez_MatrizNula_SinGancho_YQuedaComoOficial()
    {
        var ct = TestContext.Current.CancellationToken;
        var lote = LoteSuperAdmin();
        var t = Instancia(TramiteEstado.Aprobado);
        var fur = t.Attachments.Single(a => a.Tipo == "fur");
        var furAntes = (fur.Id, fur.Sha256, fur.UploadedAt);
        Wire(t);
        var item = Item(lote, t);

        var r1 = await _handler.HandleAsync(Cmd(lote, item), ct);
        var r2 = await _handler.HandleAsync(Cmd(lote, item), ct); // reintento tras la generación ya persistida

        r1.DeliveryMode.Should().Be(LoteDeliveryMode.Generado);
        r2.DeliveryMode.Should().Be(LoteDeliveryMode.Existente, "soloSiNoExiste: la segunda vez toma el que quedó");
        _storage.Saved.Should().ContainSingle("se genera una sola vez");
        _entregador.Peticiones.Should().HaveCount(2).And.OnlyContain(p =>
            p.TipoDocumento == LoteTipoDocumento.ConsolidadoMaestro
            && p.PrecedenciaMatriz == null
            && p.AntesDeGenerar == null,
            "el origen superadmin no resuelve matriz ni pasa el guard de Quipux de solo lectura (eso es de ot_bandeja)");
        var maestro = t.Attachments.Should().ContainSingle(a => a.Tipo == LoteTipoDocumento.ConsolidadoMaestro).Which;
        t.ConsolidadoMaestroVigente.Should().BeTrue("el maestro generado queda como el oficial del trámite");
        t.Events.Should().ContainSingle(e => e.Tipo == "consolidado_maestro_generado");
        await _proceso.Received(1).MarcarIncluidoAsync(
            Arg.Is<LoteItemIncluido>(i => i.Adjunto.AttachmentId == maestro.Id && i.DeliveryMode == LoteDeliveryMode.Generado),
            Arg.Any<CancellationToken>());
        await _proceso.Received(1).MarcarIncluidoAsync(
            Arg.Is<LoteItemIncluido>(i => i.Adjunto.AttachmentId == maestro.Id && i.DeliveryMode == LoteDeliveryMode.Existente),
            Arg.Any<CancellationToken>());
        var furDespues = t.Attachments.Single(a => a.Tipo == "fur");
        (furDespues.Id, furDespues.Sha256, furDespues.UploadedAt).Should().Be(furAntes, "el lote no re-genera el FUR");
        await _hotDocs.DidNotReceive().RegenerateHotDocumentsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    // ── AC6 — excepciones en el maestro ─────────────────────────────────────────────────────────

    public static TheoryData<string, bool, bool, string, string> Excepciones() => new()
    {
        // estado, migrado, conFur, código, texto del catálogo
        { TramiteEstado.Aprobado, true, true, ConsolidadoLoteOmisiones.MigradoSoloLectura, "Trámite migrado sin consolidado" },
        { TramiteEstado.Anulado, true, false, ConsolidadoLoteOmisiones.MigradoSoloLectura, "Trámite migrado sin consolidado" },
        { TramiteEstado.Aprobado, false, false, ConsolidadoLoteOmisiones.FurRequerido, "El trámite no tiene FUR; no se pudo generar el consolidado" },
        { TramiteEstado.Revocado, false, false, ConsolidadoLoteOmisiones.FurRequerido, "El trámite no tiene FUR; no se pudo generar el consolidado" },
    };

    [Theory]
    [MemberData(nameof(Excepciones))]
    public async Task AC6_EstadoFinalSinMaestro_MigradoOSinFur_SeOmiteConElTextoDelCatalogo_SinGenerar(
        string estado, bool migrado, bool conFur, string codigo, string texto)
    {
        var ct = TestContext.Current.CancellationToken;
        var lote = LoteSuperAdmin();
        var t = Instancia(estado, conFur: conFur);
        t.IsMigrated = migrado;
        Wire(t);
        var item = Item(lote, t);

        var r = await _handler.HandleAsync(Cmd(lote, item), ct);

        r.Desenlace.Should().Be(ProcesarItemLoteDesenlace.Omitido);
        r.Codigo.Should().Be(codigo);
        r.Motivo.Should().Be(texto).And.Be(ConsolidadoErrorTextos.ParaLote(codigo));
        await _proceso.Received(1).MarcarOmitidoAsync(
            new LoteItemOmitido(lote.Id, item.Id, codigo, texto, 0, Ahora), Arg.Any<CancellationToken>());
        _storage.Saved.Should().BeEmpty();
        t.Attachments.Should().NotContain(a => a.Tipo == LoteTipoDocumento.ConsolidadoMaestro);
        await _repo.DidNotReceive().GetByIdWithChecklistGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC6_GeneracionDelMaestroFallariaPorOtraValidacion_SeOmiteConElTextoDelCatalogo()
    {
        var ct = TestContext.Current.CancellationToken;
        var lote = LoteSuperAdmin();
        // No final y sin ningún documento: el generador del maestro responde sin_adjuntos (validación de la entrega individual).
        var t = Instancia(TramiteEstado.Borrador, conFur: false, conFactura: false);
        Wire(t);
        var item = Item(lote, t);

        var r = await _handler.HandleAsync(Cmd(lote, item), ct);

        r.Desenlace.Should().Be(ProcesarItemLoteDesenlace.Omitido);
        r.Codigo.Should().Be(ConsolidadoLoteOmisiones.SinAdjuntos);
        r.Motivo.Should().Be(ConsolidadoErrorTextos.ParaLote(ConsolidadoLoteOmisiones.SinAdjuntos)).And.NotBeNullOrWhiteSpace();
        r.Intentos.Should().Be(0, "una omisión de negocio no consume intentos");
        _storage.Saved.Should().BeEmpty();
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private ConsolidadoExportBatch LoteSuperAdmin() => new()
    {
        Id = Guid.NewGuid(),
        TenantId = null,                 // E5: el lote del Super Admin no tiene compañía
        ScopeTenantId = _scopeTenant,    // la de su sesión: solo informativa
        RequestedByUserId = Guid.NewGuid(),
        RequestedRoleCode = "SuperAdmin",
        Origin = ConsolidadoExportOrigin.Superadmin,
        DocumentType = ConsolidadoExportDocumentType.ConsolidadoMaestro,
        Status = ConsolidadoExportStatus.EnProceso,
    };

    private static ConsolidadoExportBatchItem Item(ConsolidadoExportBatch lote, ProcedureInstance t, Guid? tenantId = null) => new()
    {
        Id = Guid.NewGuid(),
        BatchId = lote.Id,
        TenantId = tenantId ?? t.TenantId,
        ProcedureInstanceId = t.Id,
        Status = ConsolidadoExportItemStatus.Procesando,
        ReferenceNumber = "TRM-2026-013384",
        LeaseUntil = Ahora.AddMinutes(10),
    };

    private static ProcesarItemLoteCommand Cmd(ConsolidadoExportBatch lote, ConsolidadoExportBatchItem item) =>
        ProcesarItemLoteCommand.Con(lote, item, new ConsolidadoExportSettings { MaxItemAttempts = 3, RetryDelaySeconds = 30 });

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
            ReferenceNumber = "TRM-2026-013384",
            Status = estado,
            CreatedAt = Ahora,
        };

        if (conFur)
            AddAttachment(instance, "fur", "fur.pdf", "system").UploadedAt = Ahora.AddDays(-3);
        if (conFactura)
            AddAttachment(instance, "factura", "factura.pdf", "user");
        return instance;
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
            UploadedAt = Ahora,
        };
        instance.Attachments.Add(attachment);
        return attachment;
    }

    /// <summary>Delega en el entregador real y registra cada petición (compañía, tipo, matriz, gancho).</summary>
    private sealed class EntregadorEspia(ILoteItemEntregador real) : ILoteItemEntregador
    {
        public List<LoteItemEntregaRequest> Peticiones { get; } = [];

        public Task<LoteItemEntregaResult> EntregarAsync(LoteItemEntregaRequest request, CancellationToken ct = default)
        {
            Peticiones.Add(request);
            return real.EntregarAsync(request, ct);
        }
    }

    private sealed class RelojFijo(DateTimeOffset ahora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => ahora;
    }
}
