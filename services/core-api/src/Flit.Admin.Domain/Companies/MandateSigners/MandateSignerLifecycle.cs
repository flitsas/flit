namespace Flit.Admin.Domain.Companies.MandateSigners;

/// <summary>Vínculo mandatario-compañía-organismo afectado por una operación de ciclo de vida.</summary>
public sealed record MandateSignerLinkRef(Guid TransitOfficeId, Guid CompanyTenantId);

/// <summary>
/// Default que apunta al mandatario: <c>company_rule</c> (default de la compañía en un organismo, lleva
/// <see cref="CompanyTenantId"/>) u <c>office</c> (default general del organismo).
/// </summary>
public sealed record MandateSignerDefaultRef(string Kind, Guid TransitOfficeId, Guid? CompanyTenantId)
{
    public const string CompanyRule = "company_rule";
    public const string Office = "office";
}

/// <summary>
/// HU #13135 — lo que perdería la operación si se da de baja al mandatario. Sin datos personales: solo
/// identificadores y conteos.
/// </summary>
/// <param name="OnlyActiveFor">Compañías y organismos donde el mandatario es el ÚNICO activo de esa compañía.</param>
/// <param name="Defaults">Defaults (de compañía y del organismo) que quedarían en nulo.</param>
/// <param name="PendingProcedures">Trámites radicados sin aprobar que hoy apuntan al mandatario y se reasignarían.</param>
public sealed record MandateSignerImpact(
    IReadOnlyList<MandateSignerLinkRef> OnlyActiveFor,
    IReadOnlyList<MandateSignerDefaultRef> Defaults,
    int PendingProcedures)
{
    public static MandateSignerImpact Empty { get; } = new([], [], 0);

    /// <summary>Sin únicos activos, defaults ni trámites: la baja no exige confirmación.</summary>
    public bool IsEmpty => OnlyActiveFor.Count == 0 && Defaults.Count == 0 && PendingProcedures == 0;
}

/// <summary>
/// HU #13135 — lectura del impacto de dar de baja a un mandatario: compañías y organismos donde es el único activo,
/// defaults que perdería y trámites radicados sin aprobar que lo usan. Solo lectura.
/// </summary>
public interface IMandateSignerImpactReader
{
    Task<MandateSignerImpact> GetImpactAsync(
        Guid mandateSignerId,
        CancellationToken cancellationToken = default);
}

/// <summary>Un trámite que la baja movió: mandatario anterior y nuevo (<c>null</c> = queda para que decida el OT).</summary>
public sealed record MandateSignerProcedureMove(
    Guid ProcedureInstanceId,
    Guid PreviousSignerId,
    Guid? NewSignerId)
{
    public bool IsPending => NewSignerId is null;
}

/// <summary>Resultado de reasignar los trámites radicados sin aprobar de un mandatario dado de baja (HU #13137).</summary>
public sealed record MandateSignerReassignmentResult(IReadOnlyList<MandateSignerProcedureMove> Moves)
{
    public static MandateSignerReassignmentResult None { get; } = new([]);

    public int Reassigned => Moves.Count(m => !m.IsPending);

    public int Pending => Moves.Count(m => m.IsPending);
}

/// <summary>
/// HU #13137 (ADR-0066 P8) — puerto para reasignar, con la prelación del OT, los trámites radicados sin aprobar
/// que apuntan a un mandatario dado de baja. Lo implementa Infrastructure sobre el evaluador único de Trámites; se
/// invoca DENTRO de la transacción de la baja y comparte su unidad de trabajo.
/// </summary>
public interface IMandateSignerProcedureReassigner
{
    Task<MandateSignerReassignmentResult> ReassignAsync(
        Guid mandateSignerId,
        CancellationToken cancellationToken = default);
}

/// <summary>Resultado de una operación de ciclo de vida (baja, eliminación o reactivación) del repositorio.</summary>
public sealed record MandateSignerLifecycleResult(
    bool Applied,
    MandateSignerReassignmentResult Reassignment,
    IReadOnlyList<MandateSignerLinkRef> RestoredLinks,
    IReadOnlyList<MandateSignerLinkRef> ConflictLinks,
    int RetiredDefaults,
    int RestoredDefaults,
    bool RestoredFromSnapshot = true)
{
    public static MandateSignerLifecycleResult NotApplied { get; } =
        new(false, MandateSignerReassignmentResult.None, [], [], 0, 0);

    /// <summary>La reactivación no pudo restaurar NINGÚN vínculo porque todos chocan con otro mandatario activo.</summary>
    public bool AllLinksConflict { get; init; }
}
