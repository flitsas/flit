namespace Flit.Tramites.Domain.Entities;

/// <summary>
/// Una póliza de SOAT certificada para un trámite (HU #11302, ADR-0041).
/// </summary>
/// <remarks>
/// Cada dato lleva su <b>par canónico + crudo</b>: <c>IssuedOn</c>/<c>IssuedOnRaw</c>. Lo que no se
/// pudo normalizar deja el canónico en <c>null</c>, conserva el crudo y se lista en
/// <see cref="NormalizationIssues"/> — que es la lista de trabajo para corregir el mapper sin volver a
/// consultar.
///
/// <para><see cref="FrozenAt"/> reemplaza al trigger de inmutabilidad de <c>field_values</c>: mientras
/// es <c>null</c> el dato se puede completar y corregir; se fija al radicar. Es la propiedad que hacía
/// falta y que no tenía el almacén anterior.</para>
/// </remarks>
public sealed class VehicleSoatPolicy
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProcedureInstanceId { get; set; }

    /// <summary><c>numero|vencimiento</c>. Hace idempotente la reconsulta.</summary>
    public string NaturalKey { get; set; } = string.Empty;

    public string? PolicyNumber { get; set; }
    public string? PolicyNumberRaw { get; set; }

    public string? InsurerName { get; set; }
    public string? InsurerNameRaw { get; set; }

    public DateOnly? IssuedOn { get; set; }
    public string? IssuedOnRaw { get; set; }

    public DateOnly? ValidFrom { get; set; }
    public string? ValidFromRaw { get; set; }

    public DateOnly? ValidUntil { get; set; }
    public string? ValidUntilRaw { get; set; }

    /// <summary>vigente | vencido | no_aplica | unknown</summary>
    public string VigencyStatus { get; set; } = "unknown";
    public string? VigencyStatusRaw { get; set; }

    /// <summary>La que imprime el certificado (D9: solo la vigente). Única por trámite.</summary>
    public bool IsCurrent { get; set; }

    /// <summary>consultation | user | ocr | system</summary>
    public string SourceKind { get; set; } = "system";
    public string ProviderKey { get; set; } = string.Empty;
    public DateTimeOffset ObservedAt { get; set; }
    public Guid? RawPayloadId { get; set; }
    public string MapperVersion { get; set; } = "unknown";

    /// <summary>Array JSON con los campos que llegaron y no se supieron leer.</summary>
    public string NormalizationIssues { get; set; } = "[]";

    /// <summary>Congelamiento explícito al radicar. <c>null</c> = todavía se puede completar.</summary>
    public DateTimeOffset? FrozenAt { get; set; }

    public long RowVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}
