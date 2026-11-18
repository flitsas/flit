namespace Flit.Diagnostico.Api.Modulos.Reportes.Catalogo;

/// <summary>
/// Límites del módulo de reportes con PII (HU #14579, SP-11, SP-12, diseno.md «Notas operativas»), sección
/// <c>Diagnostico:Reportes</c>. Todos opcionales y con valor por defecto: Excel hasta 5 000 filas, PDF hasta 1 000, una exportación
/// simultánea por usuario y cuatro por proceso, y 30 s de tiempo de comando de EF para las consultas de reportes.
/// </summary>
/// <example>
/// <code>
/// Diagnostico__Reportes__MaximoFilasExcel=5000
/// </code>
/// </example>
internal sealed class OpcionesReportes
{
    public const string Seccion = "Diagnostico:Reportes";

    public int MaximoFilasExcel { get; set; } = 5000;

    public int MaximoFilasPdf { get; set; } = 1000;

    public int ExportacionesPorUsuario { get; set; } = 1;

    public int ExportacionesPorProceso { get; set; } = 4;

    public int TimeoutSegundos { get; set; } = 30;

    /// <summary>Todos los valores deben ser positivos.</summary>
    public bool Validos() =>
        MaximoFilasExcel > 0 && MaximoFilasPdf > 0 && ExportacionesPorUsuario > 0 && ExportacionesPorProceso > 0 && TimeoutSegundos > 0;

    /// <summary>Máximo de filas del formato de archivo.</summary>
    public int Maximo(FormatoReporte formato) => formato switch
    {
        FormatoReporte.Xlsx => MaximoFilasExcel,
        FormatoReporte.Pdf => MaximoFilasPdf,
        _ => throw new ArgumentOutOfRangeException(nameof(formato), "Solo los archivos tienen máximo de filas."),
    };
}
