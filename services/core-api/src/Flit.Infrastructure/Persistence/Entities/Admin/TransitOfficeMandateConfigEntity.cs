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

    public long RowVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}
