namespace Flit.Infrastructure.Persistence.Entities.Tramites;

/// <summary>
/// Fuente de datos externa habilitada para un tipo de trámite.
/// Tabla: <c>tramites.procedure_type_sources</c> (FEATURE-08 / CFD-04).
/// Catálogo global sin <c>tenant_id</c>: excepción A4/A20 documentada en ADR-0019.
/// PK compuesta (<see cref="ProcedureTypeId"/>, <see cref="ExternalDataSourceId"/>).
/// </summary>
public sealed class ProcedureTypeSource
{
    public Guid ProcedureTypeId { get; set; }

    public Guid ExternalDataSourceId { get; set; }

    public bool IsActive { get; set; } = true;

    public short ExecutionOrder { get; set; }

    /// <summary>
    /// Configuración especial de la fuente para el tipo.
    /// Esquema: <c>{ "simitMode": "INTERNAL"|"ONLINE", "optimizeDailyCache": bool }</c>
    /// </summary>
    public string Config { get; set; } = "{}";

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}
