using Flit.Admin.Application.Companies.SignatureVault.ListSignatureVault;
using Flit.Admin.Domain.Companies.SignatureVault;

namespace Flit.Admin.Application.Companies.SignatureVault.GetSignatureVault;

/// <summary>Obtiene una firma del baúl por id dentro del tenant; <c>null</c> si no existe.</summary>
public sealed class GetSignatureVaultByIdHandler
{
    private readonly ISignatureVaultReader _reader;

    public GetSignatureVaultByIdHandler(ISignatureVaultReader reader)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
    }

    public async Task<SignatureVaultResponse?> HandleAsync(
        GetSignatureVaultByIdQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var item = await _reader
            .GetByIdAsync(query.TenantId, query.Id, cancellationToken).ConfigureAwait(false);

        return item is null ? null : SignatureVaultResponse.From(item);
    }
}
