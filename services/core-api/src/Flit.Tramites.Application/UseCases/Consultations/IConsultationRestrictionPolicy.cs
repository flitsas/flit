namespace Flit.Tramites.Application.UseCases.Consultations;

/// <summary>
/// Consultas que la compañía inhabilitó para el OT destino del trámite (HU #10760). Conjunto
/// case-insensitive de <see cref="ConsultationRestrictionKinds"/>; vacío = nada restringido.
/// </summary>
public sealed record ConsultationRestrictions(IReadOnlySet<string> DisabledKinds)
{
    /// <summary>Sin restricciones: el default permisivo (tabla dispersa, ausencia de fila = permitido).</summary>
    public static readonly ConsultationRestrictions None =
        new(new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// Construye el conjunto normalizado a partir de los kinds crudos de la BD, garantizando la
    /// comparación case-insensitive que <see cref="IsDisabled"/> asume (el ctor primario acepta
    /// cualquier <see cref="IReadOnlySet{T}"/>, que podría venir con otro comparador).
    /// </summary>
    public static ConsultationRestrictions From(IEnumerable<string> disabledKinds) =>
        new(new HashSet<string>(disabledKinds, StringComparer.OrdinalIgnoreCase));

    public bool IsDisabled(string kind) => DisabledKinds.Contains(kind);
}

/// <summary>
/// Puerto para resolver QUÉ consultas NO debe correr el preflight para un trámite, según la política
/// comercial que la compañía fijó sobre el OT destino
/// (<c>admin.tenant_transit_office_consultation_restrictions</c>, HU #10759). Desacopla Trámites de
/// Admin (mismo patrón que <c>IRnmcRequirementPolicy</c>).
///
/// Eje ORTOGONAL al de <c>IRnmcRequirementPolicy</c>: el OT declara qué EXIGE, la compañía qué NO
/// quiere consultar. Una consulta corre si el OT la exige Y la compañía no la restringió.
///
/// Distinto también de <c>IConsultationTenantOverrideProvider</c>, que responde QUÉ proveedor
/// atiende la consulta (por tenant, sin OT); este responde SI la consulta se hace (por tenant + OT).
/// </summary>
public interface IConsultationRestrictionPolicy
{
    /// <summary>
    /// Restricciones vigentes para el par (tenant, OT destino). Si el OT no se puede resolver
    /// devuelve <see cref="ConsultationRestrictions.None"/>: sin OT no hay política que aplicar, y
    /// el default de la tabla es permisivo (coherente con <c>RnmcRequirementPolicy</c>).
    /// </summary>
    Task<ConsultationRestrictions> GetAsync(
        Guid tenantId,
        Guid? transitOfficeId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Implementación que NUNCA restringe — default permisivo para tests de aplicación que no ejercitan
/// la configurabilidad por OT.
/// </summary>
public sealed class NullConsultationRestrictionPolicy : IConsultationRestrictionPolicy
{
    public static NullConsultationRestrictionPolicy Instance { get; } = new();

    public Task<ConsultationRestrictions> GetAsync(
        Guid tenantId,
        Guid? transitOfficeId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(ConsultationRestrictions.None);
}
