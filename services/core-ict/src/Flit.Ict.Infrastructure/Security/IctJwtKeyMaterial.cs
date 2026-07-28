using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace Flit.Ict.Infrastructure.Security;

/// <summary>Configuración del token JWT propio de ICT (aud/issuer/llave distintos de plataforma).</summary>
public sealed class IctJwtSettings
{
    public const string SectionName = "IctJwt";

    public string Issuer { get; init; } = "https://ict.flit.co";

    public string Audience { get; init; } = "flit-ict";

    public string PrivateKeyPem { get; init; } = string.Empty;

    public string PrivateKeyPath { get; init; } = string.Empty;

    /// <summary>Vida corta por ser tráfico server-to-server.</summary>
    public int TokenLifetimeHours { get; init; } = 2;
}

/// <summary>
/// Material criptográfico del JWT de ICT. Carga la llave RSA desde PEM (config o archivo); si no hay
/// llave configurada, genera una efímera (solo dev) — igual que el modo transitorio de core-api.
/// Como el emisor y el validador viven en el mismo proceso (Flit.Ict.Api), compartir esta instancia
/// por DI basta para firmar y validar.
/// </summary>
public sealed class IctJwtKeyMaterial : IDisposable
{
    private readonly RSA _rsa;

    public IctJwtKeyMaterial(IctJwtSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _rsa = RSA.Create();

        var pem = ResolvePem(settings);
        if (!string.IsNullOrWhiteSpace(pem))
        {
            _rsa.ImportFromPem(pem);
            IsEphemeral = false;
        }
        else
        {
            _rsa.KeySize = 2048;
            IsEphemeral = true;
        }

        SigningKey = new RsaSecurityKey(_rsa) { KeyId = "ict-signing" };
        Issuer = settings.Issuer;
        Audience = settings.Audience;
    }

    public RsaSecurityKey SigningKey { get; }

    public string Issuer { get; }

    public string Audience { get; }

    /// <summary>True si la llave es efímera (generada en memoria); no usar en producción.</summary>
    public bool IsEphemeral { get; }

    private static string ResolvePem(IctJwtSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.PrivateKeyPem))
        {
            return settings.PrivateKeyPem;
        }

        return !string.IsNullOrWhiteSpace(settings.PrivateKeyPath) && File.Exists(settings.PrivateKeyPath)
            ? File.ReadAllText(settings.PrivateKeyPath)
            : string.Empty;
    }

    public void Dispose() => _rsa.Dispose();
}
