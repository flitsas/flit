namespace Flit.Admin.Application.Companies.SignatureVault.RevokeSignatureVault;

/// <summary>Comando de revocación (baja lógica) de una firma del baúl.</summary>
public sealed class RevokeSignatureVaultCommand
{
    public required Guid TenantId { get; init; }
    public required Guid Id { get; init; }
    public Guid? ChangedBy { get; init; }
    public Guid? CorrelationId { get; init; }
}
