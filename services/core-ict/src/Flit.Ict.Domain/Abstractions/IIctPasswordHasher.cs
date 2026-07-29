namespace Flit.Ict.Domain.Abstractions;

/// <summary>Hashing/verificación de contraseñas de clientes de integración (Argon2id).</summary>
public interface IIctPasswordHasher
{
    string Hash(string password);

    /// <summary>Verificación en tiempo constante. Debe hacer trabajo dummy si el hash es nulo/vacío.</summary>
    bool Verify(string password, string? passwordHash);
}
