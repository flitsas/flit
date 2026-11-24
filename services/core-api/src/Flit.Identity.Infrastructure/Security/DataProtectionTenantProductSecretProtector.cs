using Flit.Modules.Platform.Application.TenantProducts;
using Microsoft.AspNetCore.DataProtection;

namespace Flit.Infrastructure.Security;

/// <summary>
/// Cifrado de credenciales de producto con Data Protection (ADR-0092): propósito
/// <c>Flit.Platform.TenantProductCredential.v1</c> + empresa + código.
/// </summary>
public sealed class DataProtectionTenantProductSecretProtector : ITenantProductSecretProtector
{
    public const string Purpose = "Flit.Platform.TenantProductCredential.v1";

    private readonly IDataProtectionProvider _provider;

    public DataProtectionTenantProductSecretProtector(IDataProtectionProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public string Protect(Guid tenantId, string credentialCode, string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(credentialCode);
        ArgumentException.ThrowIfNullOrEmpty(value);
        return Protector(tenantId, credentialCode).Protect(value);
    }

    public string Unprotect(Guid tenantId, string credentialCode, string protectedValue)
    {
        ArgumentException.ThrowIfNullOrEmpty(credentialCode);
        ArgumentException.ThrowIfNullOrEmpty(protectedValue);
        return Protector(tenantId, credentialCode).Unprotect(protectedValue);
    }

    private IDataProtector Protector(Guid tenantId, string credentialCode) =>
        _provider.CreateProtector(Purpose, tenantId.ToString("N"), credentialCode);
}
