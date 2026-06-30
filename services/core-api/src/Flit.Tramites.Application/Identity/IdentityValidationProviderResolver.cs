namespace Flit.Tramites.Application.Identity;

/// <summary>
/// Resuelve el proveedor de validación de identidad por nombre entre los registrados en DI. Provider-
/// agnostic: añadir un proveedor = registrar su <see cref="IIdentityValidationProvider"/>; aquí no se toca.
/// </summary>
public sealed class IdentityValidationProviderResolver(IEnumerable<IIdentityValidationProvider> providers)
    : IIdentityValidationProviderResolver
{
    public IIdentityValidationProvider? Resolve(string providerName) =>
        providers.FirstOrDefault(p =>
            string.Equals(p.Name, providerName, StringComparison.OrdinalIgnoreCase));
}
