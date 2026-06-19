namespace Flit.Modules.Security.Domain.Auth;

/// <summary>Genera una contraseña temporal aleatoria que cumple la política de complejidad.</summary>
public interface ITemporaryPasswordGenerator
{
    string Generate();
}
