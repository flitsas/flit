using Flit.Tramites.Domain.Entities;

namespace Flit.Tramites.Domain.Repositories;

public interface IProcedureInstanceRepository
{
    Task<ProcedureInstance?> GetByIdAsync(Guid id, Guid tenantId, CancellationToken ct = default);
    Task<ProcedureInstance?> GetByIdWithDetailsAsync(Guid id, Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Carga la instancia con únicamente sus <c>Actors</c>. Query lean para operaciones
    /// PUT/GET de actores que no necesitan FieldValues ni StatusHistory.
    /// </summary>
    Task<ProcedureInstance?> GetByIdWithActorsAsync(Guid id, Guid tenantId, CancellationToken ct = default);

    Task<ProcedureInstance?> GetByIdWithAttachmentsAsync(Guid id, Guid tenantId, CancellationToken ct = default);

    /// <summary>Carga la instancia con TODO el grafo del wizard: actores, field values, adjuntos,
    /// datos comerciales y snapshots de preflight (Slice 4 — wizard server-driven).</summary>
    Task<ProcedureInstance?> GetByIdWithWizardGraphAsync(Guid id, Guid tenantId, CancellationToken ct = default);

    /// <summary>Carga la instancia con sus datos comerciales (1:1) para GET/PUT comercial.</summary>
    Task<ProcedureInstance?> GetByIdWithCommercialAsync(Guid id, Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Lista las instancias de un tenant (no eliminadas), las más recientes primero, cargando el grafo
    /// del wizard necesario para el resumen de la tabla de operación (Slice M6): field values
    /// (placa/VIN/marca/línea), actores (comprador), adjuntos (FUR), comercial, snapshots de preflight,
    /// biométricas y firmas — el mismo grafo que consume <c>GetWizardStateHandler.ComputeState</c> para
    /// derivar el paso actual. Limitado a <paramref name="limit"/> filas (cap razonable, p.ej. 200).
    /// </summary>
    Task<IReadOnlyList<ProcedureInstance>> ListByTenantWithSummaryGraphAsync(Guid tenantId, int limit, CancellationToken ct = default);

    /// <summary>Carga la instancia con sus validaciones biométricas (Slice 6).</summary>
    Task<ProcedureInstance?> GetByIdWithBiometricsAsync(Guid id, Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Lista las validaciones biométricas del tenant a través de TODAS sus instancias no eliminadas,
    /// las más recientes primero, incluyendo la instancia padre (referencia/modalidad) para la vista
    /// transversal del submódulo "Validaciones de Identidad" (HU #10234). Solo lectura (AsNoTracking),
    /// acotada a <paramref name="limit"/> filas (cap de monitoreo, no exporta el histórico completo).
    /// </summary>
    Task<IReadOnlyList<ProcedureInstanceBiometricValidation>> ListBiometricValidationsByTenantAsync(Guid tenantId, int limit, CancellationToken ct = default);

    /// <summary>
    /// Cuenta las validaciones biométricas del tenant agrupadas por estado (KPIs del submódulo de
    /// Validaciones). Independiente del cap de filas de <see cref="ListBiometricValidationsByTenantAsync"/>
    /// para que los totales sean exactos.
    /// </summary>
    Task<IReadOnlyDictionary<string, int>> CountBiometricValidationsByEstadoAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Carga la instancia con sus validaciones biométricas + actores (Slice M4 — simular biométrica:
    /// resuelve el actor de la parte para poblar nombre/documento/email de la validación aprobada).
    /// </summary>
    Task<ProcedureInstance?> GetByIdWithBiometricsAndActorsAsync(Guid id, Guid tenantId, CancellationToken ct = default);

    /// <summary>Carga la instancia con sus firmas electrónicas (Slice 7).</summary>
    Task<ProcedureInstance?> GetByIdWithSignaturesAsync(Guid id, Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Carga la instancia con TODO el grafo necesario para generar el FUR (Slice 7): actores,
    /// field values, adjuntos, comercial, biométricas y firmas.
    /// </summary>
    Task<ProcedureInstance?> GetByIdWithFurGraphAsync(Guid id, Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Resuelve una validación biométrica por el hash SHA-256 de su token (acceso PÚBLICO vía
    /// magic-link, sin tenant). Devuelve null si no existe — el caller NO debe filtrar existencia.
    /// </summary>
    Task<ProcedureInstanceBiometricValidation?> GetBiometricByTokenHashAsync(string tokenHash, CancellationToken ct = default);

    /// <summary>
    /// Resuelve una validación biométrica por su id (acceso PÚBLICO vía webhook de Kyverum, sin tenant).
    /// La correlación del webhook se hace por este id porque viaja incrustado en la <c>webhookUrl</c>
    /// registrada (el cuerpo del webhook no lo repite). Devuelve null si no existe — el caller NO debe
    /// filtrar existencia (HU #10233, AC2/AC3).
    /// </summary>
    Task<ProcedureInstanceBiometricValidation?> GetBiometricByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Carga la instancia con sus participantes del portal (Slice 7 Part B, vista del gestor).</summary>
    Task<ProcedureInstance?> GetByIdWithParticipantsAsync(Guid id, Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Resuelve un participante del portal por el hash SHA-256 de su token (acceso PÚBLICO vía
    /// magic-link, sin tenant). Carga la instancia con el grafo necesario para agregar el estado de
    /// los pasos del rol (biométricas, firmas, adjuntos). Devuelve null si no existe — el caller NO
    /// debe filtrar existencia (not_found genérico, sin enumeración de PII).
    /// </summary>
    Task<ProcedureInstanceParticipant?> GetParticipantByTokenHashAsync(string tokenHash, CancellationToken ct = default);

    /// <summary>Encola un evento de bitácora (append-only) para persistir en el próximo SaveChanges.</summary>
    Task AddEventAsync(ProcedureInstanceEvent evt, CancellationToken ct = default);

    /// <summary>Último snapshot de preflight de la instancia (por created_at desc), o null.</summary>
    Task<ProcedureInstancePreflightSnapshot?> GetLatestPreflightAsync(Guid id, Guid tenantId, CancellationToken ct = default);

    /// <summary>Encola un nuevo snapshot de preflight para persistir en el próximo SaveChanges.</summary>
    Task AddPreflightSnapshotAsync(ProcedureInstancePreflightSnapshot snapshot, CancellationToken ct = default);

    Task<int> CountByTenantAndYearAsync(Guid tenantId, int year, CancellationToken ct = default);

    /// <summary>
    /// Inserta la instancia generando un <c>ReferenceNumber</c> único con formato
    /// <c>TRM-{year}-{seq:D6}</c> a partir de MAX(seq) + 1 por (tenant, year). Si el insert
    /// colisiona contra el constraint <c>uq_procedure_instances_tenant_reference</c> (creaciones
    /// concurrentes), regenera el siguiente seq y reintenta. Si una FK no existe
    /// (tenant/usuario/tipo) devuelve <c>ReferencedEntityMissing</c> (→ 422); si se agotan los
    /// reintentos de referencia devuelve <c>ReferenceConflict</c> (→ 409).
    /// </summary>
    Task<AddProcedureInstanceOutcome> AddWithUniqueReferenceAsync(ProcedureInstance instance, int year, CancellationToken ct = default);

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
