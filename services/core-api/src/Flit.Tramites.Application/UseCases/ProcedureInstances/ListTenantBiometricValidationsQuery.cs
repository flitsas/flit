using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

// ── DTOs del submódulo "Validaciones de Identidad" (HU #10234) ───────────────

/// <summary>
/// Fila de la tabla transversal del submódulo de Validaciones: una validación biométrica con el
/// trámite al que pertenece (para navegar) y los datos que la vista del gestor necesita. Documento y
/// correo viajan completos (vista autenticada del gestor del tenant, D3/CF-05, Feature #11004); la FE
/// decide si enmascara al pintarlos. NO incluye la URL de captura salvo cuando está vigente (ver
/// <see cref="EnlaceVigente"/> más abajo, HU #10886).
/// </summary>
public sealed record TenantBiometricValidationDto(
    Guid Id,
    /// <summary>HU #10865 — nullable para prevalidaciones standalone (sin trámite).</summary>
    Guid? InstanceId,
    /// <summary>HU #10867 — null para prevalidaciones standalone (sin trámite asociado).</summary>
    string? ReferenceNumber,
    /// <summary>HU #10867 — null para prevalidaciones standalone (sin trámite asociado).</summary>
    string? Modalidad,
    string? PartyRole,
    string Name,
    string DocumentType,
    string DocumentNumber,
    string Status,
    int? Score,
    string Provider,
    bool Expired,
    string? RejectionReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ValidatedAt,
    // CF-05 (Feature #11004, ADR-0036) — correo de la validación. Vista autenticada del gestor del
    // tenant: viaja completo, sin enmascarar (D3). Revierte la omisión intencional de HU #10234.
    string Email,
    // CF-06/tracking (Feature #11004) — intentos y reenvíos, para que Validaciones/Prevalidaciones
    // muestren el mismo dato que ya usa el detalle/BiometricValidationDto, sin llamada adicional.
    int Attempts,
    int MaxAttempts,
    int ResendCount,
    DateTimeOffset? LastResentAt,
    // Vigencia de la identidad APROBADA (30 días calendario desde la aprobación): fecha de fin de
    // vigencia y días que le restan. Null cuando no hay aprobación (ValidatedAt) → no aplica vigencia.
    DateTimeOffset? ValidUntil,
    int? DaysRemaining,
    // CF-05 (HU #10886, AC2) — enlace de captura VIGENTE, para reenviarlo por otros medios desde el
    // submódulo de Validaciones de Identidad. Null cuando no hay nada que compartir: proveedor sin
    // enlace (mock), validación en estado terminal (aprobado/rechazado/expirado) o enlace ya vencido.
    string? CaptureUrl,
    // Vencimiento del enlace de captura (distinto de ValidUntil, que es la vigencia de la identidad
    // ya APROBADA). Se expone siempre que exista, para poder mostrar "vigente hasta …".
    DateTimeOffset? LinkExpiresAt);

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
                .Where(d => d.RejectionReason is not null
                    && d.RejectionReason.Contains(term, StringComparison.OrdinalIgnoreCase))
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
            // HU #10867 — null para prevalidaciones standalone; la FE muestra "—" / badge "Prevalidación".
            v.ProcedureInstance?.ReferenceNumber,
            v.ProcedureInstance?.ModalidadEntrada,
            v.PartyRole,
            v.Name,
            v.DocumentType,
            v.DocumentNumber,
            v.Status,
            v.Score,
            v.Provider,
            // Mismo criterio que BiometricValidationDto: expirada si no aprobada y ya pasó expires_at.
            v.Status != BiometricEstados.Aprobado && now > v.ExpiresAt,
            IniciarBiometriaHandler.ExtractMotivoRechazo(v),
            v.CreatedAt,
            v.ValidatedAt,
            v.Email,
            v.Attempts,
            v.MaxAttempts,
            v.ResendCount,
            v.LastResentAt,
            // Vigencia (HU #10350): la fecha de fin se PERSISTE (la estampa el código al aprobar) y se lee
            // de la columna; los días restantes NO se persisten — se calculan al vuelo contra HOY, así que
            // siempre van frescos sin job ni columna materializada.
            v.ValidUntil,
            BiometricRules.DiasRestantesVigencia(v, now),
            EnlaceVigente(v, now),
            v.ExpiresAt);

    /// <summary>
    /// CF-05 (HU #10886, AC2) — enlace de captura solo mientras SIRVE para algo: la validación sigue en
    /// curso (pendiente de envío / enviada / en proceso) y el enlace no ha vencido. En estados terminales
    /// (aprobado, rechazado, expirado) no se expone: no hay nada que reenviar y el enlace es un dato
    /// sensible que no debe pasearse más allá de su utilidad.
    /// </summary>
    private static string? EnlaceVigente(ProcedureInstanceBiometricValidation v, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(v.CaptureUrl))
            return null;

        var enCurso = v.Status is BiometricEstados.PendienteEnvio
            or BiometricEstados.Enviado
            or BiometricEstados.EnProceso;

        return enCurso && now <= v.ExpiresAt ? v.CaptureUrl : null;
    }

    /// <summary>
    /// KPIs derivados de las filas ya materializadas (usado cuando el filtro de motivo se resuelve en
    /// memoria). "Expiradas" alinea con el flag <c>Expired</c> mostrado en la fila; "En proceso" agrupa
    /// enviado + en_proceso, igual que <see cref="BuildStats"/>.
    /// </summary>
    internal static BiometricValidationStatsDto BuildStatsFromRows(IReadOnlyList<TenantBiometricValidationDto> dtos) =>
        new(
            Total: dtos.Count,
            Aprobadas: dtos.Count(d => d.Status == BiometricEstados.Aprobado),
            EnProceso: dtos.Count(d => d.Status is BiometricEstados.Enviado or BiometricEstados.EnProceso),
            Rechazadas: dtos.Count(d => d.Status == BiometricEstados.Rechazado),
            Expiradas: dtos.Count(d => d.Status == BiometricEstados.Expirado || d.Expired));

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
