using Flit.Impuestos.Api.Modulos.Avisos;
using Flit.Impuestos.Api.Modulos.Resultados;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Domain.Liquidaciones;

namespace Flit.Impuestos.Api.Modulos.Liquidaciones;

/// <summary>
/// Terminación del lote (plan §3.3; F3-CF5, F3-CF12) con una sola fuente de verdad:
/// <list type="number">
///   <item>Los contadores y el estado salen siempre de <see cref="ContadoresLote.Recalcular"/> (M10), que usa
///   <see cref="CierreLote.Evaluar"/>: todos los ítems en estado final (incluido <c>INCIERTO_REQUIERE_DECISION</c>) y todos
///   <c>EXITOSO</c> ⇒ <c>COMPLETADO</c>; con alguno que no ⇒ <c>COMPLETADO_CON_NOVEDADES</c>. Este evaluador no repite esa regla.</item>
///   <item>Lo que suma es el aviso: si el lote pasó de no terminal a terminal (<see cref="CierreLote.DebeAvisar"/>, una sola vez
///   gracias a <c>aviso_enviado_en</c>), lo emite <see cref="AvisoLoteTerminado"/> en el mismo contexto y la misma transacción.</item>
/// </list>
/// Lo llaman <see cref="AplicadorResultado"/> tras cada cambio de ítem (webhook, conciliación, consumidor) y
/// <see cref="CerrarLiquidacion"/>, que pasa el estado anterior porque ya dejó el lote en <c>CERRADO_MANUALMENTE</c>. No guarda: guarda
/// quien abrió la transacción.
/// </summary>
/// <example>
/// <code>
/// var anterior = await evaluador.EvaluarAsync(lote, estados, ahora, ct);
/// await db.SaveChangesAsync(ct);
/// </code>
/// </example>
public sealed class EvaluadorTerminacion(AvisoLoteTerminado aviso)
{
    /// <summary>
    /// Recalcula contadores y estado del lote y, si acaba de terminar, emite el aviso. <paramref name="anterior"/> es el estado de
    /// antes del cambio cuando quien llama ya lo movió (cierre manual); si es null, el estado actual del lote. Devuelve el anterior.
    /// </summary>
    public async Task<EstadoLote> EvaluarAsync(
        Liquidacion lote, IReadOnlyCollection<EstadoParaContar> items, DateTimeOffset ahora, CancellationToken ct, EstadoLote? anterior = null,
        string? nombreEmpresa = null)
    {
        ArgumentNullException.ThrowIfNull(lote);
        ArgumentNullException.ThrowIfNull(items);
        var desde = anterior ?? lote.Estado;
        ContadoresLote.Recalcular(lote, items, ahora);
        if (CierreLote.DebeAvisar(desde, lote.Estado, lote.AvisoEnviadoEn is not null))
            await aviso.EmitirAsync(lote, ahora, nombreEmpresa, ct).ConfigureAwait(false);
        return desde;
    }
}
