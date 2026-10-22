namespace Flit.Admin.Domain.Integrations;

/// <summary>
/// HU #13087 — emisor del pase de los clientes externos: JWT RS256 con llave, emisor y audiencia propios,
/// distintos de los de la plataforma y de ICT.
/// </summary>
public interface IExternalClientTokenIssuer
{
    /// <summary><c>false</c> si no hay llave configurada fuera de Development: no se emite ningún pase.</summary>
    bool IsAvailable { get; }

    ExternalAccessToken Issue(string clientId, IReadOnlyList<string> scopes);
}

/// <summary>Pase emitido y su vigencia en segundos.</summary>
public sealed record ExternalAccessToken(string Token, int ExpiresInSeconds);
