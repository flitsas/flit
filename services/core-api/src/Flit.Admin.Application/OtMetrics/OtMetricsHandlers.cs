using Flit.Admin.Domain.OtMetrics;
using Flit.Admin.Domain.RejectionReasons;

namespace Flit.Admin.Application.OtMetrics;

/// <summary>
/// Validación del rango de los reportes del organismo. Mismo criterio que los reportes de empresa:
/// rango obligatorio, coherente y acotado, con mensajes en español.
/// </summary>
public static class OtMetricsQueryValidation
{
    /// <summary>Tope de rango. Un año cubre cualquier comparación razonable sin abrir la puerta a barridos.</summary>
    public const int MaxRangeDays = 366;

    public static string? Validate(DateOnly? from, DateOnly? to)
    {
        if (from is null || to is null)
        {
            return "Los parámetros 'from' y 'to' son obligatorios (formato AAAA-MM-DD).";
        }

        if (to < from)
        {
            return "El rango es inválido: 'to' no puede ser anterior a 'from'.";
        }

        return to.Value.DayNumber - from.Value.DayNumber > MaxRangeDays
            ? $"El rango no puede superar {MaxRangeDays} días."
            : null;
    }
}

/// <summary>
/// Reportes del organismo. Los tres casos de uso comparten forma: validar rango → resolver el
/// organismo (o el override de SuperAdmin) → devolver el reporte, o <c>null</c> si el tenant no
/// tiene organismo asociado (el endpoint lo traduce a 404 con mensaje accionable).
/// </summary>
public sealed class GetOtOperationalPanelHandler(IOtMetricsReadRepository repository)
{
    public Task<OtOperationalPanelDto?> HandleAsync(
        Guid otTenantId,
        OtMetricsFilter filter,
        Guid? transitOfficeIdOverride,
        CancellationToken cancellationToken = default) =>
        repository.GetOperationalPanelAsync(
            otTenantId, filter, transitOfficeIdOverride, cancellationToken);
}

public sealed class GetOtPerformanceHandler(IOtMetricsReadRepository repository)
{
    public Task<OtPerformanceDto?> HandleAsync(
        Guid otTenantId,
        OtMetricsFilter filter,
        Guid? transitOfficeIdOverride,
        CancellationToken cancellationToken = default) =>
        repository.GetPerformanceAsync(
            otTenantId, filter, transitOfficeIdOverride, cancellationToken);
}

/// <summary>
/// Motivos de rechazo del organismo. Además del reparto, expone el catálogo vigente de la modalidad
/// para que la consola pueda mostrar en cero las causales que NO se usaron: una causal que nunca
/// aparece es información (o sobra en el catálogo, o nadie la está marcando).
/// </summary>
public sealed class GetOtRejectionReasonsHandler(
    IOtMetricsReadRepository repository,
    IRejectionReasonRepository catalog)
{
    public async Task<OtRejectionReasonsDto?> HandleAsync(
        Guid otTenantId,
        OtMetricsFilter filter,
        Guid? transitOfficeIdOverride,
        CancellationToken cancellationToken = default)
    {
        var result = await repository
            .GetRejectionReasonsAsync(otTenantId, filter, transitOfficeIdOverride, cancellationToken)
            .ConfigureAwait(false);

        if (result is null || string.IsNullOrWhiteSpace(filter.Modalidad))
        {
            return result;
        }

        var vigentes = await catalog
            .ListAsync(filter.Modalidad, includeInactive: false, cancellationToken)
            .ConfigureAwait(false);

        var presentes = result.Causales.Select(c => c.ReasonId).ToHashSet();
        var faltantes = vigentes
            .Where(v => !presentes.Contains(v.Id))
            .Select(v => new OtRejectionReasonStatDto(v.Id, v.Code, v.Description, 0, 0));

        return result with { Causales = [.. result.Causales, .. faltantes] };
    }
}

/// <summary>
/// Abre un bloque del panel: devuelve los trámites que lo componen. El límite lo fija el endpoint;
/// el DTO informa cuántos quedaron fuera para que la consola no presente una lista recortada como
/// si fuera el total.
/// </summary>
public sealed class GetOtDrilldownHandler(IOtMetricsReadRepository repository)
{
    /// <summary>Tope de filas por bloque. Suficiente para trabajar la cola sin volcar la base entera.</summary>
    public const int MaxItems = 100;

    public Task<OtDrilldownDto?> HandleAsync(
        Guid otTenantId,
        OtMetricsFilter filter,
        string bucket,
        Guid? transitOfficeIdOverride,
        CancellationToken cancellationToken = default) =>
        repository.GetDrilldownAsync(
            otTenantId, filter, bucket, MaxItems, transitOfficeIdOverride, cancellationToken);
}

/// <summary>
/// Informe del periodo. A diferencia del panel operativo —que describe el AHORA y se lee de un
/// vistazo—, el informe responde una pregunta cerrada sobre un rango: qué recibí, en qué acabó y
/// cuánto tardé. Por eso es el único reporte del organismo que devuelve detalle fila a fila.
/// </summary>
public sealed class GetOtReportHandler(IOtMetricsReadRepository repository)
{
    public Task<OtReportDto?> HandleAsync(
        Guid otTenantId,
        OtReportQuery query,
        Guid? transitOfficeIdOverride,
        CancellationToken cancellationToken = default) =>
        repository.GetReportAsync(otTenantId, query, transitOfficeIdOverride, cancellationToken);

    /// <summary>
    /// Normaliza paginación y orden. Un <c>sortBy</c> desconocido NO es un error: cae al orden por
    /// defecto. Rechazarlo con 400 rompería la consola cada vez que se retire una columna, y el
    /// usuario perdería el informe por un detalle que no eligió.
    /// </summary>
    public static OtReportQuery BuildQuery(
        OtMetricsFilter filter,
        int? page,
        int? pageSize,
        string? sortBy,
        bool descending) =>
        new(
            filter,
            Math.Max(1, page ?? 1),
            Math.Clamp(pageSize ?? OtReportLimits.DefaultPageSize, 1, OtReportLimits.MaxPageSize),
            OtReportSort.IsKnown(sortBy) ? sortBy : OtReportSort.Radicado,
            descending);
}

/// <summary>Empresas cliente del organismo para el filtro del reporte.</summary>
public sealed class ListOtClientCompaniesHandler(IOtMetricsReadRepository repository)
{
    public Task<IReadOnlyList<OtClientCompanyOptionDto>?> HandleAsync(
        Guid otTenantId,
        Guid? transitOfficeIdOverride,
        CancellationToken cancellationToken = default) =>
        repository.ListClientCompaniesAsync(otTenantId, transitOfficeIdOverride, cancellationToken);
}
