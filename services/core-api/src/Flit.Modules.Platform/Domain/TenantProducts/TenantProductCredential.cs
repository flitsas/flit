namespace Flit.Modules.Platform.Domain.TenantProducts;

/// <summary>
/// Credencial de un producto por empresa (<c>platform.tenant_product_credentials</c>, ADR-0092).
/// El valor en <see cref="SecretProtected"/> ya va cifrado; nunca se expone en <c>ToString</c>.
/// </summary>
public sealed record TenantProductCredential(
    Guid TenantId,
    string ProductCode,
    string CredentialCode,
    string SecretProtected,
    long RowVersion,
    DateTimeOffset UpdatedAt)
{
    public override string ToString() =>
        $"TenantProductCredential {{ TenantId = {TenantId}, ProductCode = {ProductCode}, CredentialCode = {CredentialCode}, RowVersion = {RowVersion} }}";
}

/// <summary>Resultado de guardar una credencial: creada o reemplazada.</summary>
public enum TenantProductCredentialWrite
{
    Created = 0,
    Replaced = 1,
}
