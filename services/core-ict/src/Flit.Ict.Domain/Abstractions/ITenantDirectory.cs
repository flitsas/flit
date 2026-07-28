namespace Flit.Ict.Domain.Abstractions;

/// <summary>Datos mínimos del tenant necesarios para emitir el token ICT.</summary>
public sealed record TenantInfo(Guid Id, string Code, string LegalName, string TaxId, bool IsActive);

/// <summary>Lectura de <c>identity.tenants</c> (propiedad de core-api) para resolver la compañía.</summary>
public interface ITenantDirectory
{
    Task<TenantInfo?> GetAsync(Guid tenantId, CancellationToken ct = default);
}
