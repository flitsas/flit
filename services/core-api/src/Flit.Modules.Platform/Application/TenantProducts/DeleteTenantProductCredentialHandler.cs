using Flit.Modules.Platform.Domain.TenantProducts;

namespace Flit.Modules.Platform.Application.TenantProducts;

/// <summary>
/// Borra la API key del impuesto de una empresa (ADR-0092). Idempotente: sin fila no falla.
/// </summary>
public sealed class DeleteTenantProductCredentialHandler
{
    private readonly ITenantProductCredentialRepository _repository;

    public DeleteTenantProductCredentialHandler(ITenantProductCredentialRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<bool> HandleAsync(
        Guid tenantId,
        string productCode,
        string credentialCode,
        Guid? changedBy,
        CancellationToken cancellationToken = default)
    {
        var (product, credential) = TenantProductCredentialCodes.Normalize(productCode, credentialCode);
        if (!TenantProductCredentialCodes.IsSupported(product, credential))
            throw new TenantProductException(TenantProductException.CredentialNotSupported, "Credencial no soportada para este producto.");

        return await _repository.DeleteAsync(tenantId, product, credential, changedBy, cancellationToken).ConfigureAwait(false);
    }
}
