using Flit.Tramites.Domain.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Flit.Tramites.Application.UseCases.ConsolidadoLotes;

/// <summary>Resultado de un ciclo de <see cref="PurgarLotesExpiradosHandler"/>.</summary>
/// <param name="Vencidos">Lotes vencidos encontrados en el ciclo (como mucho <see cref="PurgarLotesExpiradosHandler.MaxPorCiclo"/>).</param>
/// <param name="Purgados">Purgados en este ciclo.</param>
/// <param name="Fallidos">Lotes cuya purga lanzó; se reintentan en el ciclo siguiente.</param>
public sealed record PurgaLotesResultado(int Vencidos, int Purgados, int Fallidos);

/// <summary>
/// HU #13379 (Épica #13216, ADR-0070 D4, CF-12/CF-22) — purga a las 24 h: por cada lote terminal vencido
/// (<c>expires_at &lt;= ahora</c>, <c>purged_at IS NULL</c>; incluidos los <c>fallido</c>) aplica
/// <see cref="IConsolidadoLoteRepository.PurgarAsync"/>: borrado criptográfico (DEK a <c>NULL</c>), partes
/// <c>purgada</c>, lote <c>expirado</c> con <c>purged_at</c> y <c>lote_purgado</c> en la misma transacción. Cada lote en su
/// propia transacción con lock: un fallo no frena a los demás. La placa de los ítems se conserva (decisión S2 = b).
/// </summary>
/// <remarks>
/// Uso de ejemplo: <c>var r = await handler.HandleAsync(ct); // r.Purgados</c>. Lo invoca el carril de purga
/// (<c>ConsolidadoLotePurgaProcessor</c>) cada 10 min.
/// </remarks>
public sealed partial class PurgarLotesExpiradosHandler(
    IConsolidadoLoteLectura lectura,
    IConsolidadoLoteRepository repositorio,
    ILogger<PurgarLotesExpiradosHandler>? logger = null,
    TimeProvider? reloj = null)
{
    /// <summary>Lotes que se purgan como mucho por ciclo; el carril repite el ciclo mientras salga lleno.</summary>
    public const int MaxPorCiclo = 100;

    private readonly ILogger _logger = logger ?? NullLogger<PurgarLotesExpiradosHandler>.Instance;
    private readonly TimeProvider _reloj = reloj ?? TimeProvider.System;

    public async Task<PurgaLotesResultado> HandleAsync(CancellationToken ct = default)
    {
        var ahora = _reloj.GetUtcNow();
        var vencidos = await lectura.ObtenerVencidosAsync(ahora, MaxPorCiclo, ct).ConfigureAwait(false);
        int purgados = 0, fallidos = 0;
        foreach (var loteId in vencidos)
        {
            try
            {
                // false = ya no era purgable (lo purgó la creación de un lote nuevo del mismo usuario, H10a).
                if (await repositorio.PurgarAsync(loteId, ahora, ct).ConfigureAwait(false))
                    purgados++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                fallidos++;
                LogPurgaFallida(_logger, loteId, ex.GetType().Name);
            }
        }

        return new PurgaLotesResultado(vencidos.Count, purgados, fallidos);
    }

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Lote {LoteId}: la purga a las 24 h lanzó {Tipo}; se reintentará en el ciclo siguiente.")]
    private static partial void LogPurgaFallida(ILogger logger, Guid loteId, string tipo);
}
