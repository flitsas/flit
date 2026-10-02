namespace Flit.Tramites.Domain.Tramites.Estados;

/// <summary>
/// Bug #13194 (P4, D2) — puerto del gate de firma: «no se permite enviar al OT trámites sin firmar».
/// Lo implementa la capa de aplicación de Trámites con la MISMA regla que usa el ciclo de vida
/// (<see cref="ITramiteLifecycleService"/>), para que los módulos que deciden un envío fuera de una
/// transición (p. ej. el encolado de Quipux) no reimplementen la resolución de identidad.
/// </summary>
public interface ITramiteFirmaGate
{
    /// <summary>
    /// Partes (<c>comprador</c>/<c>vendedor</c>) que deben firmar el trámite y aún no tienen identidad
    /// aprobada y vigente. Lista vacía = el trámite está firmado. Un trámite inexistente en el tenant
    /// devuelve lista vacía: el llamador ya resolvió la existencia y no es este gate quien la decide.
    /// </summary>
    Task<IReadOnlyList<string>> PartesSinFirmaAsync(Guid instanceId, Guid tenantId, CancellationToken ct = default);
}
