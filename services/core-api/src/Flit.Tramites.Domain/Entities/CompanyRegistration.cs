namespace Flit.Tramites.Domain.Entities;

/// <summary>
/// Registro mercantil (RUES) de una persona jurídica dentro de un trámite (HU #11302, ADR-0041).
/// Sustituye al snapshot congelado en la llave <c>rues_snapshots_json</c>.
/// </summary>
/// <remarks>
/// La diferencia con el snapshot anterior no es de formato: aquel vivía en <c>field_values</c>, que es
/// inmutable fuera de borrador, y por eso una compañía sin snapshot solo podía conseguirlo consultando
/// <b>en vivo al generar el PDF</b> — una llamada saliente cobrada en cada regeneración. Con esta
/// tabla, generar el expediente cuesta cero llamadas externas.
///
/// <para><b>PII</b>: <see cref="LegalRepresentatives"/> va marcado <c>@pii:high</c> en el DDL.</para>
/// </remarks>
public sealed class CompanyRegistration
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProcedureInstanceId { get; set; }

    /// <summary>Una compañía, una fila por trámite.</summary>
    public string Nit { get; set; } = string.Empty;

    public string? BusinessName { get; set; }
    public string? BusinessNameRaw { get; set; }

    public string? RegistrationNumber { get; set; }
    public string? RegistrationNumberRaw { get; set; }

    /// <summary>vigente | vencido | no_aplica | unknown — canónico derivado (D5).</summary>
    public string RegistrationStatus { get; set; } = "unknown";

    /// <summary>D5: el texto tal como lo dijo el RUES. Es lo que imprime el certificado si el canónico es unknown.</summary>
    public string? RegistrationStatusRaw { get; set; }

    public DateOnly? RegisteredOn { get; set; }
    public string? RegisteredOnRaw { get; set; }

    public DateOnly? RenewedOn { get; set; }
    public string? RenewedOnRaw { get; set; }

    public string? ChamberOfCommerce { get; set; }
    public string? ChamberOfCommerceRaw { get; set; }

    public string? Category { get; set; }
    public string? CategoryRaw { get; set; }

    public string? Address { get; set; }
    public string? AddressRaw { get; set; }

    public string? City { get; set; }
    public string? CityRaw { get; set; }

    /// <summary>Array JSON de representantes legales. Hoy se paga y se tira; guardarlo cuesta cero.</summary>
    public string LegalRepresentatives { get; set; } = "[]";

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
