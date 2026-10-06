namespace Flit.Infrastructure.Consultations.Avaluos;

/// <summary>
/// Valores de referencia de avalúo por VIN/placa y fuente para el modo mock de los proveedores (DEV/QA). Puerto del
/// módulo (HU #13342): hoy lo implementa Flit.Infrastructure sobre <c>tramites.avaluo_mock_values</c>.
/// </summary>
public interface IAvaluoMockValueSource
{
    Task<long?> GetValueAsync(string matchKey, string source, CancellationToken ct);
}
