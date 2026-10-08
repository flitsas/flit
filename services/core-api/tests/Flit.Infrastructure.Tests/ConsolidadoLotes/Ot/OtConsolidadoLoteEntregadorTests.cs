using Flit.Admin.Domain.DocumentOrderOverrides;
using Flit.Admin.Domain.OtClientProcedures;
using Flit.Admin.Domain.OtProfile;
using Flit.Infrastructure.ConsolidadoLotes.Ot;
using Flit.Infrastructure.OtClientProcedures;
using Flit.Tramites.Application.Documents;
using Flit.Tramites.Application.Storage;
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

namespace Flit.Infrastructure.Tests.ConsolidadoLotes.Ot;

/// <summary>
/// HU #13392 (Épica #13216, Feature #13308, diseño 10 §2.3) — el ítem del lote de maestros de la bandeja OT procesado
/// por el <see cref="ProcesarItemLoteHandler"/> REAL con <see cref="OtConsolidadoLoteEntregador"/>, el servicio REAL de
/// contexto <see cref="OtClientProcedureConsolidadoContext"/> (#13389) y el <see cref="ConsolidadoLoteEntregador"/> REAL
/// (#13371) con los generadores REALES. Son dobles el repositorio OT (registra el scope de la compañía cliente y
/// propaga lo que lance la acción, como la transacción real), el resolver de la matriz, el guard de Quipux, el
/// repositorio de trámites, el almacenamiento y la persistencia del ítem. La transacción real con PostgreSQL está en
/// <c>OtConsolidadoLoteEntregadorIntegrationTests</c>.
/// <para>Uso de ejemplo:
/// <c>var r = await handler.HandleAsync(ProcesarItemLoteCommand.Con(loteOt, item, settings), ct);</c> ⇒ maestro
/// guardado tal cual (sin generador ni guard) o primera generación en el scope del cliente tras el guard con el tenant OT.</para>
/// </summary>
public sealed class OtConsolidadoLoteEntregadorTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 10, 7, 20, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantOt = Guid.Parse("e1339200-0000-4000-8000-0000000000a1");
    private static readonly Guid Organismo = Guid.Parse("e1339200-0000-4000-8000-0000000000e5");
    private static readonly IReadOnlyList<string> Matriz = ["fur", "factura", "soat"];

    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly AlmacenamientoFalso _storage;
    private readonly IExpedienteHotDocumentsRegenerator _hotDocs = Substitute.For<IExpedienteHotDocumentsRegenerator>();
    private readonly IMaestroRadicadoLookup _radicado = Substitute.For<IMaestroRadicadoLookup>();
    private readonly IOtClientProcedureRepository _otRepo = Substitute.For<IOtClientProcedureRepository>();
    private readonly IResolvedDocumentMatrixResolver _matriz = Substitute.For<IResolvedDocumentMatrixResolver>();
    private readonly IQuipuxReadOnlyGuard _quipux = Substitute.For<IQuipuxReadOnlyGuard>();
    private readonly IConsolidadoLoteAccessChecker _acceso = Substitute.For<IConsolidadoLoteAccessChecker>();
    private readonly IConsolidadoLoteItemProceso _proceso = Substitute.For<IConsolidadoLoteItemProceso>();
    private readonly EntregadorEspia _entregador;
    private readonly ProcesarItemLoteHandler _handler;

    /// <summary>Orden de lo que pasa: scope del cliente, guard, subida del binario (con o sin scope abierto).</summary>
    private readonly List<string> _log = [];

    /// <summary>Cuántas veces la acción del scope terminó lanzando (la transacción real se revierte).</summary>
    private int _scopesRevertidos;

    private Guid? _scopeAbierto;

    public OtConsolidadoLoteEntregadorTests()
    {
        _storage = new AlmacenamientoFalso(() => _scopeAbierto, _log);
        _radicado.AttachmentRadicadoAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Guid?)null);
        _radicado.AttachmentsProtegidosAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlySet<Guid>)new HashSet<Guid>());
        _acceso.TieneAccesoAsync(Arg.Any<LoteItemContexto>(), Arg.Any<CancellationToken>()).Returns(true);
        _proceso.MarcarIncluidoAsync(Arg.Any<LoteItemIncluido>(), Arg.Any<CancellationToken>()).Returns(true);
        _proceso.MarcarOmitidoAsync(Arg.Any<LoteItemOmitido>(), Arg.Any<CancellationToken>()).Returns(true);
        _proceso.ReprogramarAsync(Arg.Any<LoteItemReintento>(), Arg.Any<CancellationToken>()).Returns(true);
        _quipux.ValidateActionAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                _log.Add($"guard:{ci.ArgAt<Guid>(0)}:{ci.ArgAt<string>(1)}:scope={_scopeAbierto}");
                return QuipuxReadOnlyResult.Allowed();
            });
        _matriz.ResolveAsync(Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(Matriz.Select(c => new ResolvedDocumentMatrixItem { Codigo = c }).ToList());
        _otRepo.ExecuteInClientTenantScopeAsync(
                Arg.Any<Guid>(), Arg.Any<Func<Task<LoteItemEntregaResult>>>(), Arg.Any<CancellationToken>())
            .Returns(async ci =>
            {
                var cliente = ci.ArgAt<Guid>(0);
                _log.Add($"scope:{cliente}");
                _scopeAbierto = cliente;
                try
                {
                    return await ci.ArgAt<Func<Task<LoteItemEntregaResult>>>(1)();
                }
                catch
                {
                    _scopesRevertidos++;
                    throw;
                }
                finally
                {
                    _scopeAbierto = null;
                }
            });

        var merger = new MergerFalso();
        _entregador = new EntregadorEspia(new ConsolidadoLoteEntregador(
            _repo,
            new GenerarConsolidadoHandler(_repo, merger, _storage, hotDocsRegenerator: _hotDocs),
            new GenerarConsolidadoMaestroHandler(_repo, merger, _storage, maestroRadicado: _radicado, hotDocsRegenerator: _hotDocs),
            maestroRadicado: _radicado));
        var origen = new OtConsolidadoLoteEntregador(
            _acceso, new OtClientProcedureConsolidadoContext(_otRepo, _matriz), _entregador, _quipux);
        _handler = new ProcesarItemLoteHandler(
            new LoteItemOrigenPorOrigen([origen]),
            _proceso,
            NullLogger<ProcesarItemLoteHandler>.Instance,
            LoteActivo(),
            new RelojFijo(Ahora));
    }

    // ── AC1 — maestro existente tal cual ─────────────────────────────────────────────────────

    [Fact]
    public async Task AC1_MaestroDesactualizado_SeIncluyeElMismoArchivoYSha_SinGeneradorNiGuard_NiTocarFurNiConsolidado()
    {
        var ct = TestContext.Current.CancellationToken;
        var lote = LoteOt();
        var t = Instancia(TramiteEstado.Aprobado);
        AddAttachment(t, LoteTipoDocumento.Consolidado, "consolidado.pdf", "system");
        var maestro = AddAttachment(t, LoteTipoDocumento.ConsolidadoMaestro, "maestro.pdf", "system");
        t.ConsolidadoMaestroVigente = false;
        t.ConsolidadoWizardVigente = false;
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
        await _quipux.DidNotReceiveWithAnyArgs().ValidateActionAsync(default, default!, TestContext.Current.CancellationToken);
        _storage.Saved.Should().BeEmpty("el generador no corre");
        await _repo.DidNotReceive().GetByIdWithChecklistGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        t.Attachments.Select(a => (a.Id, a.Tipo, a.Sha256, a.StoragePath, a.UploadedAt)).Should().Equal(antes,
            "el FUR y el consolidado del gestor no cambian");
        t.ConsolidadoMaestroVigente.Should().BeFalse("la marca de vigencia no se toca");
        t.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task AC1_MaestroRadicadoAnteQuipux_SeEntregaElRadicado_SinGeneradorNiGuard()
    {
        var ct = TestContext.Current.CancellationToken;
        var lote = LoteOt();
        var t = Instancia(TramiteEstado.Aprobado);
        var radicado = AddAttachment(t, LoteTipoDocumento.ConsolidadoMaestro, "radicado.pdf", "system");
        radicado.UploadedAt = Ahora.AddDays(-2);
        AddAttachment(t, LoteTipoDocumento.ConsolidadoMaestro, "posterior.pdf", "system");
        _radicado.AttachmentRadicadoAsync(t.TenantId, t.Id, Arg.Any<CancellationToken>()).Returns(radicado.Id);
        Wire(t);

        var r = await _handler.HandleAsync(Cmd(lote, Item(lote, t)), ct);

        r.DeliveryMode.Should().Be(LoteDeliveryMode.Existente);
        await _proceso.Received(1).MarcarIncluidoAsync(
            Arg.Is<LoteItemIncluido>(i => i.Adjunto.AttachmentId == radicado.Id && i.Adjunto.Sha256 == radicado.Sha256),
            Arg.Any<CancellationToken>());
        await _quipux.DidNotReceiveWithAnyArgs().ValidateActionAsync(default, default!, TestContext.Current.CancellationToken);
        _storage.Saved.Should().BeEmpty();
    }

    // ── AC2 — sin maestro: primera generación en el contexto del cliente ─────────────────────

    [Fact]
    public async Task AC2_SinMaestro_GuardConTenantOt_LuegoGeneraEnElScopeDelCliente_ConLaMatrizDelOt_YUnSoloMaestroOficial()
    {
        var ct = TestContext.Current.CancellationToken;
        var lote = LoteOt();
        var t = Instancia(TramiteEstado.Aprobado);
        var furAntes = t.Attachments.Single(a => a.Tipo == "fur");
        var acceso = Wire(t);
        var item = Item(lote, t);

        var r1 = await _handler.HandleAsync(Cmd(lote, item), ct);
        var r2 = await _handler.HandleAsync(Cmd(lote, item), ct); // reintento tras una generación ya persistida

        r1.DeliveryMode.Should().Be(LoteDeliveryMode.Generado);
        r2.DeliveryMode.Should().Be(LoteDeliveryMode.Existente, "soloSiNoExiste: la segunda vez toma el que quedó");

        // El acceso se resuelve con el tenant OT del lote y su organismo congelado (no el de la sesión).
        await _otRepo.Received(2).GetByIdAsync(TenantOt, t.Id, Organismo, Arg.Any<CancellationToken>());
        // Guard con el tenant OT y la acción del maestro, ANTES de subir el binario; todo dentro del scope del cliente.
        _log.Take(3).Should().Equal(
            $"scope:{t.TenantId}",
            $"guard:{TenantOt}:{OtConsolidadoLoteEntregador.AccionQuipux}:scope={t.TenantId}",
            $"save:scope={t.TenantId}");
        _log.Skip(3).Should().Equal([$"scope:{t.TenantId}"], "en el reintento hay maestro: ni guard ni generador");

        _entregador.Peticiones.Should().HaveCount(2).And.OnlyContain(p =>
            p.ProcedureInstanceId == t.Id
            && p.TenantId == acceso.ClientTenantId
            && p.TipoDocumento == LoteTipoDocumento.ConsolidadoMaestro
            && p.PrecedenciaMatriz != null && p.PrecedenciaMatriz.SequenceEqual(Matriz)
            && p.AntesDeGenerar != null);
        await _matriz.Received().ResolveAsync(acceso.ProcedureTypeId, acceso.TransitOfficeId, Arg.Any<CancellationToken>());

        _storage.Saved.Should().ContainSingle("se genera una sola vez");
        var maestro = t.Attachments.Should().ContainSingle(a => a.Tipo == LoteTipoDocumento.ConsolidadoMaestro).Which;
        t.ConsolidadoMaestroVigente.Should().BeTrue("el maestro generado queda como el oficial");
        await _proceso.Received(1).MarcarIncluidoAsync(
            Arg.Is<LoteItemIncluido>(i => i.Adjunto.AttachmentId == maestro.Id && i.DeliveryMode == LoteDeliveryMode.Generado),
            Arg.Any<CancellationToken>());
        var furDespues = t.Attachments.Single(a => a.Tipo == "fur");
        (furDespues.Id, furDespues.Sha256, furDespues.UploadedAt).Should().Be((furAntes.Id, furAntes.Sha256, furAntes.UploadedAt));
        await _hotDocs.DidNotReceive().RegenerateHotDocumentsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        _scopesRevertidos.Should().Be(0);
    }

    // ── AC3 — excepciones Q10 / Q12 ──────────────────────────────────────────────────────────

    public static TheoryData<string, bool, bool, string, string> Excepciones() => new()
    {
        // estado, migrado, conFur, código, texto
        { TramiteEstado.Aprobado, true, true, ConsolidadoLoteOmisiones.MigradoSoloLectura, "Trámite migrado sin consolidado" },
        { TramiteEstado.Revocado, true, false, ConsolidadoLoteOmisiones.MigradoSoloLectura, "Trámite migrado sin consolidado" },
        { TramiteEstado.Aprobado, false, false, ConsolidadoLoteOmisiones.FurRequerido, "El trámite no tiene FUR; no se pudo generar el consolidado" },
        { TramiteEstado.Anulado, false, false, ConsolidadoLoteOmisiones.FurRequerido, "El trámite no tiene FUR; no se pudo generar el consolidado" },
    };

    [Theory]
    [MemberData(nameof(Excepciones))]
    public async Task AC3_EstadoFinalSinMaestro_MigradoOSinFur_SeOmiteConSuMotivo_SinGenerarNiGuard(
        string estado, bool migrado, bool conFur, string codigo, string texto)
    {
        var ct = TestContext.Current.CancellationToken;
        var lote = LoteOt();
        var t = Instancia(estado, conFur: conFur);
        t.IsMigrated = migrado;
        Wire(t);
        var item = Item(lote, t);

        var r = await _handler.HandleAsync(Cmd(lote, item), ct);

        r.Desenlace.Should().Be(ProcesarItemLoteDesenlace.Omitido);
        r.Codigo.Should().Be(codigo);
        r.Motivo.Should().Be(texto);
        await _proceso.Received(1).MarcarOmitidoAsync(
            new LoteItemOmitido(lote.Id, item.Id, codigo, texto, 0, Ahora), Arg.Any<CancellationToken>());
        _storage.Saved.Should().BeEmpty();
        await _quipux.DidNotReceiveWithAnyArgs().ValidateActionAsync(default, default!, TestContext.Current.CancellationToken);
        t.Attachments.Should().NotContain(a => a.Tipo == LoteTipoDocumento.ConsolidadoMaestro);
    }

    // ── AC4 — validación que niega ──────────────────────────────────────────────────────────

    [Fact]
    public async Task AC4_GuardQuipuxNoPermite_SeOmiteConSuMotivo_YElGeneradorNuncaSeLlama()
    {
        var ct = TestContext.Current.CancellationToken;
        _quipux.ValidateActionAsync(TenantOt, OtConsolidadoLoteEntregador.AccionQuipux, Arg.Any<CancellationToken>())
            .Returns(QuipuxReadOnlyResult.Forbidden());
        var lote = LoteOt();
        var t = Instancia(TramiteEstado.Aprobado);
        Wire(t);
        var item = Item(lote, t);

        var r = await _handler.HandleAsync(Cmd(lote, item), ct);

        r.Desenlace.Should().Be(ProcesarItemLoteDesenlace.Omitido);
        r.Codigo.Should().Be(ConsolidadoLoteOmisiones.QuipuxSoloLectura);
        r.Motivo.Should().Be(ConsolidadoErrorTextos.ParaLote(ConsolidadoLoteOmisiones.QuipuxSoloLectura));
        r.Intentos.Should().Be(0, "una omisión de negocio no consume intentos");
        _storage.Saved.Should().BeEmpty();
        await _repo.DidNotReceive().GetByIdWithChecklistGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        t.Attachments.Should().NotContain(a => a.Tipo == LoteTipoDocumento.ConsolidadoMaestro);
    }

    // ── AC5 — el trámite sale de la bandeja ─────────────────────────────────────────────────

    [Fact]
    public async Task AC5_TramiteFueraDeLaBandeja_MovidoOBorrado_SeOmiteAccesoRevocado_SinAbrirElScopeNiEntregar()
    {
        var ct = TestContext.Current.CancellationToken;
        var lote = LoteOt();
        var t = Instancia(TramiteEstado.Entregado);
        Wire(t);
        // La consulta de la bandeja ya no lo devuelve (cambio de organismo, borrado lógico o estado fuera).
        _otRepo.GetByIdAsync(TenantOt, t.Id, Organismo, Arg.Any<CancellationToken>()).Returns((OtClientProcedure?)null);
        var item = Item(lote, t);

        var r = await _handler.HandleAsync(Cmd(lote, item), ct);

        r.Desenlace.Should().Be(ProcesarItemLoteDesenlace.Omitido);
        r.Codigo.Should().Be(ConsolidadoLoteOmisiones.AccesoRevocado);
        r.Motivo.Should().Be("Acceso revocado");
        _entregador.Peticiones.Should().BeEmpty();
        _log.Should().BeEmpty("ni scope del cliente, ni guard, ni generador");
    }

    [Fact]
    public async Task AC5_TramiteDeOtraCompania_QueLaDelItem_SeOmiteAccesoRevocado_SinEntregarConNingunaDeLasDos()
    {
        var ct = TestContext.Current.CancellationToken;
        var lote = LoteOt();
        var t = Instancia(TramiteEstado.Entregado);
        Wire(t);
        var item = Item(lote, t, tenantId: Guid.NewGuid()); // el ítem congeló otra compañía

        var r = await _handler.HandleAsync(Cmd(lote, item), ct);

        r.Codigo.Should().Be(ConsolidadoLoteOmisiones.AccesoRevocado);
        _entregador.Peticiones.Should().BeEmpty();
        _log.Should().BeEmpty();
    }

    // ── AC6 — el solicitante pierde el acceso ───────────────────────────────────────────────

    [Fact]
    public async Task AC6_SolicitanteSinAcceso_CadaItemPendienteSeOmiteAccesoRevocado_SinResolverLaBandejaNiEntregar()
    {
        var ct = TestContext.Current.CancellationToken;
        _acceso.TieneAccesoAsync(Arg.Any<LoteItemContexto>(), Arg.Any<CancellationToken>()).Returns(false);
        var lote = LoteOt();
        var tramites = new[] { Instancia(TramiteEstado.Entregado), Instancia(TramiteEstado.Aprobado) };
        foreach (var t in tramites)
            Wire(t);

        foreach (var t in tramites)
        {
            var r = await _handler.HandleAsync(Cmd(lote, Item(lote, t)), ct);
            r.Codigo.Should().Be(ConsolidadoLoteOmisiones.AccesoRevocado);
            r.Motivo.Should().Be("Acceso revocado");
        }

        await _acceso.Received(2).TieneAccesoAsync(
            Arg.Is<LoteItemContexto>(c => c.Origen == ConsolidadoExportOrigin.OtBandeja && c.CompaniaLoteId == TenantOt
                && c.OrganismoId == Organismo),
            Arg.Any<CancellationToken>());
        await _otRepo.DidNotReceiveWithAnyArgs().GetByIdAsync(default, default, default(Guid?), TestContext.Current.CancellationToken);
        _entregador.Peticiones.Should().BeEmpty();
    }

    // ── AC8 — fallo de almacenamiento y reintento sin doble generación ─────────────────────

    [Fact]
    public async Task AC8_AlmacenamientoNoDisponible_RevierteElScope_ReprogramaConReintento_YLuegoGeneraUnaSolaVez()
    {
        var ct = TestContext.Current.CancellationToken;
        var lote = LoteOt();
        var t = Instancia(TramiteEstado.Aprobado);
        Wire(t);
        var item = Item(lote, t);
        _storage.FallarAlGuardar = true;

        var r1 = await _handler.HandleAsync(Cmd(lote, item), ct);

        r1.Desenlace.Should().Be(ProcesarItemLoteDesenlace.Reprogramado);
        r1.Intentos.Should().Be(1);
        r1.SiguienteIntentoEn.Should().Be(Ahora.AddSeconds(30));
        _scopesRevertidos.Should().Be(1, "un error técnico dentro del scope revierte la transacción del cliente");
        await _proceso.Received(1).ReprogramarAsync(
            new LoteItemReintento(lote.Id, item.Id, 1, Ahora.AddSeconds(30)), Arg.Any<CancellationToken>());
        t.Attachments.Should().NotContain(a => a.Tipo == LoteTipoDocumento.ConsolidadoMaestro);

        // Reinicio: el reintento (attempts = 1) genera una sola vez.
        _storage.FallarAlGuardar = false;
        item.Attempts = 1;
        var r2 = await _handler.HandleAsync(Cmd(lote, item), ct);

        r2.Desenlace.Should().Be(ProcesarItemLoteDesenlace.Incluido);
        r2.DeliveryMode.Should().Be(LoteDeliveryMode.Generado);
        r2.Intentos.Should().Be(1, "attempts solo lo sube el handler en el fallo técnico");
        _storage.Saved.Should().ContainSingle();
        t.Attachments.Should().ContainSingle(a => a.Tipo == LoteTipoDocumento.ConsolidadoMaestro);
        _scopesRevertidos.Should().Be(1);
    }

    [Fact]
    public async Task Contrato_OrigenOtBandeja_SoloEntregaConsolidadoMaestro_YDelegaLaRevalidacionEnElChecker()
    {
        var ct = TestContext.Current.CancellationToken;
        var origen = new OtConsolidadoLoteEntregador(
            _acceso, new OtClientProcedureConsolidadoContext(_otRepo, _matriz), _entregador, _quipux);
        origen.Origen.Should().Be("ot_bandeja");

        var lote = LoteOt();
        lote.DocumentType = ConsolidadoExportDocumentType.Consolidado;
        var t = Instancia(TramiteEstado.Aprobado);
        Wire(t);
        var ctx = LoteItemContexto.Desde(lote, Item(lote, t));

        (await origen.TieneAccesoAsync(ctx, ct)).Should().BeTrue();
        await _acceso.Received(1).TieneAccesoAsync(ctx, ct);
        var act = () => origen.EntregarAsync(ctx, ct);
        await act.Should().ThrowAsync<InvalidOperationException>("el lote de la bandeja OT es solo de maestros");
        _entregador.Peticiones.Should().BeEmpty();
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────

    private static ConsolidadoExportBatch LoteOt() => new()
    {
        Id = Guid.NewGuid(),
        TenantId = TenantOt,
        OtTransitOfficeId = Organismo,
        RequestedByUserId = Guid.NewGuid(),
        RequestedRoleCode = "ot_admin",
        Origin = ConsolidadoExportOrigin.OtBandeja,
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
        ReferenceNumber = "TRM-2026-013392",
        LeaseUntil = Ahora.AddMinutes(10),
    };

    private static ProcesarItemLoteCommand Cmd(ConsolidadoExportBatch lote, ConsolidadoExportBatchItem item) =>
        ProcesarItemLoteCommand.Con(lote, item, new ConsolidadoExportSettings { MaxItemAttempts = 3, RetryDelaySeconds = 30 });

    /// <summary>Trámite visible en la bandeja (acceso del OT) y cargable con la compañía cliente.</summary>
    private OtClientProcedure Wire(ProcedureInstance instance)
    {
        var acceso = new OtClientProcedure
        {
            Id = instance.Id,
            ClientTenantId = instance.TenantId,
            ProcedureTypeId = instance.ProcedureTypeId,
            TransitOfficeId = Organismo,
        };
        _otRepo.GetByIdAsync(TenantOt, instance.Id, Organismo, Arg.Any<CancellationToken>()).Returns(acceso);
        _repo.GetByIdWithAttachmentsAsync(instance.Id, instance.TenantId, Arg.Any<CancellationToken>()).Returns(instance);
        _repo.GetByIdWithChecklistGraphAsync(instance.Id, instance.TenantId, Arg.Any<CancellationToken>()).Returns(instance);
        return acceso;
    }

    private ProcedureInstance Instancia(string estado, bool conFur = true)
    {
        var instance = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.For("matricula_inicial"),
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-013392",
            Status = estado,
            TransitOfficeId = Organismo,
            CreatedAt = Ahora,
        };

        if (conFur)
            AddAttachment(instance, "fur", "fur.pdf", "system").UploadedAt = Ahora.AddDays(-3);
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

    /// <summary>Delega en el entregador común real y registra cada petición (compañía, tipo, matriz, gancho).</summary>
    private sealed class EntregadorEspia(ILoteItemEntregador real) : ILoteItemEntregador
    {
        public List<LoteItemEntregaRequest> Peticiones { get; } = [];

        public Task<LoteItemEntregaResult> EntregarAsync(LoteItemEntregaRequest request, CancellationToken ct = default)
        {
            Peticiones.Add(request);
            return real.EntregarAsync(request, ct);
        }
    }

    private sealed class MergerFalso : IExpedienteConsolidadoMerger
    {
        public byte[] NormalizeToPdf(byte[] content, string mimetype) => content;

        public byte[] Merge(IReadOnlyList<byte[]> pdfParts) => pdfParts.SelectMany(x => x).ToArray();

        public byte[] Compose(MergeRequest request) => Merge(request.Parts.Select(p => p.Pdf).ToList());
    }

    /// <summary>Almacenamiento en memoria que anota cada subida con el scope abierto en ese momento.</summary>
    private sealed class AlmacenamientoFalso(Func<Guid?> scopeAbierto, List<string> log) : IAttachmentStorage
    {
        public List<string> Saved { get; } = [];
        public Dictionary<string, byte[]> Files { get; } = new();
        public bool FallarAlGuardar { get; set; }

        public async Task<StoredFile> SaveAsync(
            Guid procedureInstanceId, string tipo, string originalFilename, Stream content, CancellationToken ct = default)
        {
            log.Add($"save:scope={scopeAbierto()}");
            if (FallarAlGuardar)
                throw new IOException("almacenamiento no disponible (simulado)");

            using var ms = new MemoryStream();
            await content.CopyToAsync(ms, ct);
            var bytes = ms.ToArray();
            var path = $"{procedureInstanceId:D}/{tipo}_saved_{Saved.Count}";
            Files[path] = bytes;
            Saved.Add(path);
            return new StoredFile(path, $"sha-{tipo}-nuevo-{Saved.Count}", bytes.Length);
        }

        public Task<PresignedUpload> CreatePresignedUploadAsync(
            Guid procedureInstanceId, string tipo, string originalFilename, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public void Delete(string storagePath) => Files.Remove(storagePath);

        public Task<Stream?> OpenReadAsync(string storagePath, CancellationToken ct = default) =>
            Task.FromResult<Stream?>(Files.TryGetValue(storagePath, out var bytes) ? new MemoryStream(bytes) : null);

        public Task<(string Url, DateTimeOffset ExpiresAt)?> GetPresignedViewUrlAsync(
            string storagePath, CancellationToken ct = default) =>
            Task.FromResult<(string Url, DateTimeOffset ExpiresAt)?>(null);
    }

    private sealed class RelojFijo(DateTimeOffset ahora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => ahora;
    }

    /// <summary>HU #13386 — el checkpoint previo al entregador ve el lote activo.</summary>
    private static IConsolidadoLoteRepository LoteActivo()
    {
        var lotes = Substitute.For<IConsolidadoLoteRepository>();
        lotes.GetStatusAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(ConsolidadoExportStatus.EnProceso);
        return lotes;
    }
}
