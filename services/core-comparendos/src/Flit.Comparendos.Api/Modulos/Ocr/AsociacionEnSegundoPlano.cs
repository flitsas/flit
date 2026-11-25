using Flit.Comparendos.Api.Modulos.Avisos;
using Flit.Comparendos.Api.Modulos.Comparendos;
using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Persistence.Modelo;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Modelo;
using Microsoft.EntityFrameworkCore;

namespace Flit.Comparendos.Api.Modulos.Ocr;

public sealed record NoAsociadoEnLote(Guid DocumentoId, string NombreArchivo, string? Motivo);

public sealed record SinCorreoEnLote(string Motivo, int Cantidad);

/// <summary>GDC-EVID §10: el último lote de «Asociar los N» de un paquete, con su avance y su resumen (contrato `enCurso`).</summary>
public sealed record LoteEnCurso(
    Guid LoteId, int PorAsociar, int Total, int Asociados, IReadOnlyList<NoAsociadoEnLote> NoAsociados, int CorreosEnCamino,
    IReadOnlyList<SinCorreoEnLote> SinCorreo, string SolicitadoPor, DateTimeOffset SolicitadoEn, DateTimeOffset? TerminadoEn, DateTimeOffset UltimoAvanceEn);

/// <summary>
/// GDC-EVID §10: «Asociar los N» en segundo plano. El POST solo encola (marca PorAsociar en una operación atómica); el
/// <see cref="ProcesadorAsociacionLote"/> asocia de a uno con la misma regla y el correo en cola.
/// </summary>
public sealed class AsociacionEnSegundoPlano(ComparendosDb db, ServicioOcr servicio, Reloj reloj)
{
    /// <summary>
    /// Marca PorAsociar los elegibles de la MISMA preselección (<see cref="ServicioOcr.ClasificarLoteAsync"/>) y crea el lote.
    /// La condición estado = Duplicado deja cada documento en un solo lote: un doble clic, o dos personas, encola 0 la segunda vez.
    /// </summary>
    public async Task<(int Encolados, Guid? LoteId)> EncolarAsync(PaqueteOcr paquete, Actor actor, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(paquete);
        ArgumentNullException.ThrowIfNull(actor);
        var (paqueteId, tenant) = (paquete.Id, paquete.TenantId);
        var elegibles = (await servicio.ClasificarLoteAsync(paquete, ct).ConfigureAwait(false)).Where(x => x.Revision is null).Select(x => x.DocumentoId).ToArray();
        if (elegibles.Length == 0)
            return (0, null);

        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            // Dos pulsaciones a la vez sobre el mismo paquete se ordenan aquí (sin interbloqueos por el orden de las filas).
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM comparendos.paquete_ocr WHERE id = {paqueteId} FOR UPDATE", ct).ConfigureAwait(false);
            var ids = await db.Database.SqlQuery<Guid>($"""
                UPDATE comparendos.documento_ocr SET estado = 'PorAsociar', proximo_intento_en = NULL
                WHERE id = ANY({elegibles}) AND tenant_id = {tenant} AND paquete_id = {paqueteId} AND estado = 'Duplicado'
                  AND duplicado_de_comparendo_id IS NOT NULL AND fuente_duplicado IS DISTINCT FROM 'ARCHIVO'
                RETURNING id AS "Value"
                """).ToListAsync(ct).ConfigureAwait(false);
            if (ids.Count == 0)
            {
                await tx.RollbackAsync(ct).ConfigureAwait(false);
                return (0, (Guid?)null);
            }

            var lote = new AsociacionLote
            {
                Id = Guid.CreateVersion7(), TenantId = tenant, PaqueteId = paqueteId, SolicitadoPor = actor.UsuarioId,
                SolicitadoPorNombre = actor.Nombre.Length > 200 ? actor.Nombre[..200] : actor.Nombre, SolicitadoEn = reloj.Ahora,
            };
            db.AsociacionesLote.Add(lote);
            foreach (var id in ids)
                db.AsociacionLoteDocumentos.Add(new AsociacionLoteDocumento { LoteId = lote.Id, DocumentoId = id });
            var p = await db.PaquetesOcr.FirstAsync(x => x.Id == paqueteId, ct).ConfigureAwait(false);
            await servicio.RecalcularAsync(p, ct).ConfigureAwait(false);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            await tx.CommitAsync(ct).ConfigureAwait(false);
            return (ids.Count, (Guid?)lote.Id);
        }).ConfigureAwait(false);
    }

    /// <summary>El último lote del paquete (null si nunca hubo), con los contadores calculados de sus documentos.</summary>
    public async Task<LoteEnCurso?> EnCursoAsync(PaqueteOcr paquete, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(paquete);
        var lote = await db.AsociacionesLote.AsNoTracking().Where(l => l.PaqueteId == paquete.Id && l.TenantId == paquete.TenantId)
            .OrderByDescending(l => l.SolicitadoEn).ThenByDescending(l => l.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (lote is null)
            return null;
        var items = await (from i in db.AsociacionLoteDocumentos.AsNoTracking()
                           join d in db.DocumentosOcr.AsNoTracking() on i.DocumentoId equals d.Id
                           where i.LoteId == lote.Id
                           orderby d.Id
                           select new { i.DocumentoId, d.NombreArchivo, i.Resultado, i.Motivo, i.CorreoEstado, i.CorreoMotivo, i.ProcesadoEn }).ToListAsync(ct).ConfigureAwait(false);
        var asociados = items.Where(i => i.Resultado == ResultadoAsociacionLote.Asociado).ToList();
        return new LoteEnCurso(
            lote.Id,
            items.Count(i => i.Resultado == ResultadoAsociacionLote.PorAsociar),
            items.Count,
            asociados.Count,
            items.Where(i => i.Resultado == ResultadoAsociacionLote.NoAsociado).Select(i => new NoAsociadoEnLote(i.DocumentoId, i.NombreArchivo, i.Motivo)).ToList(),
            asociados.Count(i => i.CorreoEstado is ResultadoEvidencia.Pendiente or ResultadoEvidencia.Enviado),
            asociados.Where(i => i.CorreoEstado == ResultadoEvidencia.Omitido).GroupBy(i => i.CorreoMotivo ?? string.Empty)
                .Select(g => new SinCorreoEnLote(g.Key, g.Count())).OrderByDescending(x => x.Cantidad).ThenBy(x => x.Motivo, StringComparer.Ordinal).ToList(),
            lote.SolicitadoPorNombre,
            lote.SolicitadoEn,
            lote.TerminadoEn,
            // Para que la pantalla diga «sin avance desde hace N minutos» si el trabajador se detuvo (se retoma solo).
            items.Max(i => i.ProcesadoEn) ?? lote.SolicitadoEn);
    }
}
