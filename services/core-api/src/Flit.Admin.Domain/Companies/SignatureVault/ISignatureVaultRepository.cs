namespace Flit.Admin.Domain.Companies.SignatureVault;

/// <summary>
/// Escrituras del baúl de firmas (ADR-0025). El baúl es tenant-scoped: cada operación corre bajo
/// el contexto RLS del tenant (<c>app.current_tenant_id</c>), a diferencia del ámbito OT de los
/// mandatarios. La exclusividad "una firma 'activa' por (tenant, NIT, documento)" la garantiza en
/// última instancia el índice único parcial de BD; el handler la valida antes para un 422 legible.
/// <c>DocumentNumber</c> es PII (Ley 1581): no loguear.
/// </summary>
public interface ISignatureVaultRepository
{
    /// <summary>Alta de una firma del baúl (estado 'activa'). Devuelve el id generado.</summary>
    Task<Guid> CreateAsync(CreateSignatureVaultData data, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revoca una firma (baja lógica: estado → 'revocada'), con efecto inmediato en el próximo
    /// consumo. <c>false</c> si no existe o ya estaba revocada (idempotente).
    /// </summary>
    Task<bool> RevokeAsync(RevokeSignatureVaultData data, CancellationToken cancellationToken = default);
}

/// <summary>
/// Datos de alta de una firma del baúl. <c>DocumentNumber</c> es PII: no loguear. El material
/// criptográfico no viaja aquí: solo el path del artefacto (<c>StoragePath</c>) y su hash.
/// </summary>
public sealed record CreateSignatureVaultData(
    Guid TenantId,
    string DocumentType,
    string DocumentNumber,
    string NitEmpresa,
    string FullName,
    string SignatureHash,
    string StoragePath,
    string StorageSha256,
    DateOnly VigenciaDesde,
    DateOnly VigenciaHasta,
    Guid? MandateSignerId,
    Guid? CreatedBy,
    Guid? CorrelationId);

/// <summary>Datos de revocación de una firma del baúl.</summary>
public sealed record RevokeSignatureVaultData(
    Guid Id,
    Guid TenantId,
    Guid? ChangedBy,
    Guid? CorrelationId);
