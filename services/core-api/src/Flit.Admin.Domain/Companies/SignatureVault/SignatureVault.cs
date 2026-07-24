namespace Flit.Admin.Domain.Companies.SignatureVault;

/// <summary>
/// Estado del baúl de firmas (ADR-0025 §2). Baja lógica explícita: una firma se retira
/// revocándola, no borrándola.
/// </summary>
public enum SignatureVaultEstado
{
    /// <summary>Firma vigente y disponible para consumo (<c>estado = 'activa'</c>).</summary>
    Activa,

    /// <summary>Firma dada de baja; no vuelve a consumirse (<c>estado = 'revocada'</c>).</summary>
    Revocada,
}

/// <summary>
/// Agregado de baúl de firmas: la firma digital precargada de un apoderado, custodiada por
/// compañía (NIT) y aislada por tenant (ADR-0025). El material criptográfico <b>nunca</b> vive
/// en el agregado: solo se referencia el artefacto en storage (<see cref="StoragePath"/> +
/// <see cref="StorageSha256"/>). <see cref="DocumentNumber"/> es PII (Ley 1581): no loguear ni
/// exponer en errores.
///
/// La vigencia (<see cref="VigenciaDesde"/>/<see cref="VigenciaHasta"/>) y el estado se enforce
/// en el consumo con <see cref="EstaVigente"/>; la revocación surte efecto inmediato.
/// </summary>
public sealed class SignatureVault
{
    private SignatureVault(
        Guid id,
        Guid tenantId,
        string documentType,
        string documentNumber,
        string? nitEmpresa,
        string fullName,
        string signatureHash,
        string storagePath,
        string storageSha256,
        SignatureVaultEstado estado,
        DateOnly vigenciaDesde,
        DateOnly vigenciaHasta,
        Guid? mandateSignerId,
        string? codigoHash)
    {
        Id = id;
        TenantId = tenantId;
        DocumentType = documentType;
        DocumentNumber = documentNumber;
        NitEmpresa = nitEmpresa;
        FullName = fullName;
        SignatureHash = signatureHash;
        StoragePath = storagePath;
        StorageSha256 = storageSha256;
        Estado = estado;
        VigenciaDesde = vigenciaDesde;
        VigenciaHasta = vigenciaHasta;
        MandateSignerId = mandateSignerId;
        CodigoHash = codigoHash;
    }

    public Guid Id { get; }

    public Guid TenantId { get; }

    public string DocumentType { get; }

    /// <summary>PII (Ley 1581): no loguear ni exponer.</summary>
    public string DocumentNumber { get; }

    /// <summary>
    /// NIT de la compañía. DEPRECADO (HU #10930, Feature #10929): la firma es de la persona +
    /// tenant; el NIT ya no es obligatorio ni participa en la unicidad de la firma activa.
    /// </summary>
    public string? NitEmpresa { get; }

    public string FullName { get; }

    public string SignatureHash { get; }

    /// <summary>
    /// Código alfanumérico que digita el usuario (HU #10930), DISTINTO de <see cref="SignatureHash"/>
    /// (que es el SHA-256 calculado del artefacto). Opcional.
    /// </summary>
    public string? CodigoHash { get; }

    public string StoragePath { get; }

    public string StorageSha256 { get; }

    public SignatureVaultEstado Estado { get; private set; }

    public DateOnly VigenciaDesde { get; }

    public DateOnly VigenciaHasta { get; }

    public Guid? MandateSignerId { get; }

    /// <summary>
    /// Crea una firma nueva del baúl en estado <see cref="SignatureVaultEstado.Activa"/>. La
    /// exclusividad "una activa por (tenant, NIT, documento)" la garantiza en última instancia
    /// el índice único parcial de BD; el handler la valida antes para un 422 legible.
    /// </summary>
    public static SignatureVault Create(
        Guid tenantId,
        string documentType,
        string documentNumber,
        string? nitEmpresa,
        string fullName,
        string signatureHash,
        string storagePath,
        string storageSha256,
        DateOnly vigenciaDesde,
        DateOnly vigenciaHasta,
        Guid? mandateSignerId = null,
        string? codigoHash = null,
        Guid? id = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentType);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentNumber);
        // nitEmpresa DEPRECADO (HU #10930): ya no es obligatorio; solo se normaliza si viene.
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);
        ArgumentException.ThrowIfNullOrWhiteSpace(signatureHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(storagePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(storageSha256);

        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("El tenant del baúl de firmas es obligatorio.", nameof(tenantId));
        }

        if (vigenciaHasta < vigenciaDesde)
        {
            throw new ArgumentException(
                "La vigencia_hasta no puede ser anterior a vigencia_desde.", nameof(vigenciaHasta));
        }

        return new SignatureVault(
            id ?? Guid.NewGuid(),
            tenantId,
            documentType.Trim(),
            documentNumber.Trim(),
            nitEmpresa?.Trim(),
            fullName.Trim(),
            signatureHash.Trim(),
            storagePath.Trim(),
            storageSha256.Trim(),
            SignatureVaultEstado.Activa,
            vigenciaDesde,
            vigenciaHasta,
            mandateSignerId,
            codigoHash?.Trim());
    }

    /// <summary>
    /// Rehidrata el agregado desde persistencia (no aplica invariantes de alta; refleja el
    /// estado ya almacenado).
    /// </summary>
    public static SignatureVault Rehydrate(
        Guid id,
        Guid tenantId,
        string documentType,
        string documentNumber,
        string? nitEmpresa,
        string fullName,
        string signatureHash,
        string storagePath,
        string storageSha256,
        SignatureVaultEstado estado,
        DateOnly vigenciaDesde,
        DateOnly vigenciaHasta,
        Guid? mandateSignerId,
        string? codigoHash) =>
        new(id, tenantId, documentType, documentNumber, nitEmpresa, fullName, signatureHash,
            storagePath, storageSha256, estado, vigenciaDesde, vigenciaHasta, mandateSignerId, codigoHash);

    /// <summary>
    /// Revoca la firma (baja lógica). Idempotente: revocar una firma ya revocada no cambia nada.
    /// </summary>
    public void Revocar() => Estado = SignatureVaultEstado.Revocada;

    /// <summary>
    /// La firma es consumible en la fecha dada: está <see cref="SignatureVaultEstado.Activa"/>
    /// y <paramref name="today"/> cae dentro de [<see cref="VigenciaDesde"/>,
    /// <see cref="VigenciaHasta"/>] (ambos inclusive). El llamador aporta la fecha en hora
    /// Colombia (UTC-5), coherente con el enforce de BD (ADR-0025 §3).
    /// </summary>
    public bool EstaVigente(DateOnly today) =>
        Estado == SignatureVaultEstado.Activa &&
        today >= VigenciaDesde &&
        today <= VigenciaHasta;
}
