namespace Flit.Ict.Domain.Abstractions;

/// <summary>Token de acceso emitido para un cliente de integración.</summary>
public sealed record IssuedIctToken(string Token, int ExpiresInSeconds);

/// <summary>
/// Emisor del JWT propio de ICT (aud=flit-ict, issuer/llave RSA distintos del token de plataforma).
/// </summary>
public interface IIctJwtTokenIssuer
{
    IssuedIctToken Issue(Guid integrationClientId, Guid tenantId, string tenantName, IReadOnlyList<string> scopes);
}
