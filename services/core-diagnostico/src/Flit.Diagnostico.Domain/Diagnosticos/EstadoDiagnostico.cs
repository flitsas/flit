namespace Flit.Diagnostico.Domain.Diagnosticos;

/// <summary>
/// Estado del lote (plan §3.5, R-8). En la base: <c>CREADO</c>, <c>EN_PROCESO</c>, <c>COMPLETADO</c> o <c>INCOMPLETO</c>.
/// </summary>
public enum EstadoDiagnostico
{
    /// <summary>Cabecera guardada, sin vehículos encolados.</summary>
    Creado,

    /// <summary>Hay vehículos en proceso.</summary>
    EnProceso,

    /// <summary>Todas las celdas aplicables quedaron completas o N/A.</summary>
    Completado,

    /// <summary>El usuario cerró el panel con celdas en error.</summary>
    Incompleto,
}
