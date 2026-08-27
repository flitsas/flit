namespace Flit.Infrastructure.Persistence.Entities.Admin;

/// <summary>
/// Configuración de mandato de un Organismo de Tránsito (ADR-0036, HU #10912). Regla propia del OT
/// (llave <c>transit_office_id</c>, sin tenant): plantilla del mandato, si exige mandato también a
/// persona natural y los datos del mandatario institucional (OT con firmante fijo, p. ej. Sabaneta
/// UT-SETSA / Bello UT-MAB). El esquema lo lleva el DDL embebido
/// (<c>41-HU10912-transit-office-mandate-config.sql</c>); la entidad es <c>ExcludeFromMigrations</c>.
/// </summary>
public sealed class TransitOfficeMandateConfigEntity
{
    public Guid Id { get; set; }
    public Guid TransitOfficeId { get; set; }

    /// <summary>Variante de plantilla: <c>generico</c> | <c>sabaneta</c> | <c>bello</c>.</summary>
    public string TemplateCode { get; set; } = "generico";

    /// <summary>El OT exige mandato también a persona natural (Sabaneta = true).</summary>
    public bool RequiresForNaturalPerson { get; set; }

    /// <summary>Nombre del mandatario institucional (OT con firmante fijo), o null.</summary>
    public string? InstitutionalMandataryName { get; set; }

    /// <summary>NIT del mandatario institucional, o null.</summary>
    public string? InstitutionalMandataryNit { get; set; }

    /// <summary>
    /// HU #11204 — familia del mandatario: <c>individuo</c> (una persona firma) u
    /// <c>organismo_transito</c> (firma el propio organismo / una unión temporal). NO determina la
    /// redacción, que la sigue eligiendo <see cref="TemplateCode"/>.
    /// </summary>
    public string MandataryFamily { get; set; } = "individuo";

    /// <summary>HU #11204 — ciudad de la Cámara de Comercio que acredita al MANDANTE.</summary>
    public string? ChamberCity { get; set; }

    /// <summary>HU #11204 — sigla de la unión temporal (p. ej. UT-SETSA), usada en las obligaciones.</summary>
    public string? MandatarySigla { get; set; }

    /// <summary>
    /// Modo de asignación: <c>signer</c> | <c>institutional</c> | <c>open</c>.
    /// Independiente de <see cref="TemplateCode"/> (redacción).
    /// </summary>
    public string AssignmentMode { get; set; } = "open";

    /// <summary><c>none</c> | <c>pdf</c> | <c>editor</c>. Sin propia ⇒ se usa <see cref="TemplateCode"/>.</summary>
    public string CustomTemplateKind { get; set; } = "none";

    public string? CustomTemplateStoragePath { get; set; }
    public string? CustomTemplateSha256 { get; set; }
    public string? CustomTemplateFileName { get; set; }

    /// <summary>Cuerpo del editor (texto/HTML simple con placeholders). Sin firmas.</summary>
    public string? CustomTemplateBody { get; set; }

    /// <summary>Manifest de coordenadas overlay (JSON); opcional.</summary>
    public string? CustomFieldManifest { get; set; }

    public long RowVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}
