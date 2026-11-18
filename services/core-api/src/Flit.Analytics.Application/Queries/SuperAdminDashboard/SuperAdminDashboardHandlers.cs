using Flit.Analytics.Application.Queries.Metrics;

namespace Flit.Analytics.Application.Queries.SuperAdminDashboard;

/// <summary>«Resumen de compañías activas» del Inicio del SuperAdmin. From/To opcionales (BUG #12588).</summary>
public sealed record SuperAdminDashboardCompaniesQuery(DateOnly? From, DateOnly? To, int? StuckDays = null)
{
    /// <summary>HU #13664 — Ver por familia; <c>null</c> = sin restricción.</summary>
    public IReadOnlyList<string>? AllowedFamilies { get; init; }
}

/// <summary>«Rendimiento por Organismo de Tránsito» y «Tiempo promedio ecosistema». <c>TenantId</c> null = todas.</summary>
public sealed record SuperAdminDashboardOfficesQuery(Guid? TenantId, DateOnly? From, DateOnly? To)
{
    /// <summary>HU #13664 — Ver por familia; <c>null</c> = sin restricción.</summary>
    public IReadOnlyList<string>? AllowedFamilies { get; init; }
}

/// <summary>Errores: <c>invalid_range</c> (From &gt; To) e <c>invalid_stuck_days</c> (fuera de 1..90).</summary>
public sealed class SuperAdminDashboardCompaniesHandler(ISuperAdminDashboardReadRepository repo)
{
    public async Task<(SuperAdminDashboardCompaniesDto? Result, string? Error)> HandleAsync(
        SuperAdminDashboardCompaniesQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (SuperAdminDashboardRange.IsInverted(query.From, query.To))
            return (null, "invalid_range");
        if (!MetricsQueryValidation.IsValidStuckDays(query.StuckDays))
            return (null, "invalid_stuck_days");

        var stuckDays = query.StuckDays ?? MetricsQueryValidation.DefaultStuckDays;
        var items = await repo.GetCompaniesAsync(query.From, query.To, stuckDays, query.AllowedFamilies, ct)
            .ConfigureAwait(false);
        return (new SuperAdminDashboardCompaniesDto(query.From, query.To, stuckDays, items), null);
    }
}

/// <summary>Error: <c>invalid_range</c> (From &gt; To).</summary>
public sealed class SuperAdminDashboardOfficesHandler(ISuperAdminDashboardReadRepository repo)
{
    public async Task<(SuperAdminDashboardOfficesDto? Result, string? Error)> HandleAsync(
        SuperAdminDashboardOfficesQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (SuperAdminDashboardRange.IsInverted(query.From, query.To))
            return (null, "invalid_range");

        // Guid.Empty no es una compañía: se lee como «todas», igual que el centinela del overview.
        var tenant = query.TenantId is { } t && t != Guid.Empty ? t : (Guid?)null;
        var result = await repo.GetOfficesAsync(tenant, query.From, query.To, query.AllowedFamilies, ct)
            .ConfigureAwait(false);
        return (result, null);
    }
}

internal static class SuperAdminDashboardRange
{
    /// <summary>Solo hay rango que validar con los dos extremos: uno suelto acota por ese lado.</summary>
    public static bool IsInverted(DateOnly? from, DateOnly? to) => from is { } f && to is { } t && f > t;
}
