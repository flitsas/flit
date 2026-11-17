using Flit.Diagnostico.Domain.Vehiculos;

namespace Flit.Diagnostico.Domain.Consultas;

/// <summary>
/// Puerto hacia el motor de consultas (HU #13751, F02-H5, F02-C14): recibe la lista limpia de un lote recién confirmado,
/// dentro de la misma transacción que la persiste (si falla, no queda nada guardado). En D1 el adaptador no hace nada;
/// #13719 lo reemplaza (crea las celdas, encola y pasa el lote a EN_PROCESO). Nunca recibe un identificador inválido ni
/// un documento de persona: solo el id, el identificador normalizado y su clase.
/// </summary>
public interface IEncoladorConsultas
{
    /// <summary>Entrega los vehículos válidos del lote <paramref name="diagnosticoId"/> al motor de consultas.</summary>
    Task EncolarAsync(Guid diagnosticoId, IReadOnlyList<VehiculoAEncolar> vehiculos, CancellationToken ct);
}

/// <summary>Lo que el motor de consultas necesita de un vehículo confirmado; sin documentos de persona.</summary>
/// <param name="VehiculoId">Id de la fila de <c>diagnostico.vehiculos</c>.</param>
/// <param name="IdentificadorNorm">Identificador normalizado (placa o VIN).</param>
/// <param name="Tipo">Clase del identificador.</param>
public sealed record VehiculoAEncolar(Guid VehiculoId, string IdentificadorNorm, TipoIdentificador Tipo);
