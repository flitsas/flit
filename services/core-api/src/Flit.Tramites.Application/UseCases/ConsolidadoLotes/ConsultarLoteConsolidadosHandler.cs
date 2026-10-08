using Flit.Tramites.Domain.Entities.ConsolidadoLotes;

namespace Flit.Tramites.Application.UseCases.ConsolidadoLotes;

/// <summary><c>GET /api/v1/consolidados/lotes/actual</c>: el dueño sale del token (<c>sub</c>), nunca del cliente.</summary>
public sealed record ObtenerLoteActualQuery(Guid UsuarioId);

/// <summary><c>GET /api/v1/consolidados/lotes/{loteId}</c>.</summary>
public sealed record ObtenerLoteQuery(Guid LoteId, Guid UsuarioId);

/// <summary>Lote consultado con sus partes descargables (vacío mientras no sea descargable).</summary>
/// <param name="Lote">El lote del dueño.</param>
/// <param name="Partes">Partes <c>cerrada</c> de un lote descargable, por número; vacío en cualquier otro caso.</param>
/// <param name="NombreBase"><c>consolidados_{yyyyMMdd_HHmm}</c> (hora de Colombia de la creación).</param>
public sealed record LoteConsultado(ConsolidadoExportBatch Lote, IReadOnlyList<ParteConsultada> Partes, string NombreBase);

/// <summary>Una parte descargable: número, nombre del ZIP (CF-13), PDF, omitidos y bytes en claro (<c>Content-Length</c>).</summary>
public sealed record ParteConsultada(int Numero, string NombreArchivo, int Pdfs, int Omitidos, long Bytes);

/// <summary>
/// HU #13379 (Épica #13216, ADR-0070 D7, CF-06/CF-11/CF-12) — <c>ObtenerLoteActualQuery</c> y la consulta por id.
/// <list type="bullet">
///   <item>Solo el dueño (<c>sub</c>): otro usuario, incluido un Super Admin, recibe <c>null</c> (404/204 en la API).</item>
///   <item><c>actual</c>: el lote activo o, si no hay, el último terminal todavía retenido (con su <c>expiraEn</c>) o el
///   último <c>cancelado</c> (HU #13386 AC6, hasta que el usuario crea otro lote); un lote purgado por retención no se
///   devuelve (204).</item>
///   <item>Partes: solo cuando el lote es descargable (<see cref="ConsolidadoLoteDescargabilidad"/>); cada una con su
///   nombre (<see cref="ConsolidadoLoteNombres.Zip"/>) y su tamaño en claro.</item>
/// </list>
/// </summary>
/// <remarks>
/// Uso de ejemplo:
/// <code>
/// var r = await handler.ActualAsync(new ObtenerLoteActualQuery(usuarioId), ct); // null ⇒ 204
/// </code>
/// </remarks>
public sealed class ConsultarLoteConsolidadosHandler(IConsolidadoLoteLectura lectura, TimeProvider? reloj = null)
{
    private readonly TimeProvider _reloj = reloj ?? TimeProvider.System;

    public async Task<LoteConsultado?> ActualAsync(ObtenerLoteActualQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var lote = await lectura.ObtenerActualDelDuenoAsync(query.UsuarioId, ct).ConfigureAwait(false);
        return lote is null ? null : await ConPartesAsync(lote, ct).ConfigureAwait(false);
    }

    public async Task<LoteConsultado?> PorIdAsync(ObtenerLoteQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var lote = await lectura.ObtenerDelDuenoAsync(query.LoteId, query.UsuarioId, ct).ConfigureAwait(false);
        return lote is null ? null : await ConPartesAsync(lote, ct).ConfigureAwait(false);
    }

    /// <summary><c>consolidados_{yyyyMMdd_HHmm}</c>: el nombre de una sola parte sin la extensión.</summary>
    public static string NombreBase(ConsolidadoExportBatch lote)
    {
        ArgumentNullException.ThrowIfNull(lote);
        var zip = ConsolidadoLoteNombres.Zip(lote.CreatedAt, 1, 1);
        return zip[..^".zip".Length];
    }

    private async Task<LoteConsultado> ConPartesAsync(ConsolidadoExportBatch lote, CancellationToken ct)
    {
        if (ConsolidadoLoteDescargabilidad.Evaluar(lote, _reloj.GetUtcNow()) != ConsolidadoLoteDescargable.Si)
            return new LoteConsultado(lote, [], NombreBase(lote));

        var cerradas = (await lectura.ObtenerPartesAsync(lote.Id, ct).ConfigureAwait(false))
            .Where(p => p.Status == ConsolidadoExportPartStatus.Cerrada)
            .OrderBy(p => p.PartNumber)
            .ToList();
        if (cerradas.Count == 0)
            return new LoteConsultado(lote, [], NombreBase(lote));

        // El total del nombre es parts_count; defensivo por si una parte supera ese número.
        var total = Math.Max(lote.PartsCount, cerradas.Max(p => (int)p.PartNumber));
        var partes = cerradas
            .Select(p => new ParteConsultada(
                p.PartNumber,
                ConsolidadoLoteNombres.Zip(lote.CreatedAt, p.PartNumber, total),
                p.PdfCount,
                p.OmittedCount,
                p.PlainSizeBytes ?? 0))
            .ToList();
        return new LoteConsultado(lote, partes, NombreBase(lote));
    }
}
