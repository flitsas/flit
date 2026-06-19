using Flit.Tramites.Domain.Entities;

namespace Flit.Tramites.Domain.Repositories;

public interface IProcedureInstanceRepository
{
    Task<ProcedureInstance?> GetByIdAsync(Guid id, Guid tenantId, CancellationToken ct = default);
    Task<ProcedureInstance?> GetByIdWithDetailsAsync(Guid id, Guid tenantId, CancellationToken ct = default);
    Task<ProcedureInstance?> GetByIdWithAttachmentsAsync(Guid id, Guid tenantId, CancellationToken ct = default);

    /// <summary>Carga la instancia con TODO el grafo del wizard: actores, field values, adjuntos,
    /// datos comerciales y snapshots de preflight (Slice 4 — wizard server-driven).</summary>
    Task<ProcedureInstance?> GetByIdWithWizardGraphAsync(Guid id, Guid tenantId, CancellationToken ct = default);

    /// <summary>Carga la instancia con sus datos comerciales (1:1) para GET/PUT comercial.</summary>
    Task<ProcedureInstance?> GetByIdWithCommercialAsync(Guid id, Guid tenantId, CancellationToken ct = default);

    /// <summary>Carga la instancia con sus validaciones biométricas (Slice 6).</summary>
    Task<ProcedureInstance?> GetByIdWithBiometricsAsync(Guid id, Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Resuelve una validación biométrica por el hash SHA-256 de su token (acceso PÚBLICO vía
    /// magic-link, sin tenant). Devuelve null si no existe — el caller NO debe filtrar existencia.
    /// </summary>
    Task<ProcedureInstanceBiometricValidation?> GetBiometricByTokenHashAsync(string tokenHash, CancellationToken ct = default);

    /// <summary>Último snapshot de preflight de la instancia (por created_at desc), o null.</summary>
    Task<ProcedureInstancePreflightSnapshot?> GetLatestPreflightAsync(Guid id, Guid tenantId, CancellationToken ct = default);

    /// <summary>Encola un nuevo snapshot de preflight para persistir en el próximo SaveChanges.</summary>
    Task AddPreflightSnapshotAsync(ProcedureInstancePreflightSnapshot snapshot, CancellationToken ct = default);

    Task<int> CountByTenantAndYearAsync(Guid tenantId, int year, CancellationToken ct = default);

    /// <summary>
    /// Inserta la instancia generando un <c>ReferenceNumber</c> único con formato
    /// <c>TRM-{year}-{seq:D6}</c> a partir de MAX(seq) + 1 por (tenant, year). Si el insert
    /// colisiona contra el constraint <c>uq_procedure_instances_tenant_reference</c> (creaciones
    /// concurrentes), regenera el siguiente seq y reintenta. Devuelve <c>false</c> si se agotan
    /// los reintentos (mapear a <c>reference_conflict</c> / 409).
    /// </summary>
    Task<bool> AddWithUniqueReferenceAsync(ProcedureInstance instance, int year, CancellationToken ct = default);

    /// <summary>
    /// Resuelve el <c>FormField.Id</c> de un <paramref name="fieldKey"/> dentro del grafo
    /// steps→sections→fields del <paramref name="procedureTypeId"/>. Null si no existe.
    /// </summary>
    Task<Guid?> GetFormFieldIdByKeyAsync(Guid procedureTypeId, string fieldKey, CancellationToken ct = default);

    Task AddAsync(ProcedureInstance instance, CancellationToken ct = default);

    /// <summary>
    /// Marca explícitamente una entidad NUEVA como <c>Added</c> en el contexto para forzar un
    /// INSERT. Necesario para hijos creados vía colección de navegación cuya PK está mapeada como
    /// store-generated (<c>DEFAULT uuidv7()</c>) pero se asigna en código (<c>Id = Guid.NewGuid()</c>):
    /// EF infiere estado a partir de la PK store-generated y, al verla con valor no-default, asume
    /// que la fila ya existe → la marca <c>Modified</c> → emite UPDATE de 0 filas → DbUpdateConcurrencyException.
    /// <c>Add</c> deja el estado en <c>Added</c> → EF emite INSERT con ese Id.
    /// </summary>
    void Add<TEntity>(TEntity entity) where TEntity : class;

    Task UpdateAsync(ProcedureInstance instance, CancellationToken ct = default);
    void RemoveAttachment(ProcedureInstanceAttachment attachment);
    Task SaveChangesAsync(CancellationToken ct = default);
}
