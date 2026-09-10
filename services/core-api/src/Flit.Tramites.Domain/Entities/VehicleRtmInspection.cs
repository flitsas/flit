namespace Flit.Tramites.Domain.Entities;

/// <summary>
/// Una revisión técnico-mecánica certificada para un trámite (HU #11302, ADR-0041).
/// </summary>
/// <remarks>
/// <see cref="VigencyStatus"/> <b>nunca</b> se deriva del texto <c>APROBADA</c>: ese valor describe el
/// resultado del trámite de la revisión, no su vigencia. Hay vehículos con cuatro revisiones
/// <c>APROBADA</c> y ninguna vigente; tratarlo como vigencia produce un certificado que afirma una
/// cobertura inexistente. Se normaliza a <c>unknown</c> y la vigente se elige por fecha.
/// </remarks>
public sealed class VehicleRtmInspection
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProcedureInstanceId { get; set; }

    public string NaturalKey { get; set; } = string.Empty;

    public string? CertificateNumber { get; set; }
    public string? CertificateNumberRaw { get; set; }

    public string? CdaName { get; set; }
    public string? CdaNameRaw { get; set; }

    public DateOnly? IssuedOn { get; set; }
    public string? IssuedOnRaw { get; set; }

    public DateOnly? ValidFrom { get; set; }
    public string? ValidFromRaw { get; set; }

    public DateOnly? ValidUntil { get; set; }
    public string? ValidUntilRaw { get; set; }

    /// <summary>vigente | vencido | no_aplica | unknown</summary>
    public string VigencyStatus { get; set; } = "unknown";
    public string? VigencyStatusRaw { get; set; }

    /// <summary>El RUNT lo manda y no va en el certificado; al auditar distingue particular de servicio público.</summary>
    public string? InspectionType { get; set; }

    public bool IsCurrent { get; set; }

    public string SourceKind { get; set; } = "system";
    public string ProviderKey { get; set; } = string.Empty;
    public DateTimeOffset ObservedAt { get; set; }
    public Guid? RawPayloadId { get; set; }
    public string MapperVersion { get; set; } = "unknown";
    public string NormalizationIssues { get; set; } = "[]";

    public DateTimeOffset? FrozenAt { get; set; }

    public long RowVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}
