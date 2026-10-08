using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Integration.Tests.Postgres;
using Flit.Tramites.Application.UseCases.ConsolidadoLotes;
using Flit.Tramites.Domain.Entities.ConsolidadoLotes;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NSubstitute;
using Xunit;

namespace Flit.Integration.Tests.Tramites;

/// <summary>
/// HU #13386 (Feature #13307, diseño 09 §2.4, CF-09/CF-11) — los carriles y la consulta ante un lote cancelado, contra
/// PostgreSQL real (cancelación real de #13385):
/// <list type="bullet">
///   <item>AC1: <see cref="IConsolidadoLoteRepository.GetStatusAsync"/> lee el estado; el handler del ítem se detiene
///   antes del entregador.</item>
///   <item>AC2: el ítem en vuelo termina su generación (el consolidado queda en el trámite), su cierre actualiza 0 filas,
///   no suben los contadores y no entra a ninguna parte.</item>
///   <item>AC3: con el lote cancelado no se crea ni se asigna ninguna parte (cierre del carril ni asignación parcial).</item>
///   <item>AC5: carrera cancelación ↔ finalización con dos conexiones y un tercer «portero» que fija quién toma el lock
///   del lote primero: gana la primera; si ganó la finalización, la cancelación es <c>Terminado</c> (409
///   <c>lote_terminado</c>); si ganó la cancelación, el lote no pasa a <c>completado</c>.</item>
///   <item>AC6: <c>actual</c> devuelve el cancelado (terminadoEn, contadores, sin partes) hasta que el usuario crea otro
///   lote; el activo gana; la descarga de cualquier parte es 410 <c>descarga_expirada</c>.</item>
/// </list>
/// </summary>
/// <remarks>
/// Uso de ejemplo:
/// <code>
/// await using var ctx = NewContext();
/// var estado = await new ConsolidadoLoteRepository(ctx).GetStatusAsync(loteId, ct); // "cancelado"
/// </code>
/// </remarks>
public sealed class ConsolidadoLoteCancelacionCarrilesIntegrationTests(PostgresDatabaseFixture fixture)
    : ConsolidadoLoteCancelacionSiembra(fixture)
{
    private static readonly LoteItemAdjunto Adjunto = new(Guid.NewGuid(), "fm/gen-13386", 100, new string('c', 64), "consolidado.pdf");

    // ── AC1 ─────────────────────────────────────────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task AC1_GetStatusAsync_LeeElEstado_YElItemReclamadoDeUnLoteCanceladoNoLlegaAlEntregador()
    {
        await SembrarAsync();
        var u = await UsuarioAsync("ac1");
        var tramites = await TramitesAsync(u, 1, "AC1");
        var lote = await LoteAsync(u, total: 1);
        var items = await ItemsAsync(lote, u, tramites, ConsolidadoExportItemStatus.Procesando);

        await using (var ctx = NewContext())
        {
            var repo = new ConsolidadoLoteRepository(ctx);
            (await repo.GetStatusAsync(lote, Ct)).Should().Be(ConsolidadoExportStatus.EnProceso);
            (await repo.GetStatusAsync(Guid.CreateVersion7(), Ct)).Should().BeNull("lote inexistente");
        }

        (await CancelarAsync(lote, u)).Estado.Should().Be(CancelarLoteEstado.Cancelado);
        var entregador = Substitute.For<ILoteItemEntregador>();

        await using (var ctx = NewContext())
        {
            var repo = new ConsolidadoLoteRepository(ctx);
            (await repo.GetStatusAsync(lote, Ct)).Should().Be(ConsolidadoExportStatus.Cancelado);
            var r = await Handler(ctx, entregador).HandleAsync(Comando(await LoteEntidadAsync(lote), await ItemEntidadAsync(items[0])), Ct);
            r.Desenlace.Should().Be(ProcesarItemLoteDesenlace.LoteDetenido);
        }

        await entregador.DidNotReceiveWithAnyArgs().EntregarAsync(default!, default);
        await ExecAsync("UPDATE tramites.consolidado_export_batches SET deleted_at = now() WHERE id = @l", ("l", lote));
        await using (var ctx = NewContext())
            (await new ConsolidadoLoteRepository(ctx).GetStatusAsync(lote, Ct)).Should().BeNull("borrado lógico ⇒ null");
    }

    // ── AC2 ─────────────────────────────────────────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task AC2_ItemEnVueloAlCancelar_ElConsolidadoQuedaOficial_CierreCon0Filas_SinContadoresNiParte()
    {
        await SembrarAsync(maxPdfs: 1);
        var u = await UsuarioAsync("ac2");
        var tramites = await TramitesAsync(u, 2, "AC2");
        var lote = await LoteAsync(u, total: 2);
        var enVuelo = await ItemsAsync(lote, u, tramites[..1], ConsolidadoExportItemStatus.Procesando);
        await ItemsAsync(lote, u, tramites[1..], ConsolidadoExportItemStatus.Pendiente, 1);

        // El entregador «genera» (persiste el consolidado en el trámite) y, mientras tanto, el dueño cancela el lote.
        var entregador = new EntregadorQueCancela(this, tramites[0], lote, u);
        ProcesarItemLoteResultado r;
        await using (var ctx = NewContext())
            r = await Handler(ctx, entregador).HandleAsync(Comando(await LoteEntidadAsync(lote), await ItemEntidadAsync(enVuelo[0])), Ct);

        entregador.Cancelacion!.Estado.Should().Be(CancelarLoteEstado.Cancelado);
        r.Desenlace.Should().Be(ProcesarItemLoteDesenlace.Incluido);
        r.Aplicado.Should().BeFalse("el ítem ya es cancelado: el UPDATE condicionado actualiza 0 filas");
        (await ScalarAsync<long>(
                "SELECT count(*) FROM tramites.procedure_instance_attachments WHERE id = @a AND procedure_instance_id = @t AND tipo = 'consolidado'",
                ("a", entregador.AdjuntoId), ("t", tramites[0])))
            .Should().Be(1, "el consolidado generado queda oficial en el trámite (CF-09)");
        (await ScalarAsync<bool>(
                """
                SELECT status = 'cancelado' AND part_number IS NULL AND attachment_id IS NULL
                  FROM tramites.consolidado_export_batch_items WHERE id = @i
                """,
                ("i", enVuelo[0])))
            .Should().BeTrue("el ítem no se marca incluido ni entra a ninguna parte");
        (await ScalarAsync<bool>(
                "SELECT included_count = 0 AND generated_count = 0 AND omitted_count = 0 AND parts_count = 0 FROM tramites.consolidado_export_batches WHERE id = @l",
                ("l", lote)))
            .Should().BeTrue("no suben los contadores");
        (await PartesAsync(lote)).Should().Be(0, "con max_pdfs_per_part = 1 un incluido aplicado habría creado una parte");
    }

    // ── AC3 ─────────────────────────────────────────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task AC3_LoteCancelado_NiElCierreDelCarrilNiLaAsignacionParcialCreanOAsignanPartes()
    {
        await SembrarAsync(maxPdfs: 1);
        var u = await UsuarioAsync("ac3");
        var tramites = await TramitesAsync(u, 2, "AC3");
        var lote = await LoteAsync(u, total: 2, incluidos: 2);
        var incluidos = await ItemsAsync(lote, u, tramites, ConsolidadoExportItemStatus.Incluido);

        (await CancelarAsync(lote, u)).Estado.Should().Be(CancelarLoteEstado.Cancelado);

        await using (var ctx = NewContext())
        {
            (await new ConsolidadoLoteRepository(ctx).CerrarCarrilAsync(lote, Ct)).Should().Be(CierreCarrilResultado.NoAplicado);
            await using var tx = await ctx.Database.BeginTransactionAsync(Ct);
            var parcial = await ConsolidadoLotePartesAsignacion.AsignarAsync(ctx, lote, ModoAsignacion.Parcial, Ct);
            var final = await ConsolidadoLotePartesAsignacion.AsignarAsync(ctx, lote, ModoAsignacion.Final, Ct);
            await tx.CommitAsync(Ct);
            parcial.Asignado.Should().BeFalse();
            final.Asignado.Should().BeFalse();
        }

        (await PartesAsync(lote)).Should().Be(0);
        (await ScalarAsync<long>(
                "SELECT count(*) FROM tramites.consolidado_export_batch_items WHERE id = ANY(@i) AND part_number IS NOT NULL",
                ("i", incluidos)))
            .Should().Be(0);
        (await EstadoLoteAsync(lote)).Should().Be(ConsolidadoExportStatus.Cancelado);
    }

    // ── AC5 ─────────────────────────────────────────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task AC5_Negativo_GanaLaFinalizacion_LaCancelacionRespondeTerminado409()
    {
        var (u, lote) = await LoteListoParaFinalizarAsync("ac5f");

        var (finalizado, cancelacion) = await CarreraAsync(lote, u, finalizacionPrimero: true);

        finalizado.Should().NotBeNull("la finalización tomó el lock primero");
        finalizado!.Estado.Should().Be(ConsolidadoExportStatus.Completado);
        cancelacion.Estado.Should().Be(CancelarLoteEstado.Terminado, "el endpoint lo traduce a 409 lote_terminado");
        cancelacion.Lote!.Status.Should().Be(ConsolidadoExportStatus.Completado);
        (await ScalarAsync<bool>(
                "SELECT status = 'completado' AND dek_wrapped IS NOT NULL AND purged_at IS NULL FROM tramites.consolidado_export_batches WHERE id = @l",
                ("l", lote)))
            .Should().BeTrue("el ZIP sigue descargable");
        (await AuditoriasAsync(lote, ConsolidadoExportAuditEvent.LoteFinalizado)).Should().Be(1);
        (await AuditoriasAsync(lote, ConsolidadoExportAuditEvent.LoteCancelado)).Should().Be(0);
    }

    [PostgresFact]
    public async Task AC5_GanaLaCancelacion_ElLoteNoPasaACompletado()
    {
        var (u, lote) = await LoteListoParaFinalizarAsync("ac5c");

        var (finalizado, cancelacion) = await CarreraAsync(lote, u, finalizacionPrimero: false);

        cancelacion.Estado.Should().Be(CancelarLoteEstado.Cancelado, "la cancelación tomó el lock primero");
        finalizado.Should().BeNull("tras esperar el lock, el lote ya no está empaquetando");
        (await EstadoLoteAsync(lote)).Should().Be(ConsolidadoExportStatus.Cancelado);
        (await AuditoriasAsync(lote, ConsolidadoExportAuditEvent.LoteFinalizado)).Should().Be(0);
        (await AuditoriasAsync(lote, ConsolidadoExportAuditEvent.LoteCancelado)).Should().Be(1);
        (await ScalarAsync<string>(
                "SELECT status FROM tramites.consolidado_export_batch_parts WHERE batch_id = @l AND part_number = 1", ("l", lote)))
            .Should().Be(ConsolidadoExportPartStatus.Purgada);
    }

    /// <summary>Lote <c>empaquetando</c> con su única parte <c>cerrada</c> y sin ítems vivos: la finalización procede.</summary>
    private async Task<(Guid Usuario, Guid Lote)> LoteListoParaFinalizarAsync(string sufijo)
    {
        await SembrarAsync();
        var u = await UsuarioAsync(sufijo);
        var tramites = await TramitesAsync(u, 1, sufijo.ToUpperInvariant());
        var lote = await LoteAsync(u, ConsolidadoExportStatus.Empaquetando, total: 1, incluidos: 1, generados: 1, partes: 1);
        await ParteAsync(lote, 1, ConsolidadoExportPartStatus.Cerrada);
        await ItemsAsync(lote, u, tramites, ConsolidadoExportItemStatus.Incluido, parte: 1);
        await using (var ctx = NewContext())
            (await new ConsolidadoLoteEmpaquetado(ctx).ObtenerLotesParaFinalizarAsync(10, Ct)).Should().Contain(lote);
        return (u, lote);
    }

    /// <summary>
    /// Un «portero» toma el lock del lote; la primera operación se encola tras él, después la segunda (se espera a que
    /// cada una esté bloqueada en <c>pg_stat_activity</c>); al soltar el portero, PostgreSQL concede el lock de fila en
    /// orden de llegada.
    /// </summary>
    private async Task<(LoteFinalizado? Finalizado, CancelarLoteResultado Cancelacion)> CarreraAsync(
        Guid lote, Guid usuario, bool finalizacionPrimero)
    {
        await using var portero = await Fixture.OpenConnectionAsync();
        await using var tx = await portero.BeginTransactionAsync(Ct);
        await using (var bloqueo = new NpgsqlCommand(
            "SELECT 1 FROM tramites.consolidado_export_batches WHERE id = @l FOR UPDATE", portero, tx))
        {
            bloqueo.Parameters.AddWithValue("l", lote);
            await bloqueo.ExecuteNonQueryAsync(Ct);
        }

        Task<LoteFinalizado?> Finalizar() => Task.Run(async () =>
        {
            await using var ctx = NewContext();
            return await new ConsolidadoLoteEmpaquetado(ctx).FinalizarLoteAsync(lote, Ct);
        });
        Task<CancelarLoteResultado> Cancelar() => Task.Run(() => CancelarAsync(lote, usuario));

        Task<LoteFinalizado?> finalizacion;
        Task<CancelarLoteResultado> cancelacion;
        if (finalizacionPrimero)
        {
            finalizacion = Finalizar();
            await EsperarBloqueadosAsync(1);
            cancelacion = Cancelar();
            await EsperarBloqueadosAsync(2);
        }
        else
        {
            cancelacion = Cancelar();
            await EsperarBloqueadosAsync(1);
            finalizacion = Finalizar();
            await EsperarBloqueadosAsync(2);
        }

        await tx.CommitAsync(Ct);
        var limite = TimeSpan.FromSeconds(60);
        return (await finalizacion.WaitAsync(limite, Ct), await cancelacion.WaitAsync(limite, Ct));
    }

    private async Task EsperarBloqueadosAsync(int esperados)
    {
        var hasta = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            var bloqueados = await ScalarAsync<long>(
                """
                SELECT count(*) FROM pg_stat_activity
                 WHERE datname = current_database() AND wait_event_type = 'Lock' AND pid <> pg_backend_pid()
                """);
            if (bloqueados >= esperados)
                return;
            if (DateTime.UtcNow > hasta)
                throw new TimeoutException($"Se esperaban {esperados} sesiones bloqueadas por el lock del lote; hay {bloqueados}.");
            await Task.Delay(50, Ct);
        }
    }

    // ── AC6 ─────────────────────────────────────────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task AC6_Actual_DevuelveElCancelado_ConTerminadoEnContadoresYSinPartes_YLaDescargaEs410()
    {
        await SembrarAsync();
        var u = await UsuarioAsync("ac6");
        var tramites = await TramitesAsync(u, 3, "AC6");
        var lote = await LoteAsync(u, total: 3, incluidos: 2, generados: 1, partes: 2);
        await ParteAsync(lote, 1, ConsolidadoExportPartStatus.Cerrada);
        await ParteAsync(lote, 2, ConsolidadoExportPartStatus.Empaquetando);
        await ItemsAsync(lote, u, tramites[..1], ConsolidadoExportItemStatus.Incluido, 0, parte: 1);
        await ItemsAsync(lote, u, tramites[1..2], ConsolidadoExportItemStatus.Incluido, 1, parte: 2);
        await ItemsAsync(lote, u, tramites[2..], ConsolidadoExportItemStatus.Pendiente, 2);

        var cancelado = await CancelarAsync(lote, u);
        cancelado.Estado.Should().Be(CancelarLoteEstado.Cancelado);

        await using var ctx = NewContext();
        var lectura = new ConsolidadoLoteLectura(ctx);
        var r = await new ConsultarLoteConsolidadosHandler(lectura).ActualAsync(new ObtenerLoteActualQuery(u), Ct);

        r.Should().NotBeNull("el último lote cancelado se informa (CF-11): ya no es 204");
        r!.Lote.Id.Should().Be(lote);
        r.Lote.Status.Should().Be(ConsolidadoExportStatus.Cancelado);
        r.Lote.FinishedAt!.Value.Should().BeCloseTo(cancelado.Lote!.FinishedAt!.Value, TimeSpan.FromMilliseconds(1),
            "terminadoEn = instante de la cancelación (persistido con precisión de microsegundos)");
        r.Lote.PurgedAt.Should().Be(r.Lote.FinishedAt);
        (r.Lote.TotalItems, r.Lote.IncludedCount, r.Lote.OmittedCount, r.Lote.GeneratedCount).Should().Be((3, 2, 0, 1));
        r.Partes.Should().BeEmpty();

        foreach (var numero in new[] { 1, 2, 3 })
        {
            var descarga = await new DescargarParteHandler(lectura, Substitute.For<IConsolidadoLoteParteStorage>(),
                    Substitute.For<IConsolidadoLoteCipher>())
                .PrepararAsync(new DescargarParteQuery(lote, numero, u, "Radicador"), Ct);
            descarga.Estado.Should().Be(DescargarParteEstado.Expirada, $"parte {numero}: 410 descarga_expirada");
        }

        (await AuditoriasAsync(lote, ConsolidadoExportAuditEvent.ParteDescargada)).Should().Be(0);
    }

    [PostgresFact]
    public async Task AC6_Borde_ElActivoGanaAlCancelado_YElCanceladoSeInformaHastaQueElUsuarioCreaOtroLote()
    {
        await SembrarAsync();
        var u = await UsuarioAsync("ac6b");
        var tramites = await TramitesAsync(u, 2, "AC6B");
        var cancelado = await LoteAsync(u, total: 1);
        await ItemsAsync(cancelado, u, tramites[..1], ConsolidadoExportItemStatus.Pendiente);
        (await CancelarAsync(cancelado, u)).Estado.Should().Be(CancelarLoteEstado.Cancelado);

        await using (var ctx = NewContext())
            (await new ConsolidadoLoteLectura(ctx).ObtenerActualDelDuenoAsync(u, Ct))!.Id.Should().Be(cancelado);

        // Activo sembrado como MÁS ANTIGUO que el cancelado: aun así gana.
        var activo = await LoteAsync(u, ConsolidadoExportStatus.EnCola, total: 1, minutosAtras: 120);
        await using (var ctx = NewContext())
            (await new ConsolidadoLoteLectura(ctx).ObtenerActualDelDuenoAsync(u, Ct))!.Id.Should().Be(activo, "el activo gana");

        await ExecAsync("DELETE FROM tramites.consolidado_export_batches WHERE id = @l", ("l", activo));
        Guid nuevo;
        await using (var ctx = NewContext())
        {
            var creado = await new ConsolidadoLoteRepository(ctx).CrearAsync(new NuevoLoteConsolidados
            {
                TenantId = Compania,
                UsuarioId = u,
                RolCodigo = "Radicador",
                Origen = ConsolidadoExportOrigin.Tramites,
                TipoDocumento = ConsolidadoExportDocumentType.Consolidado,
                ModoSeleccion = ConsolidadoExportSelectionMode.Ids,
                DekEnvuelta = [1, 2, 3],
                EfectosAceptadosEn = DateTimeOffset.UtcNow,
                Items = [new ProcedureInstanceRef(tramites[1], Compania, "R-13386", null)],
                IdsCount = 1,
            }, Ct);
            creado.Estado.Should().Be(CrearLoteEstado.Creado);
            nuevo = creado.Lote!.Id;
        }

        // Si el lote nuevo termina y se purga a las 24 h, el cancelado anterior NO reaparece.
        await ExecAsync(
            """
            UPDATE tramites.consolidado_export_batches
               SET status = 'completado', finished_at = now(), expires_at = now() - interval '1 minute'
             WHERE id = @l
            """,
            ("l", nuevo));
        await using (var ctx = NewContext())
        {
            (await new ConsolidadoLoteLectura(ctx).ObtenerActualDelDuenoAsync(u, Ct))!.Id.Should().Be(nuevo, "el usuario creó otro lote");
            (await new ConsolidadoLoteRepository(ctx).PurgarAsync(nuevo, DateTimeOffset.UtcNow, Ct)).Should().BeTrue();
        }

        await using (var ctx = NewContext())
            (await new ConsolidadoLoteLectura(ctx).ObtenerActualDelDuenoAsync(u, Ct))
                .Should().BeNull("el último lote se purgó en la retención normal ⇒ 204; el cancelado viejo no vuelve");
    }

    // ── Apoyo ───────────────────────────────────────────────────────────────────────────────────────────

    private static ProcesarItemLoteHandler Handler(FlitDbContext ctx, ILoteItemEntregador entregador)
    {
        var acceso = Substitute.For<IConsolidadoLoteAccessChecker>();
        acceso.TieneAccesoAsync(Arg.Any<LoteItemContexto>(), Arg.Any<CancellationToken>()).Returns(true);
        return new ProcesarItemLoteHandler(
            new LoteItemOrigenPorOrigen([new TramitesLoteItemOrigen(acceso, entregador)]),
            new ConsolidadoLoteItemProceso(ctx),
            NullLogger<ProcesarItemLoteHandler>.Instance,
            new ConsolidadoLoteRepository(ctx));
    }

    private static ProcesarItemLoteCommand Comando(ConsolidadoExportBatch lote, ConsolidadoExportBatchItem item) =>
        ProcesarItemLoteCommand.Con(lote, item, new ConsolidadoExportSettings { MaxItemAttempts = 3, RetryDelaySeconds = 30 });

    private async Task<ConsolidadoExportBatch> LoteEntidadAsync(Guid lote)
    {
        await using var ctx = NewContext();
        return await ctx.ConsolidadoExportBatches.AsNoTracking().SingleAsync(b => b.Id == lote, Ct);
    }

    private async Task<ConsolidadoExportBatchItem> ItemEntidadAsync(Guid item)
    {
        await using var ctx = NewContext();
        return await ctx.ConsolidadoExportBatchItems.AsNoTracking().SingleAsync(i => i.Id == item, Ct);
    }

    /// <summary>Entregador que persiste el consolidado del trámite y, «durante la generación», cancela el lote.</summary>
    private sealed class EntregadorQueCancela(
        ConsolidadoLoteCancelacionCarrilesIntegrationTests prueba, Guid tramite, Guid lote, Guid dueno) : ILoteItemEntregador
    {
        public Guid AdjuntoId { get; } = Guid.CreateVersion7();
        public CancelarLoteResultado? Cancelacion { get; private set; }

        public async Task<LoteItemEntregaResult> EntregarAsync(LoteItemEntregaRequest request, CancellationToken ct = default)
        {
            await prueba.ExecAsync(
                """
                INSERT INTO tramites.procedure_instance_attachments
                    (id, tenant_id, procedure_instance_id, tipo, filename, mimetype, size_bytes, sha256, storage_path, uploaded_at)
                VALUES (@id, @c, @t, 'consolidado', 'consolidado.pdf', 'application/pdf', 100, repeat('c', 64), 'fm/gen-13386', now())
                """,
                ("id", AdjuntoId), ("c", Compania), ("t", tramite));
            Cancelacion = await prueba.CancelarAsync(lote, dueno);
            return LoteItemEntregaResult.Incluido(Adjunto with { AttachmentId = AdjuntoId }, LoteDeliveryMode.Generado);
        }
    }
}
