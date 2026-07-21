using Flit.Admin.Domain.Companies.SignatureVault;

namespace Flit.Admin.Application.Companies.SignatureVault.ListSignatureVault;

/// <summary>Lista las firmas del baúl de un tenant (activas primero; ADR-0025 §5).</summary>
public sealed class ListSignatureVaultHandler
{
    private readonly ISignatureVaultReader _reader;

    public ListSignatureVaultHandler(ISignatureVaultReader reader)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
    }

    public async Task<IReadOnlyList<SignatureVaultResponse>> HandleAsync(
        ListSignatureVaultQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var items = await _reader
            .ListByTenantAsync(query.TenantId, cancellationToken).ConfigureAwait(false);

        return [.. items.Select(SignatureVaultResponse.From)];
    }
}
