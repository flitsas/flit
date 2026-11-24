namespace Flit.Modules.Platform.Application.TenantProducts;

/// <summary>
/// Cifrado de credenciales de producto (ADR-0092). Propósito
/// <c>Flit.Platform.TenantProductCredential.v1</c> + empresa + código.
/// </summary>
public interface ITenantProductSecretProtector
{
    string Protect(Guid tenantId, string credentialCode, string value);

    string Unprotect(Guid tenantId, string credentialCode, string protectedValue);
}
