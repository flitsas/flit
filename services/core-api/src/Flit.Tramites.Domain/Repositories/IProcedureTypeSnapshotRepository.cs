namespace Flit.Tramites.Domain.Repositories;

/// <summary>
/// Vista de dominio de una fila de <c>tramites.procedure_type_snapshots</c> (FEATURE-08 / CFD-01 / AC#5).
/// La entidad de persistencia vive en Infraestructura; el dominio solo maneja este record inmutable para
/// no acoplar Application/Domain a EF.
/// </summary>
/// <param name="Snapshot">
/// JSON congelado del tipo al crear la instancia. Esquema:
/// <c>{ "code", "name", "family", "version", "gateProfile": {...},
/// "conformationRules": [{entityCode, validationProfile}], "stepSectionTypes": [{stepCode, sectionTypes: []}] }</c>.
/// </param>
public sealed record ProcedureTypeSnapshotRecord(
    Guid Id,
    Guid TenantId,
    Guid ProcedureInstanceId,
    Guid ProcedureTypeId,
    int TypeVersion,
    string Snapshot,
    Guid? CreatedBy);

/// <summary>
/// Persiste y recupera el snapshot inmutable del tipo de trámite capturado al crear una instancia
/// (CFD-01 / AC#5). El snapshot protege a la instancia en curso de cambios de configuración del tipo
/// posteriores: la instancia SIEMPRE lee su snapshot, nunca la versión live del tipo.
/// </summary>
public interface IProcedureTypeSnapshotRepository
{
    /// <summary>Encola el snapshot para persistir en el próximo <see cref="SaveChangesAsync"/>.</summary>
    Task AddAsync(ProcedureTypeSnapshotRecord snapshot, CancellationToken ct = default);

    /// <summary>
    /// Snapshot de una instancia (único por <c>procedure_instance_id</c>), o <c>null</c> si la
    /// instancia no capturó snapshot (tipo estático / creada antes de FEATURE-08). Filtrado por RLS.
    /// </summary>
    Task<ProcedureTypeSnapshotRecord?> GetByInstanceIdAsync(
        Guid procedureInstanceId, Guid tenantId, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
