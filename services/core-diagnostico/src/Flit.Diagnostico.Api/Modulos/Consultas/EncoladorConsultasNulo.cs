using Flit.Diagnostico.Domain.Consultas;

namespace Flit.Diagnostico.Api.Modulos.Consultas;

/// <summary>
/// Adaptador de <see cref="IEncoladorConsultas"/> de la ola D1 (HU #13751): no hace nada. La confirmación del lote ya lo
/// invoca dentro de su transacción; #13719 lo reemplaza por el que crea las celdas, encola y pasa el lote a EN_PROCESO.
/// </summary>
internal sealed class EncoladorConsultasNulo : IEncoladorConsultas
{
    public Task EncolarAsync(Guid diagnosticoId, IReadOnlyList<VehiculoAEncolar> vehiculos, CancellationToken ct) => Task.CompletedTask;
}
