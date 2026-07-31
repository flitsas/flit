namespace Flit.Admin.Application.Companies.SignatureVault.ListSignatureVault;

/// <summary>Consulta de las firmas del baúl de un tenant (activas y revocadas).</summary>
public sealed class ListSignatureVaultQuery
{
    public required Guid TenantId { get; init; }
}
