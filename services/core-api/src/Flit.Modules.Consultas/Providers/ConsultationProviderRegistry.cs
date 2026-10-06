using Flit.Tramites.Application.UseCases.Consultations;

namespace Flit.Infrastructure.Consultations;

/// <summary>
/// Registro de proveedores indexado por Key (case-insensitive). Resuelve el
/// <see cref="IConsultationProvider"/> que corresponde al provider declarado en el
/// template de consulta.
/// </summary>
internal sealed class ConsultationProviderRegistry : IConsultationProviderRegistry
{
    private readonly Dictionary<string, IConsultationProvider> _providers;

    public ConsultationProviderRegistry(IEnumerable<IConsultationProvider> providers)
    {
        _providers = new Dictionary<string, IConsultationProvider>(StringComparer.OrdinalIgnoreCase);
        foreach (var provider in providers)
        {
            _providers[provider.Key] = provider;
        }
    }

    public IConsultationProvider? Resolve(string providerKey) =>
        !string.IsNullOrWhiteSpace(providerKey) && _providers.TryGetValue(providerKey, out var provider)
            ? provider
            : null;
}
