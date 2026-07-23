namespace Flit.Ict.Domain.Entities;

/// <summary>
/// Actor de un pre-trámite (vendedor/comprador/locatario y sus representantes).
/// Tabla <c>ict.external_integration_actors</c>. <c>actor_type</c> ∈ {seller,buyer,lessee}.
/// Campos de documento/nombre/contacto marcados como PII en el DDL.
/// </summary>
public sealed class ExternalIntegrationActor : AuditableEntity
{
    public Guid MasterId { get; set; }

    public Guid TenantId { get; set; }

    public string ActorType { get; set; } = string.Empty;

    public string DocumentType { get; set; } = string.Empty;

    public string DocumentNumber { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string FirstLastName { get; set; } = string.Empty;

    public string? SecondLastName { get; set; }

    public string Phone { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string? City { get; set; }

    public string? State { get; set; }

    public string? Address { get; set; }

    public string? ExpeditionDate { get; set; }

    // Representante legal (aplica cuando document_type = NIT)
    public string? LegalRepresentativeDocumentType { get; set; }

    public string? LegalRepresentativeDocumentNumber { get; set; }

    public string? LegalRepresentativeName { get; set; }

    public string? LegalRepresentativeFirstLastName { get; set; }

    public string? LegalRepresentativeSecondLastName { get; set; }

    public string? LegalRepresentativeEmail { get; set; }

    public string? LegalRepresentativePhone { get; set; }

    // Mandante principal
    public string? PrincipalMandanteDocumentType { get; set; }

    public string? PrincipalMandanteDocumentNumber { get; set; }

    public string? PrincipalMandanteName { get; set; }

    public string? PrincipalMandanteFirstLastName { get; set; }

    public string? PrincipalMandanteSecondLastName { get; set; }

    public string? PrincipalMandanteEmail { get; set; }
}
