namespace Flit.Admin.Application.Companies.Whitelist.AddWhitelistEmails;

/// <summary>
/// Payload del alta masiva en la lista blanca (AC4): <c>{ "emails": [...] }</c>.
/// </summary>
/// <param name="Emails">Correos a exentar de la regla <c>only_own_vehicles</c>.</param>
/// <param name="Reason">Motivo opcional de la excepción (columna <c>reason</c>).</param>
public sealed record AddWhitelistEmailsRequest(IReadOnlyList<string>? Emails, string? Reason);
