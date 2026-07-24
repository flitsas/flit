namespace Flit.Admin.Domain.Companies.LegalRepresentatives;

/// <summary>
/// Read model de una compañía representada para la gestión admin — HU #10900. <c>DocumentNumber</c>
/// (NIT) es PII: solo en respuestas autenticadas; no loguear.
/// </summary>
public sealed class RepresentedCompanyItem
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public string DocumentType { get; init; } = "NIT";
    public string DocumentNumber { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Email { get; init; }
    public string? Address { get; init; }
    public string? City { get; init; }
    public string? Phone { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
}

/// <summary>
/// Read model de un representante legal para el listado/detalle admin — HU #10900. Proyecta los
/// datos de la compañía representada y las banderas de firma/identidad vigentes.
/// <c>DocumentNumber</c> es PII (@pii:high): solo en respuestas autenticadas; no loguear.
/// </summary>
public sealed class LegalRepresentativeItem
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public Guid RepresentedCompanyId { get; init; }

    /// <summary>NIT de la compañía representada (denormalizado para el listado).</summary>
    public string CompanyDocumentNumber { get; init; } = string.Empty;

    /// <summary>Razón social de la compañía representada (denormalizado para el listado).</summary>
    public string CompanyName { get; init; } = string.Empty;

    public string DocumentType { get; init; } = string.Empty;
    public string DocumentNumber { get; init; } = string.Empty;
    public string FirstLastName { get; init; } = string.Empty;
    public string? SecondLastName { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Email { get; init; }
    public string? Address { get; init; }
    public string? City { get; init; }
    public string? Phone { get; init; }
    public Guid? SignatureVaultId { get; init; }
    public Guid? IdentityValidationRef { get; init; }

    /// <summary>Ids de los tipos de trámite que el representante puede firmar (puente M:N).</summary>
    public IReadOnlyList<Guid> ProcedureTypeIds { get; init; } = [];

    public bool IsActive { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }

    /// <summary>¿Tiene firma del baúl o validación de identidad vinculada?</summary>
    public bool HasSignatureOrIdentity => SignatureVaultId is not null || IdentityValidationRef is not null;
}

/// <summary>
/// Read model de una escritura para el listado/detalle admin y el consumo del wizard — HU #10900.
/// Proyecta las compañías (NITs) a las que aplica.
/// </summary>
public sealed class DeedItem
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public string Description { get; init; } = string.Empty;
    public string StoragePath { get; init; } = string.Empty;
    public string StorageSha256 { get; init; } = string.Empty;
    public DateOnly VigenciaDesde { get; init; }
    public DateOnly VigenciaHasta { get; init; }
    public bool IsActive { get; init; }

    /// <summary>Ids de las compañías representadas a las que aplica la escritura (puente M:N).</summary>
    public IReadOnlyList<Guid> RepresentedCompanyIds { get; init; } = [];

    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
}
