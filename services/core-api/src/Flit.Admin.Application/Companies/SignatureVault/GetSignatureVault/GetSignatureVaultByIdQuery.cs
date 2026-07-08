namespace Flit.Admin.Application.Companies.SignatureVault.GetSignatureVault;

/// <summary>Consulta de una firma del baúl por id dentro del tenant.</summary>
public sealed class GetSignatureVaultByIdQuery
{
    public required Guid TenantId { get; init; }
    public required Guid Id { get; init; }
}
