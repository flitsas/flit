namespace Flit.Tramites.Domain.Repositories;

/// <summary>
/// Fuente externa habilitada por un tipo de trámite, con el código de la fuente ya resuelto
/// (FEATURE-08 / CFD-04). Vista de lectura del dominio sobre <c>tramites.procedure_type_sources</c>.
/// </summary>
public sealed record ProcedureTypeSourceRecord(
    Guid ExternalDataSourceId,
    string SourceCode,
    int ExecutionOrder,
    string Config);

/// <summary>
/// Comando de upsert de una fuente por tipo: la fuente ya resuelta a su id + orden + config.
/// </summary>
public sealed record ProcedureTypeSourceUpsert(
    Guid ExternalDataSourceId,
    int ExecutionOrder,
    string Config);

/// <summary>
/// Persiste y recupera las fuentes externas habilitadas por tipo de trámite (CFD-04).
/// Catálogo GLOBAL sin <c>tenant_id</c> (excepción ADR-0019): sin filtro de RLS por tenant.
/// </summary>
public interface IProcedureTypeSourceRepository
{
    /// <summary>Fuentes del tipo ordenadas por <c>execution_order</c> ascendente, con su código resuelto.</summary>
    Task<IReadOnlyList<ProcedureTypeSourceRecord>> ListByTypeAsync(
        Guid procedureTypeId, CancellationToken ct = default);

    /// <summary>
    /// Reemplaza el conjunto completo de fuentes del tipo (borra las existentes e inserta las nuevas).
    /// </summary>
    Task ReplaceSourcesAsync(
        Guid procedureTypeId, IReadOnlyList<ProcedureTypeSourceUpsert> sources, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
