namespace Flit.Tramites.Application.UseCases.Consultations;

/// <summary>
/// Proveedor de consultas externas. Cada implementacion (Verifik, gateway Flit, ...)
/// se identifica por <see cref="Key"/> y produce un <see cref="ConsultationResult"/>
/// normalizado. NUNCA debe lanzar excepciones de transporte al handler: errores de red
/// se mapean a checks con status "unknown"/"fail".
/// </summary>
public interface IConsultationProvider
{
    string Key { get; }

    Task<ConsultationResult> ConsultAsync(ConsultationContext ctx, CancellationToken ct);
}

/// <summary>
/// Registro de proveedores indexado por <see cref="IConsultationProvider.Key"/>.
/// </summary>
public interface IConsultationProviderRegistry
{
    IConsultationProvider? Resolve(string providerKey);
}
