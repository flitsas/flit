namespace Flit.Tramites.Domain.Entities;

/// <summary>
/// Entidad persona/sujeto a nivel tenant — HU #10865 (Feature #10864, CF-00, ADR-0030).
/// Ancla persistida de una persona identificada por <c>(tenant_id, document_type, document_number)</c>.
/// Permite crear validaciones biométricas standalone (sin trámite previo) y reutilizarlas al abrir
/// un trámite cuyo actor coincide por documento.
///
/// <para>Persona jurídica: los datos del representante legal se embeben en la misma fila
/// (mismo patrón que <see cref="ProcedureInstanceActor"/>) en vez de tabla separada — ADR-0030 §Tradeoff.</para>
/// </summary>
public sealed class Person
{
    public Guid Id { get; set; }

    /// <summary>@pii:high — Número de documento de identidad de la persona.</summary>
    public Guid TenantId { get; set; }

    /// <summary>Tipo de documento (CC, CE, NIT, PP, …). Máx 20 chars.</summary>
    public string DocumentType { get; set; } = string.Empty;

    /// <summary>@pii:high — Número de documento. Máx 40 chars.</summary>
    public string DocumentNumber { get; set; } = string.Empty;

    /// <summary>@pii:medium — Nombre completo de la persona (o razón social si es jurídica). Máx 200 chars.</summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>@pii:high — Correo electrónico principal. Máx 320 chars.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Tipo de persona: <see cref="PersonTypes.Natural"/> | <see cref="PersonTypes.Juridical"/>.
    /// La biométrica standalone para persona jurídica valida al representante legal (<c>LegalRep*</c>).
    /// </summary>
    public string PersonType { get; set; } = PersonTypes.Natural;

    // ── Representante legal embebido (solo para PersonType='juridical') ─────────
    // Mismo enfoque que ProcedureInstanceActor — evita tabla legal_representatives separada en fase 1.

    /// <summary>Tipo de documento del representante legal. Null para persona natural.</summary>
    public string? LegalRepDocumentType { get; set; }

    /// <summary>@pii:high — Número de documento del representante legal.</summary>
    public string? LegalRepDocumentNumber { get; set; }

    /// <summary>@pii:medium — Nombre del representante legal.</summary>
    public string? LegalRepName { get; set; }

    /// <summary>@pii:high — Correo del representante legal.</summary>
    public string? LegalRepEmail { get; set; }

    // ── Columnas estándar FLIT (checklist §A5) ───────────────────────────────────
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    /// <summary>Versión de fila para concurrencia optimista (checklist §A5, §B8).</summary>
    public int RowVersion { get; set; }

    // ── Colección de navegación inversa ─────────────────────────────────────────
    public ICollection<ProcedureInstanceBiometricValidation> BiometricValidations { get; set; } = [];
}

/// <summary>Tipos de persona (CF-00, ADR-0030).</summary>
public static class PersonTypes
{
    /// <summary>Persona natural — la biométrica la realiza el titular del documento.</summary>
    public const string Natural = "natural";

    /// <summary>Persona jurídica (empresa) — la biométrica la realiza el representante legal.</summary>
    public const string Juridical = "juridical";
}
