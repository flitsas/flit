namespace Flit.Modules.Security.Application.Auth.Login;

public sealed record LoginCommand(string Email, string Password);

public sealed record LoginResult(string AccessToken, int ExpiresInSeconds, string TokenType);
