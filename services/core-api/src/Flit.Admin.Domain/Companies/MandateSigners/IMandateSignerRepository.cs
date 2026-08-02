namespace Flit.Admin.Domain.Companies.MandateSigners;

/// <summary>
/// Escrituras de mandatarios (ADR-0023). Cada operación (alta, edición, inactivación) y su
/// reasignación de compañías se persiste junto a su fila de auditoría en
/// <c>admin.tenant_config_audit_logs</c> dentro de <b>una sola transacción</b> (patrón
/// Oleada 1 / ciclo de vida OT). La auditoría se atribuye al tenant del OT
/// (<paramref name="otTenantId"/>), que también fija el GUC <c>app.current_tenant_id</c>.
///
/// La exclusividad (OT, compañía) → un mandatario activo la garantiza en última instancia el
/// índice único parcial de BD; el handler la valida antes para dar un 422 legible.
/// </summary>
public interface IMandateSignerRepository
{
    /// <summary>Alta de un mandatario con sus compañías. Devuelve el id generado.</summary>
    Task<Guid> CreateAsync(CreateMandateSignerData data, CancellationToken cancellationToken = default);

    /// <summary>
    /// Edita nombre/documento/huella y reemplaza el conjunto de compañías del mandatario.
    /// <c>false</c> si el mandatario no existe.
    /// </summary>
    Task<bool> UpdateAsync(UpdateMandateSignerData data, CancellationToken cancellationToken = default);

    /// <summary>
    /// Baja lógica del mandatario (soft-delete): marca inactivo y libera sus compañías para
    /// reasignación. <c>false</c> si no existe o ya estaba inactivo (idempotente).
    /// </summary>
    Task<bool> InactivateAsync(
        InactivateMandateSignerData data,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reactiva un mandatario inactivado: vuelve a marcarlo activo <b>sin</b> restaurar
    /// compañías (se liberaron al inactivar y deben reasignarse). <c>false</c> si no existe o
    /// ya estaba activo (idempotente).
    /// </summary>
    Task<bool> ReactivateAsync(
        ReactivateMandateSignerData data,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Datos de alta de un mandatario. <c>DocumentNumber</c>/<c>Email</c> son PII: no loguear. Los campos
/// ADR-0036 (<c>DocumentType</c>, <c>Email</c>, <c>UserId</c>) son opcionales para no romper callers
/// previos; la firma/identidad se resuelven en flujos posteriores (HU #10911/#10916).
/// </summary>
public sealed record CreateMandateSignerData(
    Guid TransitOfficeId,
    Guid OtTenantId,
    string FullName,
    string DocumentNumber,
    string IntegrityHash,
    DateTimeOffset RegisteredAt,
    IReadOnlyList<Guid> CompanyTenantIds,
    Guid? CreatedBy,
    Guid? CorrelationId,
    string DocumentType = "CC",
    string? Email = null,
    Guid? UserId = null,
    /// <summary>
    /// HU #11201 — organismos donde aplica el mandatario. Vacío ⇒ solo
    /// <see cref="TransitOfficeId"/>, que es lo que hace el alta desde el perfil del organismo.
    /// <see cref="TransitOfficeId"/> queda como organismo PRIMARIO (deprecado, se conserva por
    /// compatibilidad); la lista es la que decide dónde puede firmar.
    /// </summary>
    IReadOnlyList<Guid>? TransitOfficeIds = null,
    /// <summary>
    /// Organismos (subconjunto de los anteriores) en los que este mandatario firma A MANO: el contrato
    /// deja la línea de guiones bajos con sus datos debajo y no estampa firma del baúl ni sello de
    /// identidad. Va por organismo y no por persona porque la misma puede firmar a mano ante uno y
    /// electrónicamente ante otro.
    /// </summary>
    IReadOnlyList<Guid>? PhysicalSignatureOfficeIds = null,
    /// <summary>
    /// Firma del baúl elegida para el mandatario. <c>null</c> ⇒ el trámite la resuelve por documento,
    /// que es el comportamiento previo.
    /// </summary>
    Guid? SignatureVaultId = null,
    /// <summary>
    /// Empresas representadas para las que firma, POR ORGANISMO. Vacío o ausente ⇒ el mandatario aplica
    /// a todas las empresas de ese organismo, que es como se comportan los que ya existen.
    /// </summary>
    IReadOnlyList<MandateSignerOfficeCompanies>? OfficeCompanies = null);

/// <summary>Datos de edición. La huella ya viene recalculada con la fecha de registro original.</summary>
public sealed record UpdateMandateSignerData(
    Guid MandateSignerId,
    Guid OtTenantId,
    string FullName,
    string DocumentNumber,
    string IntegrityHash,
    IReadOnlyList<Guid> CompanyTenantIds,
    Guid? UpdatedBy,
    Guid? CorrelationId,
    string DocumentType = "CC",
    string? Email = null,
    Guid? UserId = null,
    /// <summary>
    /// HU #11201 — conjunto deseado de organismos. <c>null</c> ⇒ no se tocan (la edición desde el
    /// perfil del organismo solo cambia datos personales y compañías, AC2). Una lista, aunque esté
    /// vacía, REEMPLAZA el conjunto: los organismos que no vengan se retiran (AC3).
    /// </summary>
    IReadOnlyList<Guid>? TransitOfficeIds = null,
    /// <summary>
    /// Organismos (subconjunto de los anteriores) en los que este mandatario firma A MANO: el contrato
    /// deja la línea de guiones bajos con sus datos debajo y no estampa firma del baúl ni sello de
    /// identidad. Va por organismo y no por persona porque la misma puede firmar a mano ante uno y
    /// electrónicamente ante otro.
    /// </summary>
    IReadOnlyList<Guid>? PhysicalSignatureOfficeIds = null,
    /// <summary>
    /// Firma del baúl elegida para el mandatario. <c>null</c> ⇒ el trámite la resuelve por documento,
    /// que es el comportamiento previo.
    /// </summary>
    Guid? SignatureVaultId = null,
    /// <summary>
    /// Empresas representadas para las que firma, POR ORGANISMO. Vacío o ausente ⇒ el mandatario aplica
    /// a todas las empresas de ese organismo, que es como se comportan los que ya existen.
    /// </summary>
    IReadOnlyList<MandateSignerOfficeCompanies>? OfficeCompanies = null,
    /// <summary>
    /// El llamante gestiona la firma del baúl y <see cref="SignatureVaultId"/> es su valor deseado
    /// (incluido <c>null</c>, que la desvincula). En <c>false</c> la firma NO se toca.
    ///
    /// <para>Hace falta porque <c>Guid?</c> no distingue "no la gestiono" de "quítala": la edición desde
    /// el perfil del organismo no maneja este campo, y sin esta señal cada guardado suyo borraría la
    /// firma que la compañía acababa de elegir.</para>
    /// </summary>
    bool ActualizaFirma = false,
    /// <summary>
    /// Organismo que pasa a ser el PRIMARIO (<c>mandate_signers.transit_office_id</c>). Solo hace falta
    /// cuando la edición retira de la lista al primario actual: sin repuntarlo, la fila quedaría
    /// apuntando a un organismo donde el mandatario ya no aplica, y la reactivación —que restaura el
    /// primario— lo resucitaría. <c>null</c> ⇒ se conserva el que ya tiene.
    /// </summary>
    Guid? NuevoOrganismoPrimario = null);

/// <summary>
/// Empresas representadas que un mandatario atiende en un organismo. La lista vacía significa "todas":
/// no hay forma de decir "ninguna", porque un mandatario sin empresas no podría firmar nada.
/// </summary>
public sealed record MandateSignerOfficeCompanies(
    Guid TransitOfficeId,
    IReadOnlyList<Guid> RepresentedCompanyIds);

/// <summary>Datos de inactivación.</summary>
public sealed record InactivateMandateSignerData(
    Guid MandateSignerId,
    Guid OtTenantId,
    Guid? ChangedBy,
    Guid? CorrelationId);

/// <summary>Datos de reactivación.</summary>
public sealed record ReactivateMandateSignerData(
    Guid MandateSignerId,
    Guid OtTenantId,
    Guid? ChangedBy,
    Guid? CorrelationId);
