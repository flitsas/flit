namespace Flit.Diagnostico.Domain.Configuracion;

/// <summary>
/// Cuándo se le muestra al usuario el estimado de consultas de un lote (R-11, R-11b). En la base: <c>NO_MOSTRAR</c>,
/// <c>ANTES_DE_LANZAR</c> o <c>AL_FINALIZAR</c>.
/// </summary>
public enum EstimadoConsultas
{
    /// <summary>No se muestra (valor por defecto).</summary>
    NoMostrar,

    /// <summary>Antes de lanzar el lote.</summary>
    AntesDeLanzar,

    /// <summary>Al finalizar el lote.</summary>
    AlFinalizar,
}
