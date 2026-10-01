namespace Flit.Infrastructure.Persistence.Entities.Admin;

/// <summary>
/// Personalización de un formato de contrato de mandato del catálogo (HU #13169, Feature #13118): nombre visible,
/// tipo de mandato por defecto y versión de plantilla vigente. Global (sin tenant). El esquema lo lleva el DDL
/// embebido <c>124-HU13169-mandate-format-settings-versions.sql</c>.
/// </summary>
public sealed class MandateFormatSettingEntity
{
    public Guid Id { get; set; }

    /// <summary>Código del catálogo (<c>MandatoFormatCatalog</c>): el mismo <c>template_code</c> del organismo.</summary>
    public string FormatCode { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    /// <summary><c>signer</c> | <c>institutional</c> | <c>open</c>.</summary>
    public string AssignmentMode { get; set; } = "signer";

    /// <summary>Última versión publicada; 0 = sin plantilla personalizada.</summary>
    public int CurrentVersion { get; set; }

    public long RowVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}

/// <summary>Versión inmutable de la plantilla de un formato (HU #13169): nunca se modifica ni se elimina.</summary>
public sealed class MandateFormatVersionEntity
{
    public Guid Id { get; set; }
    public Guid FormatSettingId { get; set; }
    public int VersionNumber { get; set; }
    public string Body { get; set; } = string.Empty;

    /// <summary>SHA-256 hexadecimal en minúsculas del cuerpo en UTF-8.</summary>
    public string BodySha256 { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
}
