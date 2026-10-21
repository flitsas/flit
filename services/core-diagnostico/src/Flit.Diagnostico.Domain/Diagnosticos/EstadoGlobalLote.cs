using Flit.Diagnostico.Domain.Consultas;

namespace Flit.Diagnostico.Domain.Diagnosticos;

/// <summary>Si el usuario puede cerrar el panel de un lote (HU #13743, F04-C5) y, si no, por qué.</summary>
public enum ResultadoCierrePanel
{
    /// <summary>EN_PROCESO, todas las celdas terminadas y al menos una en ERROR: pasa a INCOMPLETO.</summary>
    Permitido,

    /// <summary>El lote no está EN_PROCESO (CREADO, COMPLETADO o INCOMPLETO).</summary>
    LoteNoEnProceso,

    /// <summary>Hay celdas PENDIENTE o EN_CURSO: el procesador todavía trabaja en el lote.</summary>
    CeldasSinTerminar,

    /// <summary>Todas las celdas terminaron sin ERROR: el lote se completa solo, no se cierra a mano.</summary>
    SinErrores,
}

/// <summary>
/// Estado global del lote a partir de sus celdas (HU #13743, F04-H1, plan §3.5, R-8): <c>EN_PROCESO → COMPLETADO</c> cuando
/// todas las celdas aplicables están en COMPLETADO o NA; con alguna en ERROR el lote sigue EN_PROCESO hasta que el usuario
/// cierra el panel, que lo deja INCOMPLETO. Un lote que no está EN_PROCESO no cambia aquí (CREADO lo mueve el encolado; la
/// vuelta de un lote cerrado a EN_PROCESO es del reproceso, R-8). Servicio puro: sin base ni HTTP.
/// </summary>
/// <example>
/// <code>
/// EstadoGlobalLote.Calcular(EstadoDiagnostico.EnProceso, [EstadoCelda.Completado, EstadoCelda.Na]);               // COMPLETADO
/// EstadoGlobalLote.Calcular(EstadoDiagnostico.EnProceso, [EstadoCelda.Completado, EstadoCelda.Error]);            // EN_PROCESO
/// EstadoGlobalLote.Calcular(EstadoDiagnostico.EnProceso, [EstadoCelda.Completado, EstadoCelda.Error], true);      // INCOMPLETO
/// </code>
/// </example>
public static class EstadoGlobalLote
{
    /// <summary>Estado del lote dadas sus celdas; <paramref name="cierrePanel"/> indica que el usuario cerró el panel.</summary>
    public static EstadoDiagnostico Calcular(EstadoDiagnostico actual, IEnumerable<EstadoCelda> celdas, bool cierrePanel = false)
    {
        ArgumentNullException.ThrowIfNull(celdas);
        if (actual != EstadoDiagnostico.EnProceso)
            return actual;

        var (total, sinTerminar, errores) = Contar(celdas);
        if (total == 0 || sinTerminar > 0)
            return EstadoDiagnostico.EnProceso;
        if (errores == 0)
            return EstadoDiagnostico.Completado;
        return cierrePanel ? EstadoDiagnostico.Incompleto : EstadoDiagnostico.EnProceso;
    }

    /// <summary>Si el panel del lote se puede cerrar con sus celdas actuales.</summary>
    public static ResultadoCierrePanel EvaluarCierrePanel(EstadoDiagnostico actual, IEnumerable<EstadoCelda> celdas)
    {
        ArgumentNullException.ThrowIfNull(celdas);
        if (actual != EstadoDiagnostico.EnProceso)
            return ResultadoCierrePanel.LoteNoEnProceso;

        var (total, sinTerminar, errores) = Contar(celdas);
        if (total == 0 || sinTerminar > 0)
            return ResultadoCierrePanel.CeldasSinTerminar;
        return errores == 0 ? ResultadoCierrePanel.SinErrores : ResultadoCierrePanel.Permitido;
    }

    private static (int Total, int SinTerminar, int Errores) Contar(IEnumerable<EstadoCelda> celdas)
    {
        int total = 0, sinTerminar = 0, errores = 0;
        foreach (var celda in celdas)
        {
            total++;
            if (celda is EstadoCelda.Pendiente or EstadoCelda.EnCurso)
                sinTerminar++;
            else if (celda == EstadoCelda.Error)
                errores++;
        }

        return (total, sinTerminar, errores);
    }
}
