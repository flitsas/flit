namespace Flit.Diagnostico.Domain.Diagnosticos;

/// <summary>
/// Qué provocó un cambio de estado del lote (plan §5, R-8). En la base: <c>AUTO</c>, <c>CIERRE_PANEL</c>,
/// <c>REPROCESO</c> o <c>MANUAL</c>.
/// </summary>
public enum DisparadorEstado
{
    /// <summary>El procesador, sin usuario.</summary>
    Auto,

    /// <summary>El usuario cerró el panel.</summary>
    CierrePanel,

    /// <summary>Reproceso de un admin o del backoffice.</summary>
    Reproceso,

    /// <summary>Llenado manual del backoffice.</summary>
    Manual,
}
