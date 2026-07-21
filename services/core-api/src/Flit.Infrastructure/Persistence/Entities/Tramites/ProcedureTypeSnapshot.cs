namespace Flit.Infrastructure.Persistence.Entities.Tramites;

/// <summary>
/// Snapshot liviano del tipo de trámite capturado al crear una instancia.
/// Tabla: <c>tramites.procedure_type_snapshots</c> (FEATURE-08 / CFD-01 / AC#5).
/// Protege la instancia en curso de cambios de configuración del tipo posteriores.
/// Inmutable post-INSERT: sin <c>updated_at/by</c> ni <c>deleted_at/by</c>.
/// Tiene <c>tenant_id</c> — filtrado por RLS.
/// </summary>
public sealed class ProcedureTypeSnapshot
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid ProcedureInstanceId { get; set; }

    public Guid ProcedureTypeId { get; set; }

    public int TypeVersion { get; set; }

    /// <summary>
    /// Snapshot JSON mínimo del tipo al momento de creación de la instancia.
    /// Esquema: <c>{ "code", "name", "family", "version", "gateProfile": {...},
    /// "conformationRules": [{...}], "stepSectionTypes": [{...}] }</c>
    /// </summary>
    public string Snapshot { get; set; } = "{}";

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }
}
