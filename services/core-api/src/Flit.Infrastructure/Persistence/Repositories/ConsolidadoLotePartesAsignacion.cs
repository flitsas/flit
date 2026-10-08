using Flit.Tramites.Application.UseCases.ConsolidadoLotes;
using Flit.Tramites.Domain.Entities.ConsolidadoLotes;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// HU #13377 (Épica #13216, diseño §3) — persiste el plan de <see cref="AsignadorDePartes"/> para un lote.
/// <b>Precondición:</b> corre dentro de una transacción que YA tiene el lock del lote (<c>SELECT … FOR UPDATE</c>), así
/// la numeración de partes es estable y sin huecos (la subclave de cifrado FLZ1 v6 deriva de <c>part_number</c>) y no
/// hay dos asignaciones simultáneas del mismo lote. Orden de locks lote → ítem: los ítems se tocan después.
/// <list type="bullet">
///   <item>Solo actúa si el lote sigue <c>en_proceso</c> (un lote cancelado bajo lock no asigna, diseño #13307).</item>
///   <item>Lee N y M de <c>consolidado_export_settings</c>; sin fila no asigna.</item>
///   <item>Inserta las partes <c>pendiente</c> numeradas a continuación de la mayor existente, con
///   <c>pdf_count</c>/<c>omitted_count</c>; luego fija <c>part_number</c> en los ítems (la FK compuesta exige la parte
///   primero) y <c>parts_count</c> en el lote.</item>
/// </list>
/// SQL parametrizado; sin logs (los registra el llamador, solo con ids y conteos).
/// </summary>
internal static class ConsolidadoLotePartesAsignacion
{
    /// <summary>Resultado de una asignación.</summary>
    /// <param name="Asignado"><c>false</c> si el lote no estaba <c>en_proceso</c> o no hay parámetros.</param>
    /// <param name="PartesCreadas">Partes nuevas.</param>
    /// <param name="PartesTotales">Partes del lote tras la asignación.</param>
    internal readonly record struct Resultado(bool Asignado, int PartesCreadas, int PartesTotales);

    private static readonly Resultado NoAsignado = new(false, 0, 0);

    /// <summary>Asigna las partes del lote <paramref name="batchId"/> en el <paramref name="modo"/> dado.</summary>
    internal static async Task<Resultado> AsignarAsync(
        FlitDbContext db, Guid batchId, ModoAsignacion modo, CancellationToken ct)
    {
        var estado = await db.ConsolidadoExportBatches.AsNoTracking()
            .Where(b => b.Id == batchId && b.DeletedAt == null)
            .Select(b => b.Status)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (estado != ConsolidadoExportStatus.EnProceso)
            return NoAsignado;

        var limites = await db.ConsolidadoExportSettings.AsNoTracking()
            .Select(s => new { s.MaxPdfsPerPart, s.MaxMbPerPart })
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (limites is null)
            return NoAsignado;

        var ultima = await db.ConsolidadoExportBatchParts.AsNoTracking()
            .Where(p => p.BatchId == batchId)
            .MaxAsync(p => (short?)p.PartNumber, ct).ConfigureAwait(false) ?? 0;

        var incluido = ConsolidadoExportItemStatus.Incluido;
        var omitido = ConsolidadoExportItemStatus.Omitido;
        var sinParte = await db.ConsolidadoExportBatchItems.AsNoTracking()
            .Where(i => i.BatchId == batchId && i.PartNumber == null && (i.Status == incluido || i.Status == omitido))
            .Select(i => new ItemSinParte(
                i.Id, i.Status == incluido, i.SizeBytes ?? 0, i.ProcessedAt ?? i.CreatedAt, i.Position))
            .ToListAsync(ct).ConfigureAwait(false);

        var plan = AsignadorDePartes.Planear(
            sinParte, limites.MaxPdfsPerPart, AsignadorDePartes.MbABytes(limites.MaxMbPerPart), modo, loteSinPartes: ultima == 0);
        if (plan.Count == 0)
            return new Resultado(true, 0, ultima);

        var numeros = new short[plan.Count];
        var pdfCounts = new int[plan.Count];
        var omittedCounts = new int[plan.Count];
        var itemIds = new List<Guid>();
        var itemPartes = new List<short>();
        for (var k = 0; k < plan.Count; k++)
        {
            var numero = checked((short)(ultima + k + 1));
            numeros[k] = numero;
            pdfCounts[k] = plan[k].Pdfs.Count;
            omittedCounts[k] = plan[k].Omitidos.Count;
            foreach (var id in plan[k].Pdfs.Concat(plan[k].Omitidos))
            {
                itemIds.Add(id);
                itemPartes.Add(numero);
            }
        }

        var pendiente = ConsolidadoExportPartStatus.Pendiente;
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO tramites.consolidado_export_batch_parts (batch_id, part_number, status, pdf_count, omitted_count)
            SELECT {batchId}, t.numero, {pendiente}, t.pdfs, t.omitidos
              FROM unnest({numeros}, {pdfCounts}, {omittedCounts}) AS t(numero, pdfs, omitidos)
            """,
            ct).ConfigureAwait(false);

        if (itemIds.Count > 0)
        {
            Guid[] ids = [.. itemIds];
            short[] partes = [.. itemPartes];
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE tramites.consolidado_export_batch_items i
                   SET part_number = t.parte,
                       updated_at = now()
                  FROM unnest({ids}, {partes}) AS t(id, parte)
                 WHERE i.id = t.id AND i.batch_id = {batchId} AND i.part_number IS NULL
                """,
                ct).ConfigureAwait(false);
        }

        var total = (short)(ultima + plan.Count);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE tramites.consolidado_export_batches
               SET parts_count = {total},
                   updated_at = now()
             WHERE id = {batchId}
            """,
            ct).ConfigureAwait(false);

        return new Resultado(true, plan.Count, total);
    }
}
