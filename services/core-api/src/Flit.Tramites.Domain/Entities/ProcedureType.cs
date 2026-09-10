namespace Flit.Tramites.Domain.Entities;

public sealed class ProcedureType
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Family { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public string PublicationStatus { get; set; } = Enums.PublicationStatus.Draft;
    public DateTimeOffset? PublishedAt { get; set; }
    public Guid? PublishedBy { get; set; }
    public string ExternalRefs { get; set; } = "{}";

    /// <summary>
    /// Versión semántica del tipo. Incrementa al publicar cambios de configuración.
    /// Las instancias en curso usan <c>procedure_type_snapshots</c> para leer la
    /// versión con la que fueron creadas (AC#5 / CFD-01).
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// Perfil de conformación dinámico serializado como JSON (CFD-01).
    /// Evaluado por <c>DynamicGateEvaluator</c> cuando el flag F08_DynamicProcedures está activo.
    /// </summary>
    public string GateProfile { get; set; } = "{}";

    /// <summary>
    /// Barrera de operación (ADR-0050): el tipo puede elegirse al crear un trámite. Es independiente
    /// de <see cref="PublicationStatus"/>, que solo gobierna la visibilidad en administración: un
    /// tipo puede estar publicado y visible en el catálogo sin que exista todavía un recorrido
    /// operable para él.
    /// <para>Se enciende cuando el tipo tiene pasos parametrizados, matriz documental, causales y
    /// homologación Quipux/ICT si aplica.</para>
    /// </summary>
    public bool WizardEnabled { get; set; }
    public long RowVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public ICollection<ConformationRule> ConformationRules { get; set; } = [];
    public ICollection<ProcedureStep> Steps { get; set; } = [];
}
