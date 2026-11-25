using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Domain.Liquidaciones;

namespace Flit.Impuestos.Api.Modulos.Resultados;

/// <summary>Lo que cuenta de un ítem para los contadores de su lote: el estado y, si es exitoso, <c>con_deuda</c> o <c>paz_y_salvo</c>.</summary>
public readonly record struct EstadoParaContar(EstadoItem Estado, string? ResultadoStatus);

/// <summary>
/// Contadores de la cabecera de una liquidación (plan §3.2 «Cuenta en el dashboard como» y §3.3), recalculados desde los estados
/// de TODOS sus ítems, nunca sumando o restando: así un resultado tardío o un attempt que reemplaza a otro no los descuadra. Después
/// evalúa el cierre con <see cref="CierreLote.Evaluar"/>: un lote en proceso con todos sus ítems en estado final termina (y guarda
/// <c>terminada_en</c>); uno terminado o cerrado a mano no cambia de estado. El correo y el aviso del lote terminado son de M12.
/// </summary>
/// <example>
/// <code>
/// ContadoresLote.Recalcular(lote, items.Select(i => new EstadoParaContar(i.Estado, i.ResultadoStatus)).ToList(), ahora);
/// </code>
/// </example>
public static class ContadoresLote
{
    public const string ConDeuda = "con_deuda";
    public const string PazYSalvo = "paz_y_salvo";

    /// <summary>Recalcula los contadores y el estado; devuelve el estado anterior (para quien deba avisar al terminar).</summary>
    public static EstadoLote Recalcular(Liquidacion lote, IReadOnlyCollection<EstadoParaContar> items, DateTimeOffset ahora)
    {
        ArgumentNullException.ThrowIfNull(lote);
        ArgumentNullException.ThrowIfNull(items);
        var exitosos = items.Where(i => i.Estado == EstadoItem.Exitoso).ToList();
        lote.CExitosas = exitosos.Count;
        lote.CConDeuda = exitosos.Count(i => i.ResultadoStatus == ConDeuda);
        lote.CPazYSalvo = exitosos.Count(i => i.ResultadoStatus == PazYSalvo);
        lote.CError = Contar(items, CategoriaDashboard.Error);
        lote.CDatosIncompletos = Contar(items, CategoriaDashboard.DatosIncompletos);
        lote.CDptoNoHabilitado = Contar(items, CategoriaDashboard.DepartamentoNoHabilitado);
        lote.CIncierto = Contar(items, CategoriaDashboard.Incierto);
        lote.CSinResultado = Contar(items, CategoriaDashboard.SinResultado);
        lote.CPendientes = Contar(items, CategoriaDashboard.Pendiente);

        var anterior = lote.Estado;
        lote.Estado = CierreLote.Evaluar(anterior, items.Select(i => i.Estado).ToList());
        if (CierreLote.EsTerminal(lote.Estado) && !CierreLote.EsTerminal(anterior))
            lote.TerminadaEn ??= ahora;
        return anterior;
    }

    private static int Contar(IReadOnlyCollection<EstadoParaContar> items, CategoriaDashboard categoria) =>
        items.Count(i => EstadosItem.Categoria(i.Estado) == categoria);
}
