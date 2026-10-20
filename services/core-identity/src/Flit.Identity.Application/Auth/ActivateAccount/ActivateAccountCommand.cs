namespace Flit.Modules.Security.Application.Auth.ActivateAccount;

public sealed record ActivateAccountCommand(string Token, string Password);

public sealed record AccountActivatedResult();
