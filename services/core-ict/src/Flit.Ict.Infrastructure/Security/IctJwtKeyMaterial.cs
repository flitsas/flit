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

    /// <summary>
    /// Ruta donde se PERSISTE la llave RSA de DEV cuando no hay <see cref="PrivateKeyPem"/> ni
    /// <see cref="PrivateKeyPath"/> configuradas, para que sobreviva a los reinicios (una llave efímera
    /// regenerada por proceso invalidaba los tokens vigentes en cada reinicio: 401 aunque el <c>exp</c>
    /// siguiera vigente). Vacío ⇒ se deriva un default en el directorio temporal del SO. Solo dev; en
    /// prod se debe configurar <see cref="PrivateKeyPem"/>/<see cref="PrivateKeyPath"/> (llave montada).
    /// </summary>
    public string DevKeyCachePath { get; init; } = string.Empty;

    /// <summary>Vida corta por ser tráfico server-to-server.</summary>
    public int TokenLifetimeHours { get; init; } = 2;
}

/// <summary>
/// Material criptográfico del JWT de ICT. Carga la llave RSA desde PEM (config o archivo); si no hay
/// llave configurada (dev), genera una y la PERSISTE en disco para reusarla entre reinicios — antes se
/// generaba una efímera por proceso, así que cada reinicio de core-ict (hot-reload de <c>dotnet watch</c>,
/// redeploy) rotaba la llave e invalidaba TODOS los tokens vigentes (401 aunque su <c>exp</c> siguiera
/// vivo). Como el emisor y el validador viven en el mismo proceso (Flit.Ict.Api), compartir esta
/// instancia por DI basta para firmar y validar.
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
            // Llave configurada (prod): PrivateKeyPem o PrivateKeyPath montado.
            _rsa.ImportFromPem(pem);
            IsEphemeral = false;
        }
        else
        {
            // Sin llave configurada (dev): se genera una y se PERSISTE, de modo que sobreviva a los
            // reinicios y los tokens emitidos sigan validando hasta su exp real. NO es una llave de prod.
            var cachePath = ResolveDevKeyPath(settings);
            var devPem = TryReadDevKey(cachePath);
            if (devPem is not null)
            {
                _rsa.ImportFromPem(devPem);
            }
            else
            {
                _rsa.KeySize = 2048;
                TryWriteDevKey(cachePath, _rsa.ExportPkcs8PrivateKeyPem());
            }

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

    /// <summary>Ruta del archivo de la llave de dev persistida (default en el temp del SO).</summary>
    private static string ResolveDevKeyPath(IctJwtSettings settings) =>
        string.IsNullOrWhiteSpace(settings.DevKeyCachePath)
            ? Path.Combine(Path.GetTempPath(), "flit-ict-dev-signing.pem")
            : settings.DevKeyCachePath;

    /// <summary>Lee el PEM de dev persistido; <c>null</c> si no existe o no se puede leer (se regenera).</summary>
    private static string? TryReadDevKey(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var pem = File.ReadAllText(path);
                return string.IsNullOrWhiteSpace(pem) ? null : pem;
            }
        }
        catch (IOException) { /* se regenera */ }
        catch (UnauthorizedAccessException) { /* se regenera */ }

        return null;
    }

    /// <summary>Persiste el PEM de dev (best-effort). Si no se puede escribir, la llave queda solo en
    /// memoria (comportamiento efímero previo) — no rompe el arranque.</summary>
    private static void TryWriteDevKey(string path, string pem)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(path, pem);
        }
        catch (IOException) { /* best-effort */ }
        catch (UnauthorizedAccessException) { /* best-effort */ }
    }

    public void Dispose() => _rsa.Dispose();
}
