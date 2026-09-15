namespace Flit.Admin.Domain.Companies.Domains;

/// <summary>
/// Traduce un <c>check_violation</c> de formato de host (<c>ck_tenant_domains_host_format</c> /
/// <c>_host_lower</c> / <c>_host_length</c>, DDL 116) a un error de negocio identificable. Defensa en
/// profundidad: <see cref="HostNormalizer"/> ya valida el formato antes de llegar aquí, así que esta
/// excepción solo aparece si el motor rechaza algo que la aplicación dejó pasar.
/// </summary>
public sealed class DomainHostInvalidException(string host)
    : Exception($"El dominio '{host}' no cumple el formato exigido por la base de datos.")
{
    public string Host { get; } = host;
}
