namespace Flit.Modules.Security.Application.Auth.Login;

public sealed record LoginCommand(string Email, string Password);

/// <summary>
/// <paramref name="NetworkHost"/> (HU #12422 AC1/AC7, ADR-0060 D3) es aditivo y opcional: solo
/// viene con valor cuando el login se hizo por el dominio de una red MARCA_BLANCA — el endpoint
/// lo expone como <c>network: { host }</c> sin tocar la forma del resto de la respuesta.
/// </summary>
public sealed record LoginResult(
    string AccessToken,
    int ExpiresInSeconds,
    string TokenType,
    string? NetworkHost = null);
