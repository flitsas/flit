namespace Flit.Admin.Domain.Companies.SignatureVault;

/// <summary>
/// Read model de una firma del baúl para la gestión admin (listado / detalle) — ADR-0025 §5.
/// <c>DocumentNumber</c> es PII (Ley 1581): se entrega solo en respuestas autenticadas de
/// gestión y nunca debe registrarse en logs ni aparecer en mensajes de error. El read model
/// <b>no</b> expone material de firma: solo <c>StoragePath</c>/<c>StorageSha256</c>.
/// </summary>
public sealed class SignatureVaultItem
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public string DocumentType { get; init; } = string.Empty;
    public string DocumentNumber { get; init; } = string.Empty;
    /// <summary>NIT deprecado (HU #10930, Feature #10929): nullable durante la transición.</summary>
    public string? NitEmpresa { get; init; }
    public string FullName { get; init; } = string.Empty;
    public string SignatureHash { get; init; } = string.Empty;
    /// <summary>Código alfanumérico digitado por el usuario (distinto del SHA-256 del artefacto).</summary>
    public string? CodigoHash { get; init; }
    public string StoragePath { get; init; } = string.Empty;
    public string StorageSha256 { get; init; } = string.Empty;
    public SignatureVaultEstado Estado { get; init; }
    public DateOnly VigenciaDesde { get; init; }
    public DateOnly VigenciaHasta { get; init; }
    public Guid? MandateSignerId { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
}
