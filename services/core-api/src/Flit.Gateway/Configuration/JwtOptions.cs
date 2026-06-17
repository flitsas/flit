using Microsoft.IdentityModel.Tokens;

namespace Flit.Gateway.Configuration;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; init; } = string.Empty;
    public string Audience { get; init; } = string.Empty;
    public string PublicKeyPem { get; init; } = string.Empty;
    public string PublicKeyPath { get; init; } = string.Empty;

    /// <summary>
    /// Solo Development: permite arrancar sin llave pública (no valida firma JWT).
    /// Ejecuta ./scripts/gen-secrets.sh para validación real en local.
    /// </summary>
    public bool DevRelaxedValidation { get; init; }

    public bool TryGetSigningKey(IHostEnvironment env, out SecurityKey? key)
    {
        var pem = ResolvePublicKeyPem(env);
        if (string.IsNullOrWhiteSpace(pem))
        {
            key = null;
            return false;
        }

        var rsa = System.Security.Cryptography.RSA.Create();
        rsa.ImportFromPem(pem);
        key = new RsaSecurityKey(rsa);
        return true;
    }

    private string ResolvePublicKeyPem(IHostEnvironment env)
    {
        if (!string.IsNullOrWhiteSpace(PublicKeyPem))
            return PublicKeyPem;

        if (!string.IsNullOrWhiteSpace(PublicKeyPath))
        {
            var path = Path.IsPathRooted(PublicKeyPath)
                ? PublicKeyPath
                : Path.Combine(env.ContentRootPath, PublicKeyPath);
            if (File.Exists(path))
                return File.ReadAllText(path);
        }

        var repoSecrets = Path.GetFullPath(
            Path.Combine(env.ContentRootPath, "../../../../infra/secrets/jwt-public.pem"));
        if (File.Exists(repoSecrets))
            return File.ReadAllText(repoSecrets);

        return string.Empty;
    }
}
