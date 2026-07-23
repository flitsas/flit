using System.Security.Claims;
using Flit.Ict.Domain.Abstractions;
using Microsoft.AspNetCore.Http;

namespace Flit.Ict.Infrastructure.Security;

/// <summary>
/// Resuelve el tenant/compañía del token ICT (claims tenant_id, company_nit, sub). El cliente nunca
/// elige tenant por payload/header: siempre sale del token validado.
/// </summary>
public sealed class HttpCurrentTenant(IHttpContextAccessor accessor) : ICurrentTenant
{
    public Guid? TenantId => ParseGuid(GetClaim("tenant_id"));

    public string? CompanyNit => GetClaim("company_nit");

    public Guid? IntegrationClientId => ParseGuid(GetClaim("sub"));

    private string? GetClaim(string type) => accessor.HttpContext?.User.FindFirstValue(type);

    private static Guid? ParseGuid(string? value) => Guid.TryParse(value, out var parsed) ? parsed : null;
}
