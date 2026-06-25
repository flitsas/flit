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
/// Validaciones de Identidad (HU #10234). La tabla se acota a <see cref="MaxRows"/> filas; los KPIs
/// se calculan con un conteo agrupado aparte para que sean exactos aunque la tabla esté acotada.
/// Reusa <see cref="IniciarBiometriaHandler.ExtractMotivoRechazo"/> para el motivo SANITIZADO (sin PII).
/// </summary>
public sealed class ListTenantBiometricValidationsHandler(IProcedureInstanceRepository repo)
{
    /// <summary>Cap de filas de la tabla (vista de monitoreo). Los KPIs no dependen de este cap.</summary>
    public const int MaxRows = 500;

    public async Task<TenantBiometricValidationsResponse> HandleAsync(Guid tenantId, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var rows = await repo.ListBiometricValidationsByTenantAsync(tenantId, MaxRows, ct);
        var counts = await repo.CountBiometricValidationsByEstadoAsync(tenantId, ct);

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

        var stats = new BiometricValidationStatsDto(
            Total: counts.Values.Sum(),
            Aprobadas: counts.GetValueOrDefault(BiometricEstados.Aprobado),
            // "En proceso" agrupa enviado + en_proceso (ambos son trabajo en curso para el gestor).
            EnProceso: counts.GetValueOrDefault(BiometricEstados.Enviado)
                + counts.GetValueOrDefault(BiometricEstados.EnProceso),
            Rechazadas: counts.GetValueOrDefault(BiometricEstados.Rechazado),
            Expiradas: counts.GetValueOrDefault(BiometricEstados.Expirado));

        return new TenantBiometricValidationsResponse(dtos, stats);
    }
}
