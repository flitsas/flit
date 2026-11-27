namespace Flit.Impuestos.Domain.Liquidaciones;

/// <summary>
/// Estado de la cabecera de una liquidación (plan §3.3). La individual es un lote de un ítem (supuesto S-A). En la base se
/// guarda en mayúsculas con guion bajo (<c>COMPLETADO_CON_NOVEDADES</c>).
/// </summary>
public enum EstadoLote
{
    /// <summary>Recién creada; ningún ítem despachado.</summary>
    Creado,

    /// <summary>Al menos un ítem despachado y alguno sin estado final.</summary>
    EnProceso,

    /// <summary>Todos los ítems terminaron y todos son <c>EXITOSO</c>.</summary>
    Completado,

    /// <summary>Todos los ítems terminaron y al menos uno no es <c>EXITOSO</c>.</summary>
    CompletadoConNovedades,

    /// <summary>Un Admin o el Super Admin la cerró antes de terminar.</summary>
    CerradoManualmente,
}
