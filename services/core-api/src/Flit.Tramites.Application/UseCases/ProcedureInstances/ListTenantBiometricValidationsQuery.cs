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
    DateTimeOffset? ValidadoAt);

/// <summary>KPIs del submódulo: totales por estado (exactos, sin el cap de filas de la tabla).</summary>
public sealed record BiometricValidationStatsDto(
    int Total,
    int Aprobadas,
    int EnProceso,
    int Rechazadas,
    int Expiradas);

/// <summary>Respuesta del listado transversal: filas + KPIs agregados.</summary>
public sealed record TenantBiometricValidationsResponse(
    IReadOnlyList<TenantBiometricValidationDto> Validations,
    BiometricValidationStatsDto Stats);

/// <summary>
/// Lista las validaciones biométricas del tenant (todas las instancias) para el submódulo de
/// Validaciones de Identidad (HU #10234, filtros HU #10347). La tabla se acota a
/// <see cref="MaxRows"/> filas; los KPIs se calculan con un conteo agrupado aparte para que sean
/// exactos aunque la tabla esté acotada. Con filtros activos, filas y KPIs reflejan el mismo subconjunto.
/// Reusa <see cref="IniciarBiometriaHandler.ExtractMotivoRechazo"/> para el motivo SANITIZADO (sin PII).
/// </summary>
public sealed class ListTenantBiometricValidationsHandler(IProcedureInstanceRepository repo)
{
    /// <summary>Cap de filas de la tabla (vista de monitoreo). Los KPIs no dependen de este cap.</summary>
    public const int MaxRows = 500;

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
        var now = DateTimeOffset.UtcNow;

        var rows = await repo.ListBiometricValidationsByTenantAsync(tenantId, MaxRows, activeFilter, now, ct);

        var dtos = rows.Select(v => new TenantBiometricValidationDto(
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
            v.ValidadoAt)).ToList();

        // motivoRechazo se filtra EN MEMORIA (no en SQL): Detalle/ProviderPayload son columnas jsonb y
        // Postgres no soporta ILIKE sobre jsonb. Se compara contra el MISMO texto sanitizado que se muestra
        // en la columna (ExtractMotivoRechazo, ya calculado en el DTO); así filtrar por lo que ve el gestor
        // también funciona para Kyverum (motivo derivado, no literal del payload).
        if (!string.IsNullOrWhiteSpace(filter.MotivoRechazo))
        {
            var term = filter.MotivoRechazo;
            dtos = dtos
                .Where(d => d.MotivoRechazo is not null
                    && d.MotivoRechazo.Contains(term, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        // KPIs: sin filtro de motivo, conteo exacto agrupado en BD (independiente del cap de filas). Con
        // filtro de motivo (sólo aplica a rechazadas y se resuelve en memoria), los KPIs se derivan del
        // subconjunto efectivamente mostrado para que filas y KPIs sean coherentes.
        var stats = string.IsNullOrWhiteSpace(filter.MotivoRechazo)
            ? BuildStats(await repo.CountBiometricValidationsByEstadoAsync(tenantId, activeFilter, now, ct))
            : BuildStatsFromRows(dtos);

        return (new TenantBiometricValidationsResponse(dtos, stats), null);
    }

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
