namespace Flit.Admin.Application.Companies.Whitelist;

/// <summary>Entrada de la lista blanca en la respuesta API (AC6).</summary>
/// <param name="Email">Correo normalizado exento de la regla.</param>
/// <param name="CreatedAt">Marca temporal de alta.</param>
/// <param name="AddedBy">Operador SuperAdmin que la dio de alta, si se conoce.</param>
public sealed record WhitelistEntryResponse(string Email, DateTimeOffset CreatedAt, Guid? AddedBy);
