namespace Flit.Admin.Domain.Integrations;

/// <summary>
/// HU #13087 — hash Argon2id de los secretos de los clientes externos. Puerto propio para que el módulo
/// Admin no dependa del de Seguridad; la implementación reutiliza el hasher de contraseñas.
/// </summary>
public interface IExternalClientSecretHasher
{
    string Hash(string secret);

    bool Verify(string secret, string storedHash);

    /// <summary>Hash de relleno para igualar el tiempo de respuesta cuando el cliente no existe o está inactivo.</summary>
    string DummyHash { get; }
}
