namespace Flit.Analytics.Application.Abstractions;

/// <summary>Métrica agregada de un paso del wizard (telemetría HU-A, Reportes 2.0).</summary>
public sealed record WizardStepMetricDto(
    string StepKey,
    int Views,
    int Completions,
    double AbandonmentPct,
    double? AvgDurationMs,
    double? MedianDurationMs);

/// <summary>Uso agregado de un módulo del aplicativo.</summary>
public sealed record ModuleUsageDto(string Module, int Events, int UniqueUsers);

/// <summary>Celda del heatmap de horas pico (hora America/Bogota).</summary>
public sealed record PeakHourDto(int DayOfWeek, int Hour, int Events);

/// <summary>Duración total del wizard por instancia completada.</summary>
public sealed record WizardDurationDto(double? AvgDurationMs, double? MedianDurationMs);
