namespace Flit.Modules.Security.Domain.Auth;

/// <summary>Token de un solo uso: valor crudo (viaja en el enlace) y su hash (se persiste).</summary>
public sealed record GeneratedToken(string RawToken, string TokenHash);

/// <summary>
/// Genera tokens aleatorios criptográficamente seguros y calcula su hash para
/// almacenamiento. Solo el hash se guarda en <c>security.password_reset_tokens</c>;
/// el valor crudo nunca se persiste.
/// </summary>
public interface ISecureTokenGenerator
{
    GeneratedToken Generate();

    /// <summary>Calcula el hash de un token crudo recibido, para buscarlo por <c>token_hash</c>.</summary>
    string HashToken(string rawToken);
}
