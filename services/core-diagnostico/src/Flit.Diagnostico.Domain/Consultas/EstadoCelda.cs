namespace Flit.Diagnostico.Domain.Consultas;

/// <summary>
/// Estado de una celda del semáforo (<c>diagnostico.consultas.estado</c>, plan §3.5 y §5, HU #13723): <c>PENDIENTE → EN_CURSO →
/// COMPLETADO | ERROR | NA</c>, con <c>ERROR → PENDIENTE</c> (reproceso) o <c>ERROR → COMPLETADO</c> (manual). <see cref="EnCurso"/>
/// distingue la celda tomada por el procesador (con lease) de la pendiente.
/// </summary>
public enum EstadoCelda
{
    /// <summary>En la cola, esperando que el procesador la tome.</summary>
    Pendiente,

    /// <summary>Tomada por el procesador, con lease vigente.</summary>
    EnCurso,

    /// <summary>Con dato, consultado o llenado a mano.</summary>
    Completado,

    /// <summary>Falló; se puede reprocesar.</summary>
    Error,

    /// <summary>No aplica; siempre con <see cref="MotivoNa"/>.</summary>
    Na,
}
