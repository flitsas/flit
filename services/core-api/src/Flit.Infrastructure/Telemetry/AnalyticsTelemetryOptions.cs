namespace Flit.Infrastructure.Telemetry;

/// <summary>
/// Opciones de la telemetría de uso (HU-A Reportes 2.0). Sección <c>AnalyticsTelemetry</c>
/// de appsettings. <see cref="Enabled"/> apaga la captura (middleware y endpoint batch)
/// sin desregistrar servicios; <see cref="RetentionDays"/> gobierna la limpieza diaria
/// del writer (borra eventos con <c>occurred_at</c> más viejos que la ventana).
/// </summary>
public sealed class AnalyticsTelemetryOptions
{
    public const string SectionName = "AnalyticsTelemetry";

    public bool Enabled { get; set; } = true;

    public int RetentionDays { get; set; } = 90;
}
