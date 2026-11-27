namespace Flit.Impuestos.Domain.Liquidaciones;

/// <summary>
/// Reglas del estado del lote (plan §3.3), puras: despacho, terminación por los estados de sus ítems, cierre manual y cuándo
/// sale el aviso (una sola vez, al entrar en un estado terminal). Un resultado tardío no cambia un lote terminado: solo se
/// recalculan sus contadores.
/// </summary>
/// <example>
/// <code>
/// var nuevo = CierreLote.Evaluar(EstadoLote.EnProceso, items.Select(i => i.Estado).ToList());
/// if (CierreLote.DebeAvisar(EstadoLote.EnProceso, nuevo, lote.AvisoEnviadoEn is not null)) { /* correo + aviso */ }
/// </code>
/// </example>
public static class CierreLote
{
    /// <summary>Cerrar a mano una liquidación que ya terminó (409 de la API).</summary>
    public const string CodigoYaTerminada = "YA_TERMINADA";

    public static bool EsTerminal(EstadoLote estado) =>
        estado is EstadoLote.Completado or EstadoLote.CompletadoConNovedades or EstadoLote.CerradoManualmente;

    /// <summary>Se despachó un ítem: <c>CREADO</c> pasa a <c>EN_PROCESO</c>; un lote terminado no despacha.</summary>
    public static EstadoLote Despachar(EstadoLote estado) => estado switch
    {
        EstadoLote.Creado or EstadoLote.EnProceso => EstadoLote.EnProceso,
        _ => throw TransicionInvalida.FueraDeTabla(estado.ToString(), "despacho"),
    };

    /// <summary>
    /// <c>EN_PROCESO</c> con todos los ítems en estado final (incluido <c>INCIERTO_REQUIERE_DECISION</c>) termina:
    /// <c>COMPLETADO</c> si todos son <c>EXITOSO</c>, si no <c>COMPLETADO_CON_NOVEDADES</c>. En cualquier otro caso no cambia.
    /// </summary>
    public static EstadoLote Evaluar(EstadoLote estado, IReadOnlyCollection<EstadoItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (estado != EstadoLote.EnProceso || items.Count == 0 || !items.All(EstadosItem.EsFinal))
            return estado;
        return items.All(i => i == EstadoItem.Exitoso) ? EstadoLote.Completado : EstadoLote.CompletadoConNovedades;
    }

    /// <summary>Cierre manual: solo antes de terminar; un lote terminado responde <see cref="CodigoYaTerminada"/>.</summary>
    public static EstadoLote CerrarManualmente(EstadoLote estado) =>
        EsTerminal(estado)
            ? throw TransicionInvalida.Con(CodigoYaTerminada, "La liquidación ya terminó.")
            : EstadoLote.CerradoManualmente;

    /// <summary>El correo y el aviso en pantalla salen una vez: al pasar de no terminal a terminal, si no se enviaron ya.</summary>
    public static bool DebeAvisar(EstadoLote anterior, EstadoLote nuevo, bool avisoYaEnviado) =>
        !avisoYaEnviado && !EsTerminal(anterior) && EsTerminal(nuevo);
}
