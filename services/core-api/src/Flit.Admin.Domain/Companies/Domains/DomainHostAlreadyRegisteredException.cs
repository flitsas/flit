namespace Flit.Admin.Domain.Companies.Domains;

/// <summary>
/// Traduce la violación de <c>uq_tenant_domains_host</c> (host ya vigente en OTRA red, DDL 116) a un
/// error de negocio identificable (HU #12416 AC1/AC2).
/// </summary>
public sealed class DomainHostAlreadyRegisteredException(string host)
    : Exception($"El dominio '{host}' ya está registrado y vigente para otra red.")
{
    public string Host { get; } = host;
}
