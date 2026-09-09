using Flit.Admin.Application.GeneracionDocumental.Batches;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Flit.Infrastructure.Messaging;

/// <summary>
/// Worker de lotes XLSX de generación documental (Feature #12201, I3, CF-11/CF-12/CF-13).
///
/// <para>Su única responsabilidad es <b>el ritmo</b>: reclamar, ejecutar y dormir. El recorrido del
/// lote vive en <see cref="StandaloneDocumentBatchRunner"/>, en Application, para que sea
/// comprobable sin hosting, sin base de datos y sin OpenXml.</para>
///
/// <para><b>Reaper (R5).</b> Un lote cuyo <c>claimed_at</c> superó <see cref="ClaimTimeout"/> vuelve
/// a ser reclamable —el proceso pudo caerse a mitad—. Reprocesarlo es seguro: el índice único
/// <c>uq_standalone_documents_batch_row</c> impide una segunda fila con el mismo (lote, número de
/// fila), y el runner además salta las filas ya materializadas.</para>
///
/// <para>Un lote se procesa por vez, en su propio scope de DI: un <c>BackgroundService</c> es
/// singleton y no puede sostener un <c>DbContext</c> scoped entre ciclos.</para>
/// </summary>
internal sealed class StandaloneDocumentBatchProcessor : BackgroundService
{
    /// <summary>Espera entre sondeos cuando no hay nada en cola.</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Vencimiento del claim. 100 filas con consulta a proveedor y render de PDF caben de sobra en
    /// 15 minutos; por debajo, dos workers podrían pisarse en un lote sano.
    /// </summary>
    internal static readonly TimeSpan ClaimTimeout = TimeSpan.FromMinutes(15);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<StandaloneDocumentBatchProcessor> _logger;

    public StandaloneDocumentBatchProcessor(
        IServiceScopeFactory scopeFactory,
        ILogger<StandaloneDocumentBatchProcessor> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var huboTrabajo = false;

            try
            {
                huboTrabajo = await RunOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
#pragma warning disable CA1031 // El worker no puede morir por un lote: se registra y sigue.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                // Sin datos del lote en el mensaje: el nombre del archivo puede llevar razón social.
                StandaloneDocumentBatchLog.CycleError(_logger, ex);
            }

            // Con trabajo pendiente se encadena el siguiente lote sin esperar.
            if (huboTrabajo)
            {
                continue;
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>Un ciclo: reclama y procesa un lote. <c>false</c> si no había nada que hacer.</summary>
    internal async Task<bool> RunOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var runner = scope.ServiceProvider.GetRequiredService<StandaloneDocumentBatchRunner>();

        return await runner.RunNextAsync(ClaimTimeout, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Mensajes del worker por generador de código (CA1848). <b>Nunca</b> llevan nombre de archivo,
/// tenant ni datos de la fila: el nombre del XLSX puede traer la razón social del cliente.
/// </summary>
internal static partial class StandaloneDocumentBatchLog
{
    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Lotes de generación documental: falló un ciclo del worker; se reintentará.")]
    public static partial void CycleError(ILogger logger, Exception ex);
}
