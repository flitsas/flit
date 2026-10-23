using Flit.Infrastructure.Persistence.Repositories;
using Flit.Integration.Tests.Postgres;
using Flit.Tramites.Application.UseCases.ConsolidadoLotes;
using Flit.Tramites.Domain.Entities.ConsolidadoLotes;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using Xunit;

namespace Flit.Integration.Tests.Tramites;

/// <summary>
/// HU #13386 AC7 (diseño 09 §2.4, CF-09/CF-12) — la purga no toca un lote cancelado: la cancelación ya lo dejó con
/// <c>purged_at</c> informado, y tanto la purga a las 24 h (<see cref="PurgarLotesExpiradosHandler"/> →
/// <c>ix_consolidado_export_batches_purge</c>) como la purga del lote anterior en el alta (#13373) filtran
/// <c>purged_at IS NULL</c>. El lote cancelado no pasa a <c>expirado</c> ni se audita <c>lote_purgado</c> sobre él.
/// </summary>
/// <remarks>
/// Uso de ejemplo:
/// <code>
/// var r = await new PurgarLotesExpiradosHandler(lectura, repo, reloj: dentroDe48h).HandleAsync(ct); // r.Vencidos == 0
/// </code>
/// </remarks>
public sealed class PurgaLoteCanceladoTests(PostgresDatabaseFixture fixture) : ConsolidadoLoteCancelacionSiembra(fixture)
{
    /// <summary>Lote cancelado de verdad (#13385) con una parte cerrada (→ purgada) y un ítem pendiente.</summary>
    private async Task<(Guid Usuario, Guid Lote, List<Guid> Tramites, DateTimeOffset PurgadoEn)> CanceladoAsync(string sufijo)
    {
        await SembrarAsync();
        var u = await UsuarioAsync(sufijo);
        var tramites = await TramitesAsync(u, 3, sufijo.ToUpperInvariant());
        var lote = await LoteAsync(u, total: 2, incluidos: 1, partes: 1);
        await ParteAsync(lote, 1, ConsolidadoExportPartStatus.Cerrada);
        await ItemsAsync(lote, u, tramites[..1], ConsolidadoExportItemStatus.Incluido, 0, parte: 1);
        await ItemsAsync(lote, u, tramites[1..2], ConsolidadoExportItemStatus.Pendiente, 1);
        var r = await CancelarAsync(lote, u);
        r.Estado.Should().Be(CancelarLoteEstado.Cancelado);
        // El valor persistido (timestamptz, microsegundos), no el instante en memoria de la respuesta.
        var purgadoEn = await ScalarAsync<DateTime>(
            "SELECT purged_at FROM tramites.consolidado_export_batches WHERE id = @l", ("l", lote));
        return (u, lote, tramites, new DateTimeOffset(DateTime.SpecifyKind(purgadoEn, DateTimeKind.Utc)));
    }

    private async Task DebeSeguirCanceladoAsync(Guid lote, DateTimeOffset purgadoEn)
    {
        await using var ctx = NewContext();
        var fila = await new ConsolidadoLoteLectura(ctx).ObtenerDelDuenoAsync(lote, await DuenoAsync(lote), Ct);
        fila!.Status.Should().Be(ConsolidadoExportStatus.Cancelado, "no pasa a expirado");
        fila.PurgedAt.Should().Be(purgadoEn, "purged_at no se reescribe");
        (await AuditoriasAsync(lote, ConsolidadoExportAuditEvent.LotePurgado)).Should().Be(0);
    }

    private Task<Guid> DuenoAsync(Guid lote) => ScalarAsync<Guid>(
        "SELECT requested_by_user_id FROM tramites.consolidado_export_batches WHERE id = @l", ("l", lote));

    [PostgresFact]
    public async Task AC7_LaPurgaDeExpirados_NoSeleccionaNiPurgaElCancelado_NiAunDosDiasDespues()
    {
        var (_, lote, _, purgadoEn) = await CanceladoAsync("ac7a");

        await using (var ctx = NewContext())
        {
            var lectura = new ConsolidadoLoteLectura(ctx);
            var repo = new ConsolidadoLoteRepository(ctx);
            var dentroDe48h = new RelojFijoIt(DateTimeOffset.UtcNow.AddHours(48));

            (await lectura.ObtenerVencidosAsync(dentroDe48h.GetUtcNow(), 100, Ct)).Should().NotContain(lote);
            var r = await new PurgarLotesExpiradosHandler(lectura, repo, reloj: dentroDe48h).HandleAsync(Ct);
            r.Purgados.Should().Be(0);
            r.Fallidos.Should().Be(0);
            (await repo.PurgarAsync(lote, DateTimeOffset.UtcNow.AddHours(48), Ct))
                .Should().BeFalse("PurgarAsync también filtra purged_at IS NULL");
        }

        await DebeSeguirCanceladoAsync(lote, purgadoEn);
        (await ScalarAsync<string>(
                "SELECT status FROM tramites.consolidado_export_batch_parts WHERE batch_id = @l AND part_number = 1", ("l", lote)))
            .Should().Be(ConsolidadoExportPartStatus.Purgada, "la parte la purgó la cancelación, no la purga");
    }

    [PostgresFact]
    public async Task AC7_ElAltaDeUnLoteNuevo_PurgaElRetenidoAnteriorPeroNoElCancelado()
    {
        var (u, cancelado, tramites, purgadoEn) = await CanceladoAsync("ac7b");
        // Un lote terminado y todavía retenido del mismo usuario, sembrado como más antiguo: la alta SÍ debe purgarlo.
        var retenido = await LoteAsync(u, ConsolidadoExportStatus.Completado, minutosAtras: 60);

        CrearLoteResultado creado;
        await using (var ctx = NewContext())
        {
            creado = await new ConsolidadoLoteRepository(ctx).CrearAsync(new NuevoLoteConsolidados
            {
                TenantId = Compania,
                UsuarioId = u,
                RolCodigo = "Radicador",
                Origen = ConsolidadoExportOrigin.Tramites,
                TipoDocumento = ConsolidadoExportDocumentType.Consolidado,
                ModoSeleccion = ConsolidadoExportSelectionMode.Ids,
                DekEnvuelta = [1, 2, 3],
                EfectosAceptadosEn = DateTimeOffset.UtcNow,
                Items = [new ProcedureInstanceRef(tramites[2], Compania, "R-13386", null)],
                IdsCount = 1,
            }, Ct);
        }

        creado.Estado.Should().Be(CrearLoteEstado.Creado);
        creado.LotesPurgados.Should().Be(1, "solo el retenido sin purged_at");
        (await EstadoLoteAsync(retenido)).Should().Be(ConsolidadoExportStatus.Expirado);
        (await AuditoriasAsync(retenido, ConsolidadoExportAuditEvent.LotePurgado)).Should().Be(1);
        await DebeSeguirCanceladoAsync(cancelado, purgadoEn);
    }

    private sealed class RelojFijoIt(DateTimeOffset ahora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => ahora;
    }
}
