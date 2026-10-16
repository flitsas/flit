using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Flit.Infrastructure.FlitoSignals;

/// <summary>
/// HU #13625 — al arrancar deja en el log cómo quedaron los avisos a FLITO: apagados, encendidos (con el host, sin la
/// clave) o encendidos pero mal configurados. Nunca impide el arranque: una configuración incompleta solo deja el
/// despachador sin encolar.
/// </summary>
internal sealed partial class FlitoSignalsStartupCheck(
    IOptions<FlitoSignalsOptions> options,
    ILogger<FlitoSignalsStartupCheck> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var o = options.Value;
        if (o.EsOperable(out var motivo))
        {
            var host = new Uri(o.TargetUrl!).Host;
            LogEncendido(logger, host);
        }
        else if (motivo is not null)
        {
            LogMalConfigurado(logger, motivo);
        }
        else
        {
            LogApagado(logger);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(EventId = 13625, Level = LogLevel.Information, Message = "Avisos a FLITO apagados (Integrations:FlitoSignals:Enabled = false).")]
    private static partial void LogApagado(ILogger logger);

    [LoggerMessage(EventId = 13626, Level = LogLevel.Information, Message = "Avisos a FLITO encendidos hacia {Host}.")]
    private static partial void LogEncendido(ILogger logger, string host);

    [LoggerMessage(EventId = 13627, Level = LogLevel.Error, Message = "Avisos a FLITO encendidos pero mal configurados; no se enviará ninguno: {Motivo}")]
    private static partial void LogMalConfigurado(ILogger logger, string motivo);
}
