using Flit.Tramites.Domain.Certifications;

namespace Flit.Tramites.Application.UseCases.Certifications;

/// <summary>
/// Puerto de persistencia del modelo canónico de certificaciones (HU #11302, ADR-0041).
/// </summary>
/// <remarks>
/// Habla en tipos de dominio, no en filas: quien lo use no sabe que detrás hay cuatro tablas, y la
/// precedencia entre fuentes se resuelve <b>antes</b> de llegar aquí (ver
/// <see cref="CertificationPrecedence"/>). El repositorio solo escribe lo que se le da y devuelve lo
/// que hay guardado, con su procedencia.
///
/// <para>Las escrituras son <b>upsert por llave natural</b>, así que reconsultar el mismo trámite
/// actualiza filas en vez de duplicarlas, y el histórico de pólizas y revisiones convive sin
/// colisionar.</para>
/// </remarks>
public interface ICertificationRepository
{
    /// <summary>Todo lo certificado que hay guardado para el trámite, con procedencia por fila.</summary>
    Task<CertificationSnapshot> LoadAsync(Guid tenantId, Guid instanceId, CancellationToken cancellationToken);

    /// <summary>
    /// Guarda la respuesta cruda <b>ya sanitizada</b> y devuelve su id, para que las filas
    /// certificadas puedan apuntar a la evidencia que las produjo. Devuelve <c>null</c> si no hay
    /// payload que guardar.
    /// </summary>
    Task<Guid?> SaveRawPayloadAsync(
        Guid tenantId, Guid instanceId, RawProviderPayload? payload, CancellationToken cancellationToken);

    /// <summary>Upsert del histórico de pólizas. Exactamente una puede quedar como vigente.</summary>
    Task UpsertSoatPoliciesAsync(
        Guid tenantId, Guid instanceId, IReadOnlyList<StoredSoatPolicy> policies, CancellationToken cancellationToken);

    /// <summary>Upsert del histórico de revisiones. Exactamente una puede quedar como vigente.</summary>
    Task UpsertRtmInspectionsAsync(
        Guid tenantId, Guid instanceId, IReadOnlyList<StoredRtmInspection> inspections, CancellationToken cancellationToken);

    /// <summary>Upsert de registros mercantiles. Una fila por NIT dentro del trámite.</summary>
    Task UpsertMerchantRegistrationsAsync(
        Guid tenantId, Guid instanceId, IReadOnlyList<StoredMerchantRegistration> registrations, CancellationToken cancellationToken);

    /// <summary>
    /// Congela lo certificado del trámite (se invoca al radicar). Sustituye al trigger de
    /// inmutabilidad de <c>field_values</c>: es explícito, y por eso hay una ventana en la que el dato
    /// todavía se puede completar o corregir. Las filas ya congeladas no se vuelven a tocar.
    /// </summary>
    Task<int> FreezeAsync(
        Guid tenantId, Guid instanceId, DateTimeOffset frozenAt, CancellationToken cancellationToken);
}

/// <summary>Lo guardado para un trámite, tal como salió de la base.</summary>
public sealed record CertificationSnapshot(
    IReadOnlyList<StoredSoatPolicy> SoatPolicies,
    IReadOnlyList<StoredRtmInspection> RtmInspections,
    IReadOnlyList<StoredMerchantRegistration> MerchantRegistrations)
{
    public static readonly CertificationSnapshot Empty = new([], [], []);

    public bool IsEmpty =>
        SoatPolicies.Count == 0 && RtmInspections.Count == 0 && MerchantRegistrations.Count == 0;
}

/// <summary>Una póliza guardada: el dato canónico + quién lo dijo + si es la que va al certificado.</summary>
public sealed record StoredSoatPolicy(
    SoatCertification Certification,
    CertificationProvenance Provenance,
    bool IsCurrent = false,
    DateTimeOffset? FrozenAt = null);

/// <summary>Una revisión guardada.</summary>
public sealed record StoredRtmInspection(
    RtmCertification Certification,
    CertificationProvenance Provenance,
    bool IsCurrent = false,
    DateTimeOffset? FrozenAt = null);

/// <summary>Un registro mercantil guardado.</summary>
public sealed record StoredMerchantRegistration(
    MerchantRegistration Registration,
    CertificationProvenance Provenance,
    DateTimeOffset? FrozenAt = null);
