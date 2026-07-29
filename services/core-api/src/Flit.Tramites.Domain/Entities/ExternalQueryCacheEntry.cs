namespace Flit.Tramites.Domain.Entities;

/// <summary>
/// Caché cross-trámite de una consulta externa (RUNT/SIMIT/RUES/FASECOLDA/…) — HU #10878
/// (Feature #10862, CF-04), ADR-0030. Una fila = la ÚLTIMA consulta reutilizable de una persona
/// o vehículo, por tenant y por fuente (<see cref="ExternalDataSourceId"/>). Vigencia estampada en
/// escritura (<see cref="ExpiresAt"/>), mismo espíritu que <c>BiometricRules.VigenciaDias</c>: no se
/// recalcula dinámicamente, se compara contra el reloj (<c>now &lt; ExpiresAt</c>).
/// </summary>
public sealed class ExternalQueryCacheEntry
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ExternalDataSourceId { get; set; }

    /// <summary><see cref="ExternalQueryCacheRules.SubjectKindPerson"/> | <see cref="ExternalQueryCacheRules.SubjectKindVehicle"/>.</summary>
    public string SubjectKind { get; set; } = string.Empty;

    // Llave persona (subject_kind = person).
    public string? DocumentType { get; set; }
    public string? DocumentNumber { get; set; }

    // Llave vehículo (subject_kind = vehicle): placa O VIN, normalizado a mayúsculas/trim.
    public string? VehicleIdentifier { get; set; }

    /// <summary>Snapshot <c>HydratedField[]</c> serializado (mismo shape que <c>field_values</c>/<c>ConsultationResult</c>).</summary>
    public string Payload { get; set; } = "[]";

    public DateTimeOffset QueriedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Trámite que originó/actualizó por última vez esta fila (trazabilidad, opcional).</summary>
    public Guid? SourceProcedureInstanceId { get; set; }

    public int ReuseCount { get; set; }
    public DateTimeOffset? LastReusedAt { get; set; }

    public long RowVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}

/// <summary>Constantes de negocio de la caché de consultas externas (ADR-0030, HU #10878).</summary>
public static class ExternalQueryCacheRules
{
    /// <summary>TTL (horas) usado cuando <c>ExternalDataSource.CacheTtlHours</c> es NULL.</summary>
    public const int DefaultTtlHours = 24;

    public const string SubjectKindPerson = "person";
    public const string SubjectKindVehicle = "vehicle";
}
