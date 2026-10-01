using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Flit.Admin.Domain.Integrations;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace Flit.Infrastructure.Security;

/// <summary>
/// HU #13087 — configuración del pase de los clientes externos (sección <c>ExternalJwt</c>). Emisor,
/// audiencia y llave propios: un pase de la plataforma o de ICT no valida aquí, ni al revés.
/// </summary>
public sealed class ExternalJwtSettings
{
    public const string SectionName = "ExternalJwt";

    public string Issuer { get; init; } = "flit-core-external";

    public string Audience { get; init; } = "flit-external";

    public string PrivateKeyPem { get; init; } = string.Empty;

    public string PrivateKeyPath { get; init; } = string.Empty;

    public int TokenLifetimeMinutes { get; init; } = 30;
}

/// <summary>
/// Llave RSA del pase externo. Emisor y validador viven en el mismo proceso (Flit.Api), así que
/// comparten esta instancia. Sin llave configurada: en Development se genera una en memoria (los pases
/// mueren al reiniciar y el cliente pide otro ante el 401, contrato §2); fuera de Development no hay
/// llave, no se emite nada (503) y la validación usa una llave aleatoria que nada firma (cerrado).
/// </summary>
public sealed partial class ExternalJwtKeyMaterial : IDisposable
{
    private readonly RSA _rsa;

    public ExternalJwtKeyMaterial(ExternalJwtSettings settings, IHostEnvironment environment, ILogger<ExternalJwtKeyMaterial> logger)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(logger);

        Issuer = settings.Issuer;
        Audience = settings.Audience;
        _rsa = RSA.Create(2048);

        var pem = settings.PrivateKeyPem;
        if (string.IsNullOrWhiteSpace(pem) && !string.IsNullOrWhiteSpace(settings.PrivateKeyPath)
            && File.Exists(settings.PrivateKeyPath))
        {
            pem = File.ReadAllText(settings.PrivateKeyPath);
        }

        if (!string.IsNullOrWhiteSpace(pem))
        {
            _rsa.ImportFromPem(pem);
            IsAvailable = true;
        }
        else if (environment.IsDevelopment())
        {
            IsAvailable = true;
            LogDevelopmentKey(logger);
        }
        else
        {
            IsAvailable = false;
            LogMissingKey(logger);
        }

        SigningKey = new RsaSecurityKey(_rsa) { KeyId = "flit-external" };
    }

    /// <summary><c>false</c> fuera de Development sin llave: la llave existe pero no firma pases.</summary>
    public bool IsAvailable { get; }

    public RsaSecurityKey SigningKey { get; }

    public string Issuer { get; }

    public string Audience { get; }

    public void Dispose() => _rsa.Dispose();

    [LoggerMessage(Level = LogLevel.Warning, Message = "ExternalJwt sin llave configurada: se usa una llave efímera de desarrollo.")]
    private static partial void LogDevelopmentKey(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "ExternalJwt sin llave configurada fuera de Development: POST /api/v1/external/auth/token responde 503 y ningún pase externo valida.")]
    private static partial void LogMissingKey(ILogger logger);
}

/// <summary>
/// HU #13087 — emisor del pase externo (RS256). Claims mínimos: <c>sub</c> y <c>client_id</c> con el
/// identificador del cliente, un <c>scope</c> por permiso y <c>jti</c>. Sin compañía, roles ni permisos
/// de la plataforma.
/// </summary>
public sealed class ExternalJwtTokenIssuer(
    ExternalJwtKeyMaterial keyMaterial, ExternalJwtSettings settings, TimeProvider timeProvider)
    : IExternalClientTokenIssuer
{
    public const string ClientIdClaim = "client_id";
    public const string ScopeClaim = "scope";

    public bool IsAvailable => keyMaterial.IsAvailable;

    public ExternalAccessToken Issue(string clientId, IReadOnlyList<string> scopes)
    {
        ArgumentNullException.ThrowIfNull(clientId);
        ArgumentNullException.ThrowIfNull(scopes);
        if (!keyMaterial.IsAvailable)
        {
            throw new InvalidOperationException("No hay llave configurada para emitir pases externos.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var lifetime = TimeSpan.FromMinutes(settings.TokenLifetimeMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, clientId),
            new(ClientIdClaim, clientId),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
        };
        claims.AddRange(scopes.Select(scope => new Claim(ScopeClaim, scope)));

        var token = new JwtSecurityToken(
            issuer: keyMaterial.Issuer,
            audience: keyMaterial.Audience,
            claims: claims,
            notBefore: now,
            expires: now.Add(lifetime),
            signingCredentials: new SigningCredentials(keyMaterial.SigningKey, SecurityAlgorithms.RsaSha256));

        return new ExternalAccessToken(new JwtSecurityTokenHandler().WriteToken(token), (int)lifetime.TotalSeconds);
    }
}
