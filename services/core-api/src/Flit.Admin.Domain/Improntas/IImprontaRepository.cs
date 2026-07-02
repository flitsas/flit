namespace Flit.Admin.Domain.Improntas;

/// <summary>
/// Persistencia del historial de improntas generadas (HU #10466 / ADR-0022). El listado
/// paginado/filtrable (placa, radicado, fecha) es responsabilidad de la HU #10468 y se agrega
/// aquí cuando se implemente — este contrato cubre únicamente el alta append-only.
/// </summary>
public interface IImprontaRepository
{
    /// <summary>
    /// Persiste una nueva generación de impronta (metadata + PDF) en una sola operación atómica.
    /// </summary>
    Task SaveAsync(ImprontaGeneration generation, CancellationToken cancellationToken = default);
}
