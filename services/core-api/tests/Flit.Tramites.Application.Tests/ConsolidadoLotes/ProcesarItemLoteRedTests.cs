using Flit.Tramites.Application.UseCases.ConsolidadoLotes;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Entities.ConsolidadoLotes;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Flit.Tramites.Application.Tests.ConsolidadoLotes;

/// <summary>
/// HU #13418 (épica #13216, ADR-0070 adenda v7, P1 = a) — procesamiento de un ítem de un lote de RED: el handler real
/// (<see cref="ProcesarItemLoteHandler"/>), el origen real (<see cref="TramitesLoteItemOrigen"/>) y el entregador REAL
/// (<see cref="ConsolidadoLoteEntregador"/>) con los generadores oficiales sobre un repositorio sustituto, para afirmar
/// que en el trámite de una hija no se escribe nada: ni adjunto, ni bitácora (eventos), ni impronta.
/// <list type="bullet">
///   <item>AC1 — hija con consolidado ⇒ <c>incluido</c> / <c>existente</c>, el trámite de C1 no cambia.</item>
///   <item>AC2 — hija sin consolidado ⇒ <c>omitido</c> <c>red_sin_consolidado</c> «La compañía no ha generado su consolidado».</item>
///   <item>AC3 — trámite propio de la cabeza sin consolidado ⇒ se genera como en el lote del Gestor (<c>generado</c>).</item>
///   <item>AC8 — la revalidación lanza (error de BD) ⇒ reprogramado como fallo técnico, nunca <c>acceso_revocado</c>.</item>
/// </list>
/// <para>Uso de ejemplo:
/// <c>var r = await Handler().HandleAsync(Cmd(LoteDeRed(), ItemDe(C1, tramite)), ct);</c> ⇒ <c>r.Codigo == "red_sin_consolidado"</c>.</para>
/// </summary>
public sealed class ProcesarItemLoteRedTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 10, 7, 15, 0, 0, TimeSpan.Zero);
    private static readonly Guid CabezaP = new("d1000000-0000-4000-8000-000000013418");
    private static readonly Guid HijaC1 = new("d2000000-0000-4000-8000-000000013418");

    private readonly IConsolidadoLoteAccessChecker _acceso = Substitute.For<IConsolidadoLoteAccessChecker>();
    private readonly IConsolidadoLoteItemProceso _proceso = Substitute.For<IConsolidadoLoteItemProceso>();
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly IExpedienteHotDocumentsRegenerator _hotDocs = Substitute.For<IExpedienteHotDocumentsRegenerator>();
    private readonly IImprontaAutoGenerator _impronta = Substitute.For<IImprontaAutoGenerator>();
    private readonly IMaestroRadicadoLookup _radicado = Substitute.For<IMaestroRadicadoLookup>();
    private readonly LoteFakeStorage _storage = new();
    private readonly ConsolidadoLoteEntregador _entregador;

    public ProcesarItemLoteRedTests()
    {
        _acceso.TieneAccesoAsync(Arg.Any<LoteItemContexto>(), Arg.Any<CancellationToken>()).Returns(true);
        _proceso.MarcarIncluidoAsync(Arg.Any<LoteItemIncluido>(), Arg.Any<CancellationToken>()).Returns(true);
        _proceso.MarcarOmitidoAsync(Arg.Any<LoteItemOmitido>(), Arg.Any<CancellationToken>()).Returns(true);
        _proceso.ReprogramarAsync(Arg.Any<LoteItemReintento>(), Arg.Any<CancellationToken>()).Returns(true);
        _radicado.AttachmentRadicadoAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Guid?)null);
        _radicado.AttachmentsProtegidosAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlySet<Guid>)new HashSet<Guid>());

        var merger = new LoteFakeMerger();
        _entregador = new ConsolidadoLoteEntregador(
            _repo,
            new GenerarConsolidadoHandler(_repo, merger, _storage, hotDocsRegenerator: _hotDocs, improntaGenerator: _impronta),
            new GenerarConsolidadoMaestroHandler(_repo, merger, _storage, maestroRadicado: _radicado, hotDocsRegenerator: _hotDocs),
            maestroRadicado: _radicado);
    }

    public static TheoryData<string> AmbosTipos() =>
        new() { ConsolidadoExportDocumentType.Consolidado, ConsolidadoExportDocumentType.ConsolidadoMaestro };

    // ── AC1 — hija con consolidado ──────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(AmbosTipos))]
    public async Task AC1_HijaConConsolidado_IncluidoExistente_ElTramiteDeC1NoCambia(string tipo)
    {
        var ct = TestContext.Current.CancellationToken;
        var tramite = Tramite(HijaC1, TramiteEstado.Entregado);
        var existente = Adjuntar(tramite, tipo, "consolidado-c1.pdf", "system");
        var antes = tramite.Attachments.Select(a => (a.Id, a.Sha256, a.UploadedAt, a.StoragePath)).ToList();
        Wire(tramite);
        var lote = LoteDeRed(tipo);

        var r = await Handler().HandleAsync(Cmd(lote, ItemDe(lote, tramite)), ct);

        r.Desenlace.Should().Be(ProcesarItemLoteDesenlace.Incluido);
        r.DeliveryMode.Should().Be(LoteDeliveryMode.Existente);
        await _proceso.Received(1).MarcarIncluidoAsync(
            Arg.Is<LoteItemIncluido>(c => c.Adjunto.AttachmentId == existente.Id && c.DeliveryMode == LoteDeliveryMode.Existente),
            Arg.Any<CancellationToken>());
        tramite.Attachments.Select(a => (a.Id, a.Sha256, a.UploadedAt, a.StoragePath)).Should().Equal(antes);
        tramite.Events.Should().BeEmpty("no se escribe bitácora en el trámite de la hija");
        _storage.Saved.Should().BeEmpty();
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _impronta.DidNotReceive().TryGenerateAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    // ── AC2 — hija sin consolidado ──────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(AmbosTipos))]
    public async Task AC2_HijaSinConsolidado_OmitidoRedSinConsolidado_SinAdjuntoBitacoraNiImpronta(string tipo)
    {
        var ct = TestContext.Current.CancellationToken;
        var tramite = Tramite(HijaC1, TramiteEstado.Entregado);
        var antes = tramite.Attachments.Select(a => a.Id).ToList();
        Wire(tramite);
        var lote = LoteDeRed(tipo);

        var r = await Handler().HandleAsync(Cmd(lote, ItemDe(lote, tramite)), ct);

        r.Desenlace.Should().Be(ProcesarItemLoteDesenlace.Omitido);
        r.Codigo.Should().Be(ConsolidadoLoteOmisiones.RedSinConsolidado);
        r.Motivo.Should().Be("La compañía no ha generado su consolidado");
        await _proceso.Received(1).MarcarOmitidoAsync(
            Arg.Is<LoteItemOmitido>(o => o.Codigo == ConsolidadoLoteOmisiones.RedSinConsolidado
                && o.Motivo == "La compañía no ha generado su consolidado"),
            Arg.Any<CancellationToken>());
        tramite.Attachments.Select(a => a.Id).Should().Equal(antes, "no se crea adjunto en el trámite de C1");
        tramite.Events.Should().BeEmpty("no se crea bitácora en el trámite de C1");
        _storage.Saved.Should().BeEmpty();
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _repo.DidNotReceive().GetByIdWithChecklistGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _impronta.DidNotReceive().TryGenerateAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _hotDocs.DidNotReceive().RegenerateHotDocumentsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC2_Edge_LoteAcotadoAUnaHija_TampocoGeneraEnElla()
    {
        var ct = TestContext.Current.CancellationToken;
        var tramite = Tramite(HijaC1, TramiteEstado.Entregado);
        Wire(tramite);
        var lote = LoteDeRed(ConsolidadoExportDocumentType.Consolidado, scope: HijaC1);

        var r = await Handler().HandleAsync(Cmd(lote, ItemDe(lote, tramite)), ct);

        r.Codigo.Should().Be(ConsolidadoLoteOmisiones.RedSinConsolidado);
        _storage.Saved.Should().BeEmpty();
    }

    // ── AC3 — trámite propio de la cabeza sin consolidado ──────────────────────────────────────

    [Fact]
    public async Task AC3_TramitePropioDeLaCabeza_SinConsolidado_SeGeneraComoEnElLoteDelGestor()
    {
        var ct = TestContext.Current.CancellationToken;
        var tramite = Tramite(CabezaP, TramiteEstado.Aprobado);
        Wire(tramite);
        var lote = LoteDeRed(ConsolidadoExportDocumentType.Consolidado);

        var r = await Handler().HandleAsync(Cmd(lote, ItemDe(lote, tramite)), ct);

        r.Desenlace.Should().Be(ProcesarItemLoteDesenlace.Incluido);
        r.DeliveryMode.Should().Be(LoteDeliveryMode.Generado);
        _storage.Saved.Should().ContainSingle("la cabeza sí genera en su propio trámite");
        tramite.Events.Should().ContainSingle(e => e.Tipo == "consolidado_generado");
        await _impronta.DidNotReceive().TryGenerateAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    // ── AC8 — error de BD al revalidar ──────────────────────────────────────────────────────────

    [Fact]
    public async Task AC8_RevalidacionDeRedLanza_SeReprogramaComoFalloTecnico_NuncaAccesoRevocado()
    {
        var ct = TestContext.Current.CancellationToken;
        _acceso.TieneAccesoAsync(Arg.Any<LoteItemContexto>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("lectura de la jerarquía fallida (simulada)"));
        var tramite = Tramite(HijaC1, TramiteEstado.Entregado);
        Wire(tramite);
        var lote = LoteDeRed(ConsolidadoExportDocumentType.Consolidado);

        var r = await Handler().HandleAsync(Cmd(lote, ItemDe(lote, tramite)), ct);

        r.Desenlace.Should().Be(ProcesarItemLoteDesenlace.Reprogramado);
        r.Intentos.Should().Be(1);
        await _proceso.DidNotReceive().MarcarOmitidoAsync(Arg.Any<LoteItemOmitido>(), Arg.Any<CancellationToken>());
        await _proceso.Received(1).ReprogramarAsync(Arg.Any<LoteItemReintento>(), Arg.Any<CancellationToken>());
    }

    // ── Contrato — el contexto lleva la red ─────────────────────────────────────────────────────

    [Fact]
    public void Contrato_ElContextoDelItemLlevaRedActivaYLaHijaAcotada_SoloEnLotesDeRed()
    {
        var tramite = Tramite(HijaC1, TramiteEstado.Entregado);
        var red = LoteDeRed(ConsolidadoExportDocumentType.Consolidado, scope: HijaC1);
        var ctxRed = LoteItemContexto.Desde(red, ItemDe(red, tramite));
        ctxRed.RedActiva.Should().BeTrue();
        ctxRed.ScopeTenantId.Should().Be(HijaC1);
        ctxRed.CompaniaLoteId.Should().Be(CabezaP);
        ctxRed.CompaniaTramiteId.Should().Be(HijaC1);

        // El scope_tenant_id del Super Admin no es vista de red: no viaja como hija acotada.
        var superAdmin = LoteDeRed(ConsolidadoExportDocumentType.Consolidado, scope: HijaC1);
        superAdmin.NetworkScope = false;
        superAdmin.TenantId = null;
        superAdmin.Origin = ConsolidadoExportOrigin.Superadmin;
        var ctxSa = LoteItemContexto.Desde(superAdmin, ItemDe(superAdmin, tramite));
        ctxSa.RedActiva.Should().BeFalse();
        ctxSa.ScopeTenantId.Should().BeNull();
    }

    [Fact]
    public async Task Contrato_LoteDelGestorSinRed_ItemPropioSinConsolidado_SigueGenerando()
    {
        var ct = TestContext.Current.CancellationToken;
        var tramite = Tramite(CabezaP, TramiteEstado.Aprobado);
        Wire(tramite);
        var lote = LoteDeRed(ConsolidadoExportDocumentType.Consolidado);
        lote.NetworkScope = false;

        var r = await Handler().HandleAsync(Cmd(lote, ItemDe(lote, tramite)), ct);

        r.DeliveryMode.Should().Be(LoteDeliveryMode.Generado, "sin red no cambia nada del lote del Gestor (#13371)");
    }

    // ── Infraestructura del test ────────────────────────────────────────────────────────────────

    private ProcesarItemLoteHandler Handler() => new(
        new LoteItemOrigenPorOrigen([new TramitesLoteItemOrigen(_acceso, _entregador)]),
        _proceso,
        NullLogger<ProcesarItemLoteHandler>.Instance,
        LotesEnEstado.Con(ConsolidadoExportStatus.EnProceso),
        new RelojFijo(Ahora));

    private static ProcesarItemLoteCommand Cmd(ConsolidadoExportBatch lote, ConsolidadoExportBatchItem item) =>
        ProcesarItemLoteCommand.Con(lote, item, new ConsolidadoExportSettings { MaxItemAttempts = 3, RetryDelaySeconds = 30 });

    private static ConsolidadoExportBatch LoteDeRed(string tipo, Guid? scope = null) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = CabezaP,
        RequestedByUserId = Guid.NewGuid(),
        RequestedRoleCode = "AdminCompany",
        Origin = ConsolidadoExportOrigin.Tramites,
        DocumentType = tipo,
        Status = ConsolidadoExportStatus.EnProceso,
        NetworkScope = true,
        ScopeTenantId = scope,
    };

    private static ConsolidadoExportBatchItem ItemDe(ConsolidadoExportBatch lote, ProcedureInstance tramite) => new()
    {
        Id = Guid.NewGuid(),
        BatchId = lote.Id,
        TenantId = tramite.TenantId,
        ProcedureInstanceId = tramite.Id,
        Status = ConsolidadoExportItemStatus.Procesando,
        ReferenceNumber = tramite.ReferenceNumber,
        LeaseUntil = Ahora.AddMinutes(10),
    };

    private void Wire(ProcedureInstance tramite)
    {
        _repo.GetByIdWithAttachmentsAsync(tramite.Id, tramite.TenantId, Arg.Any<CancellationToken>()).Returns(tramite);
        _repo.GetByIdWithChecklistGraphAsync(tramite.Id, tramite.TenantId, Arg.Any<CancellationToken>()).Returns(tramite);
    }

    private ProcedureInstance Tramite(Guid compania, string estado)
    {
        var tramite = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.For("matricula_inicial"),
            Id = Guid.NewGuid(),
            TenantId = compania,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-013418",
            Status = estado,
            CreatedAt = Ahora,
        };
        Adjuntar(tramite, "fur", "fur.pdf", "system").UploadedAt = Ahora.AddDays(-3);
        Adjuntar(tramite, "factura", "factura.pdf", "user");
        return tramite;
    }

    private ProcedureInstanceAttachment Adjuntar(ProcedureInstance tramite, string tipo, string filename, string source)
    {
        var path = $"{tramite.Id:D}/{tipo}-{Guid.NewGuid():N}";
        var content = System.Text.Encoding.UTF8.GetBytes($"%PDF-{filename}");
        _storage.Files[path] = content;
        var adjunto = new ProcedureInstanceAttachment
        {
            Id = Guid.NewGuid(),
            TenantId = tramite.TenantId,
            ProcedureInstanceId = tramite.Id,
            Tipo = tipo,
            Filename = filename,
            Mimetype = "application/pdf",
            SizeBytes = content.Length,
            Sha256 = $"sha-{tipo}-{Guid.NewGuid():N}",
            StoragePath = path,
            Source = source,
            UploadedAt = Ahora.AddHours(-1),
        };
        tramite.Attachments.Add(adjunto);
        return adjunto;
    }
}
