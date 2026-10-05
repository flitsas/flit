using System.Security.Cryptography;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace Flit.Infrastructure.Security;

// Epic #13217 (HU #13232): salió de DevelopmentAuthSeeder.cs para compartirse con core-identity (firma y validación
// del JWT de siempre). Sin cambios de comportamiento.

public sealed class JwtKeyMaterial
{
    public required RsaSecurityKey SigningKey { get; init; }

    public string Issuer { get; init; } = "https://api.flit.co";

    public string Audience { get; init; } = "flit-api";
}

public static class JwtKeyMaterialLoader
{
    /// <param name="persistentKey">
    /// HU #12896 (A-03): fuente de la llave persistente (<c>security.jwt_signing_keys</c>). Se usa si no hay llave
    /// configurada y <see cref="JwtSettings.PersistSigningKey"/> está encendida. Una llave configurada gana siempre.
    /// </param>
    public static JwtKeyMaterial Load(JwtSettings settings, IHostEnvironment environment, Func<RSA>? persistentKey = null)
    {
        var pem = settings.PrivateKeyPem;
        if (string.IsNullOrWhiteSpace(pem) && !string.IsNullOrWhiteSpace(settings.PrivateKeyPath)
            && File.Exists(settings.PrivateKeyPath))
            pem = File.ReadAllText(settings.PrivateKeyPath);

        RSA rsa;
        if (string.IsNullOrWhiteSpace(pem) && settings.PersistSigningKey && persistentKey is not null)
        {
            rsa = persistentKey();
        }
        else if (string.IsNullOrWhiteSpace(pem))
        {
            if (!environment.IsDevelopment())
                throw new InvalidOperationException("JWT private key is required outside Development.");

            rsa = RSA.Create(2048);
        }
        else
        {
            rsa = RSA.Create();
            rsa.ImportFromPem(pem);
        }

        return new JwtKeyMaterial
        {
            SigningKey = new RsaSecurityKey(rsa),
            Issuer = settings.Issuer,
            Audience = settings.Audience,
        };
    }
}
