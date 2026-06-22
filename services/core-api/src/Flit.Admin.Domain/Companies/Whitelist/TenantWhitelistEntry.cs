namespace Flit.Admin.Domain.Companies.Whitelist;

/// <summary>
/// Entrada activa de la lista blanca de un tenant — proyección de
/// <c>admin.tenant_whitelist_users</c> (HU #10191, RF05). El DDL no define
/// soft-delete, por lo que toda fila existente se considera activa.
/// </summary>
/// <param name="Email">Correo normalizado exento de la regla <c>only_own_vehicles</c>.</param>
/// <param name="CreatedAt">Marca temporal de alta.</param>
/// <param name="AddedBy">Operador SuperAdmin que dio de alta el correo, si se conoce.</param>
public sealed record TenantWhitelistEntry(string Email, DateTimeOffset CreatedAt, Guid? AddedBy);
