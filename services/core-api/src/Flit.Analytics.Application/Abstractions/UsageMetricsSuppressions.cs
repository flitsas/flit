// Reportes2 HU-B: IUsageMetricsReadRepository es un archivo VERBATIM del contrato compartido
// (docs/contratos-reportes-v2.md §6, byte-idéntico entre HU-A y HU-B) y sus parámetros se llaman
// `from`/`to` por contrato. CA1716 ("to" es palabra reservada de VB) se suprime aquí, en un
// archivo aparte, para no modificar el archivo verbatim.
using System.Diagnostics.CodeAnalysis;

[assembly: SuppressMessage("Naming", "CA1716:Identifiers should not match keywords",
    Justification = "Firma fijada por el contrato compartido Reportes 2.0 (§6, archivo verbatim).",
    Scope = "member",
    Target = "~M:Flit.Analytics.Application.Abstractions.IUsageMetricsReadRepository.GetWizardStepMetricsAsync(System.Guid,System.DateOnly,System.DateOnly,System.Threading.CancellationToken)")]
[assembly: SuppressMessage("Naming", "CA1716:Identifiers should not match keywords",
    Justification = "Firma fijada por el contrato compartido Reportes 2.0 (§6, archivo verbatim).",
    Scope = "member",
    Target = "~M:Flit.Analytics.Application.Abstractions.IUsageMetricsReadRepository.GetModuleUsageAsync(System.Guid,System.DateOnly,System.DateOnly,System.Threading.CancellationToken)")]
[assembly: SuppressMessage("Naming", "CA1716:Identifiers should not match keywords",
    Justification = "Firma fijada por el contrato compartido Reportes 2.0 (§6, archivo verbatim).",
    Scope = "member",
    Target = "~M:Flit.Analytics.Application.Abstractions.IUsageMetricsReadRepository.GetPeakHoursAsync(System.Guid,System.DateOnly,System.DateOnly,System.Threading.CancellationToken)")]
[assembly: SuppressMessage("Naming", "CA1716:Identifiers should not match keywords",
    Justification = "Firma fijada por el contrato compartido Reportes 2.0 (§6, archivo verbatim).",
    Scope = "member",
    Target = "~M:Flit.Analytics.Application.Abstractions.IUsageMetricsReadRepository.GetWizardDurationAsync(System.Guid,System.DateOnly,System.DateOnly,System.Threading.CancellationToken)")]
