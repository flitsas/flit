namespace Flit.Modules.Security.Domain.Auth;

public interface IPasswordHasher
{
    string Hash(string password);

    bool Verify(string password, string storedHash);

    /// <summary>
    /// Hash válido de relleno usado para igualar el costo de verificación cuando el
    /// usuario no existe o no es elegible, evitando enumeración de cuentas por tiempo.
    /// </summary>
    string DummyHash { get; }
}
