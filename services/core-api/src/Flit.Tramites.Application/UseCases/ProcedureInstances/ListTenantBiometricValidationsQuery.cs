using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

// ── DTOs del submódulo "Validaciones de Identidad" (HU #10234) ───────────────

/// <summary>
/// Fila de la tabla transversal del submódulo de Validaciones: una validación biométrica con el
/// trámite al que pertenece (para navegar) y los datos que la vista del gestor necesita. NO incluye
/// email ni la URL de captura (vista de monitoreo, no de gestión de la captura). El documento viaja
/// completo (vista autenticada del gestor del tenant); la FE lo enmascara al pintarlo.
/// </summary>
public sealed record TenantBiometricValidationDto(
    Guid Id,
    Guid InstanceId,
    string ReferenceNumber,
    string Modalidad,
    string? Parte,
    string Nombre,
    string TipoDoc,
    string Documento,
    string Estado,
    int? Score,
    string Provider,
    bool Expired,
    string? MotivoRechazo,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ValidadoAt,
    // Vigencia de la identidad APROBADA (30 días calendario desde la aprobación): fecha de fin de
    // vigencia y días que le restan. Null cuando no hay aprobación (ValidadoAt) → no aplica vigencia.
    DateTimeOffset? VigenciaHasta,
    int? DiasRestantes);

/// <summary>KPIs del submódulo: totales por estado (exactos, sin el cap de filas de la tabla).</summary>
public sealed record BiometricValidationStatsDto(
    int Total,
    int Aprobadas,
    int EnProceso,
    int Rechazadas,
    int Expiradas);

/// <summary>
/// Respuesta del listado transversal: filas de la PÁGINA pedida + KPIs agregados de TODO el conjunto
/// filtrado + metadatos de paginación. <c>Total</c> es el total filtrado (para calcular el nº de páginas);
/// los KPIs (<see cref="Stats"/>) siguen siendo del conjunto completo, no solo de la página.
/// </summary>
public sealed record TenantBiometricValidationsResponse(
    IReadOnlyList<TenantBiometricValidationDto> Validations,
    BiometricValidationStatsDto Stats,
    int Page,
    int PageSize,
    int Total);

/// <summary>
/// Lista PAGINADA de las validaciones biométricas del tenant (todas las instancias) para el submódulo de
/// Validaciones de Identidad (HU #10234, filtros HU #10347, paginación). Devuelve solo la página pedida
/// (server-side: <c>Skip/Take</c>), con los KPIs calculados con un conteo agrupado aparte para que sean
/// exactos sobre TODO el conjunto filtrado (no solo la página) y el <c>Total</c> para el paginador.
/// Reusa <see cref="IniciarBiometriaHandler.ExtractMotivoRechazo"/> para el motivo SANITIZADO (sin PII).
/// </summary>
public sealed class ListTenantBiometricValidationsHandler(IProcedureInstanceRepository repo)
{
    // Cap de escaneo en memoria SOLO para el filtro motivoRechazo (jsonb, no filtrable/paginable en SQL):
    // se trae un lote acotado de rechazadas, se filtra y se pagina en memoria.
    private const int MotivoScanCap = 2000;

    public async Task<(TenantBiometricValidationsResponse? Result, string? Error)> HandleAsync(
        Guid tenantId,
        TenantBiometricValidationListQuery? query = null,
        CancellationToken ct = default)
    {
        query ??= new TenantBiometricValidationListQuery();
        var validationError = query.Validate();
        if (validationError is not null)
            return (null, validationError);

        var filter = query.ToFilter();
        var activeFilter = filter.HasActiveFilters ? filter : null;
        var page = query.SafePage();
        var pageSize = query.SafePageSize();
        var now = DateTimeOffset.UtcNow;

        // Caso motivoRechazo: se resuelve EN MEMORIA (Detalle/ProviderPayload son jsonb y Postgres no soporta
        // ILIKE sobre jsonb). La UI sólo muestra este filtro con estado=rechazado, así que el lote escaneado
        // ya viene acotado a rechazadas; se filtra por el texto sanitizado y se pagina en memoria.
        if (!string.IsNullOrWhiteSpace(filter.MotivoRechazo))
        {
            var scan = await repo.ListBiometricValidationsByTenantAsync(tenantId, 0, MotivoScanCap, activeFilter, now, ct);
            var term = filter.MotivoRechazo;
            var all = scan
                .Select(v => ToDto(v, now))
                .Where(d => d.MotivoRechazo is not null
                    && d.MotivoRechazo.Contains(term, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var pageDtos = all.Skip((page - 1) * pageSize).Take(pageSize).ToList();
            var statsMotivo = BuildStatsFromRows(all);
            return (new TenantBiometricValidationsResponse(pageDtos, statsMotivo, page, pageSize, all.Count), null);
        }

        // Caso general: KPIs + total exactos por conteo agrupado en BD; filas de la página por Skip/Take.
        var stats = BuildStats(await repo.CountBiometricValidationsByEstadoAsync(tenantId, activeFilter, now, ct));
        var rows = await repo.ListBiometricValidationsByTenantAsync(
            tenantId, (page - 1) * pageSize, pageSize, activeFilter, now, ct);
        var dtos = rows.Select(v => ToDto(v, now)).ToList();

        return (new TenantBiometricValidationsResponse(dtos, stats, page, pageSize, stats.Total), null);
    }

    /// <summary>Mapea una validación a su DTO de fila (incluye flag expirada + motivo sanitizado).</summary>
    private static TenantBiometricValidationDto ToDto(ProcedureInstanceBiometricValidation v, DateTimeOffset now) =>
        new(
            v.Id,
            v.ProcedureInstanceId,
            v.ProcedureInstance?.ReferenceNumber ?? string.Empty,
            v.ProcedureInstance?.ModalidadEntrada ?? string.Empty,
            v.Parte,
            v.Nombre,
            v.TipoDoc,
            v.Documento,
            v.Estado,
            v.Score,
            v.Provider,
            // Mismo criterio que BiometricValidationDto: expirada si no aprobada y ya pasó expires_at.
            v.Estado != BiometricEstados.Aprobado && now > v.ExpiresAt,
            IniciarBiometriaHandler.ExtractMotivoRechazo(v),
            v.CreatedAt,
            v.ValidadoAt,
            BiometricRules.FechaFinVigencia(v),
            BiometricRules.DiasRestantesVigencia(v, now));

    /// <summary>
    /// KPIs derivados de las filas ya materializadas (usado cuando el filtro de motivo se resuelve en
    /// memoria). "Expiradas" alinea con el flag <c>Expired</c> mostrado en la fila; "En proceso" agrupa
    /// enviado + en_proceso, igual que <see cref="BuildStats"/>.
    /// </summary>
    internal static BiometricValidationStatsDto BuildStatsFromRows(IReadOnlyList<TenantBiometricValidationDto> dtos) =>
        new(
            Total: dtos.Count,
            Aprobadas: dtos.Count(d => d.Estado == BiometricEstados.Aprobado),
            EnProceso: dtos.Count(d => d.Estado is BiometricEstados.Enviado or BiometricEstados.EnProceso),
            Rechazadas: dtos.Count(d => d.Estado == BiometricEstados.Rechazado),
            Expiradas: dtos.Count(d => d.Estado == BiometricEstados.Expirado || d.Expired));

    internal static BiometricValidationStatsDto BuildStats(IReadOnlyDictionary<string, int> counts) =>
        new(
            Total: counts.Values.Sum(),
            Aprobadas: counts.GetValueOrDefault(BiometricEstados.Aprobado),
            // "En proceso" agrupa enviado + en_proceso (ambos son trabajo en curso para el gestor).
            EnProceso: counts.GetValueOrDefault(BiometricEstados.Enviado)
                + counts.GetValueOrDefault(BiometricEstados.EnProceso),
            Rechazadas: counts.GetValueOrDefault(BiometricEstados.Rechazado),
            Expiradas: counts.GetValueOrDefault(BiometricEstados.Expirado));
}
