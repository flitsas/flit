using System.Threading.Channels;
using Flit.Impuestos.Api.Modulos.Resultados;
using Flit.Impuestos.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Flit.Impuestos.Api.Modulos.Avisos;

/// <summary>Un aviso ya verificado y deduplicado, por aplicar. Lleva la consulta en memoria (puede traer PII): no se registra.</summary>
public sealed record AvisoPendiente(Guid Empresa, Guid ItemId, string DeliveryId, ResultadoLeido Resultado)
{
    public override string ToString() => $"AvisoPendiente {{ Empresa = {Empresa}, ItemId = {ItemId}, DeliveryId = {DeliveryId} }}";
}

/// <summary>
/// Cola en memoria entre el receptor y <see cref="AplicadorResultado"/> (plan §2.2: responder 200 de inmediato y aplicar en
/// segundo plano). Acotada a <see cref="Capacidad"/>: llena, el receptor responde 200 igual y el resultado lo trae la
/// conciliación por GET (por eso perder la cola en un reinicio no pierde resultados). Un solo lector, un scope por aviso; deja en
/// <c>avisos_recibidos.resultado</c> lo que pasó (<c>APLICADO</c>, <c>ATTEMPT_MENOR</c>, …, o <c>ERROR</c>).
/// </summary>
/// <example>
/// <code>
/// if (!cola.TryEncolar(new AvisoPendiente(empresa, itemId, deliveryId, consulta))) { /* queda para la conciliación */ }
/// </code>
/// </example>
public sealed partial class ColaAvisos(IServiceScopeFactory scopes, ILogger<ColaAvisos> log) : BackgroundService
{
    public const int Capacidad = 1000;
    public const string ResultadoError = "ERROR";

    private readonly Channel<AvisoPendiente> _canal = Channel.CreateBounded<AvisoPendiente>(
        new BoundedChannelOptions(Capacidad) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });

    public bool TryEncolar(AvisoPendiente aviso) => _canal.Writer.TryWrite(aviso);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var aviso in _canal.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
                await ProcesarAsync(aviso, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Apagado: lo que quedó en la cola lo trae la conciliación.
        }
    }

    internal async Task ProcesarAsync(AvisoPendiente aviso, CancellationToken ct)
    {
        string resultado;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var aplicado = await scope.ServiceProvider.GetRequiredService<AplicadorResultado>()
                .AplicarAsync(aviso.Empresa, aviso.ItemId, aviso.Resultado, ct).ConfigureAwait(false);
            resultado = CodigosDeEnum.Codigo(aplicado);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            Fallo(log, aviso.ItemId, ex.GetType().Name);
            resultado = ResultadoError;
        }

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ImpuestosDb>().Database.ExecuteSqlAsync(
                $"UPDATE impuestos.avisos_recibidos SET resultado = {resultado} WHERE delivery_id = {aviso.DeliveryId}", ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            Fallo(log, aviso.ItemId, ex.GetType().Name);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "No se pudo aplicar el aviso del ítem {Item} ({Error}); el resultado queda para la conciliación por GET.")]
    private static partial void Fallo(ILogger logger, Guid item, string error);
}
