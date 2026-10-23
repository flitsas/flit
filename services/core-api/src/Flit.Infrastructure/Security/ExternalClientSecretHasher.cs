using Flit.Admin.Domain.Integrations;
using Flit.Modules.Security.Domain.Auth;

namespace Flit.Infrastructure.Security;

/// <summary>
/// HU #13087 — adaptador del puerto de Admin sobre el hasher Argon2id de contraseñas: mismo formato de
/// hash (<c>argon2id|salt|hash</c>) y mismos parámetros.
/// </summary>
internal sealed class ExternalClientSecretHasher(IPasswordHasher passwordHasher) : IExternalClientSecretHasher
{
    public string Hash(string secret) => passwordHasher.Hash(secret);

    public bool Verify(string secret, string storedHash) => passwordHasher.Verify(secret, storedHash);

    public string DummyHash => passwordHasher.DummyHash;
}
