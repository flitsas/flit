namespace Flit.Tramites.Application.UseCases.Avaluos;

/// <summary>
/// Proveedor de avalúo comercial (Fasecolda, base gravable, Mercado Libre, ...). Cada
/// implementación se identifica por <see cref="Key"/> y produce un <see cref="AvaluoResult"/>
/// orientado a valor. A diferencia de <c>IConsultationProvider</c> (verificación por checks),
/// esta capa agrega VALOR de varias fuentes en paralelo (ADR-0029). NUNCA lanza excepciones de
/// transporte al handler: los errores se mapean a un resultado con status <c>error</c>/<c>no_data</c>.
/// </summary>
public interface IAvaluoProvider
{
    string Key { get; }

    Task<AvaluoResult> GetAvaluoAsync(AvaluoContext ctx, CancellationToken ct);
}

/// <summary>Registro de proveedores de avalúo indexado por <see cref="IAvaluoProvider.Key"/>.</summary>
public interface IAvaluoProviderRegistry
{
    IReadOnlyList<IAvaluoProvider> All();

    IAvaluoProvider? Resolve(string key);
}
