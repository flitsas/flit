using Flit.Tramites.Application.UseCases.Avaluos;

namespace Flit.Infrastructure.Consultations.Avaluos;

/// <summary>Registro de proveedores de avalúo indexado por Key (case-insensitive).</summary>
internal sealed class AvaluoProviderRegistry : IAvaluoProviderRegistry
{
    private readonly IReadOnlyList<IAvaluoProvider> _all;
    private readonly Dictionary<string, IAvaluoProvider> _byKey;

    public AvaluoProviderRegistry(IEnumerable<IAvaluoProvider> providers)
    {
        _all = providers.ToList();
        _byKey = new Dictionary<string, IAvaluoProvider>(StringComparer.OrdinalIgnoreCase);
        foreach (var provider in _all)
        {
            _byKey[provider.Key] = provider;
        }
    }

    public IReadOnlyList<IAvaluoProvider> All() => _all;

    public IAvaluoProvider? Resolve(string key) =>
        !string.IsNullOrWhiteSpace(key) && _byKey.TryGetValue(key, out var provider) ? provider : null;
}
