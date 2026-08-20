namespace Flit.Admin.Domain.Companies.SignatureVault;

/// <summary>
/// Lecturas del baúl de firmas (ADR-0025). El baúl es tenant-scoped: las lecturas corren bajo el
/// contexto RLS del tenant (<c>app.current_tenant_id</c>). La firma vigente para el consumo se
/// resuelve por (tenant, NIT); la vigencia efectiva la decide el agregado con
/// <see cref="SignatureVault.EstaVigente"/>. <c>DocumentNumber</c> es PII (Ley 1581): no loguear.
/// </summary>
public interface ISignatureVaultReader
{
    /// <summary>
    /// Firma 'activa' del baúl para una compañía (por NIT) dentro del tenant. Base del consumo
    /// (ADR-0025 §4). Devuelve el agregado rehidratado para que el llamador aplique
    /// <see cref="SignatureVault.EstaVigente"/>; <c>null</c> si no hay ninguna activa.
    /// </summary>
    Task<SignatureVault?> FindActiveByNitAsync(
        Guid tenantId,
        string nitEmpresa,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Firma 'activa' del baúl para una PERSONA (por tipo + número de documento) dentro del tenant
    /// (HU #10930, Feature #10929): la firma es de la persona + tenant, ya no depende del NIT.
    /// Devuelve la más reciente rehidratada para que el llamador aplique
    /// <see cref="SignatureVault.EstaVigente"/>; <c>null</c> si no hay ninguna activa.
    /// <c>documentNumber</c> es PII (Ley 1581): no loguear.
    /// <para>
    /// <b>Bug #11659 — el empate es por tipo Y número.</b> Esta lectura ACREDITA a una persona como
    /// firmante; el par (tipo, número) es su identidad, igual que en la validación biométrica. El
    /// empate se hace con la normalización canónica única (<c>Trim</c> + mayúsculas invariantes en
    /// AMBAS partes): una <c>TI 123</c> nunca acredita a la <c>CC 123</c>, y una cédula de extranjería
    /// capturada como <c>ab123</c> sí acredita a <c>AB123</c>.
    /// </para>
    /// </summary>
    Task<SignatureVault?> FindActiveByDocumentAsync(
        Guid tenantId,
        string documentType,
        string documentNumber,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Firma 'activa' del baúl por NÚMERO de documento, sin mirar el tipo (Bug #11659).
    /// <para>
    /// <b>No es una lectura de acreditación y no debe usarse como tal:</b> existe solo para el camino
    /// de ESCRITURA, que necesita ver exactamente lo que ve el índice único parcial
    /// <c>uq_signature_vault_activa</c>, definido sobre <c>(tenant_id, document_number)</c> con filtro
    /// <c>estado = 'activa'</c>. Cuando el alta choca contra ese índice (HU #11193: la última firma
    /// capturada sustituye a la anterior), hay que poder resolver la fila que ocupa el sitio aunque su
    /// <c>document_type</c> difiera del que trae el alta; si no, la sustitución degrada a un 422 que
    /// deja al usuario sin salida dentro del formulario.
    /// </para>
    /// <para>
    /// Empate por igualdad exacta tras <c>Trim</c> (sin mayúsculas), que es la semántica del índice.
    /// <c>documentNumber</c> es PII (Ley 1581): no loguear.
    /// </para>
    /// </summary>
    Task<SignatureVault?> FindActiveByNumberAsync(
        Guid tenantId,
        string documentNumber,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Firmas del baúl del tenant (activas y revocadas) para la gestión admin, ordenadas primero
    /// las activas y luego por creación descendente.
    /// </summary>
    Task<IReadOnlyList<SignatureVaultItem>> ListByTenantAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Una firma por id dentro del tenant (cualquier estado). <c>null</c> si no existe. Usada para
    /// precargar el detalle / formulario de gestión.
    /// </summary>
    Task<SignatureVaultItem?> GetByIdAsync(
        Guid tenantId,
        Guid id,
        CancellationToken cancellationToken = default);
}
