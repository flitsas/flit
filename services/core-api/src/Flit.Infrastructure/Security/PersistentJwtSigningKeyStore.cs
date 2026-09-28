using System.Security.Cryptography;
using Flit.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Flit.Infrastructure.Security;

/// <summary>
/// Llave RSA guardada en <c>security.jwt_signing_keys</c>, con la llave privada cifrada por
/// Data Protection (HU #12896, A-03). La primera instancia que la necesita la crea; las siguientes, y los reinicios,
/// leen la misma. La usan el JWT de core-api y las llaves de firma y cifrado del servidor OIDC (HU #12990, A-05),
/// cada una con su key_id. Dos instancias que arrancan a la vez no pisan la llave: el INSERT usa ON CONFLICT DO NOTHING y
/// ambas releen la fila guardada.
/// </summary>
public static class PersistentJwtSigningKeyStore
{
    private const string Purpose = "Flit.Jwt.SigningKey.v1";

    public static RSA LoadOrCreate(IServiceProvider services, string keyId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlitDbContext>();
        var protector = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>().CreateProtector(Purpose);

        var stored = Read(db, keyId);
        if (stored is null)
        {
            using var fresh = RSA.Create(2048);
            var protectedPem = protector.Protect(fresh.ExportPkcs8PrivateKeyPem());
            db.Database.ExecuteSql(
                $"INSERT INTO security.jwt_signing_keys (key_id, protected_private_key) VALUES ({keyId}, {protectedPem}) ON CONFLICT (key_id) DO NOTHING");
            stored = Read(db, keyId) ?? throw new InvalidOperationException("No se pudo guardar la llave de firma del JWT.");
        }

        var rsa = RSA.Create();
        rsa.ImportFromPem(protector.Unprotect(stored));
        return rsa;
    }

    private static string? Read(FlitDbContext db, string keyId) =>
        db.Database
            .SqlQuery<string>($"SELECT protected_private_key AS \"Value\" FROM security.jwt_signing_keys WHERE key_id = {keyId}")
            .AsEnumerable()
            .FirstOrDefault();
}
