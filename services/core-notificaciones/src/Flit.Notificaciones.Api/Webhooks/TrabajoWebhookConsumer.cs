using System.Diagnostics;
using System.Text;
using Flit.Modules.Notificaciones.Webhooks;
using Flit.Notificaciones.Api.Persistence;
using Flit.Platform.Sdk.Messaging;

namespace Flit.Notificaciones.Api.Webhooks;

/// <summary>¿Se puede entregar a esta URL? En producción, <see cref="WebhookTargetGuard"/> (solo destinos públicos).</summary>
internal interface IFiltroDestinosWebhook
{
    Task<bool> PermitidoAsync(string url, CancellationToken ct);
}

internal sealed class FiltroDestinosPublicos : IFiltroDestinosWebhook
{
    public Task<bool> PermitidoAsync(string url, CancellationToken ct) => WebhookTargetGuard.IsPublicHttpTargetAsync(url, ct);
}

/// <summary>
/// HU #13356 — entrega los webhooks salientes (<see cref="TrabajoWebhook.Tipo"/>) tal como los armó y firmó su dueño: el
/// cuerpo exacto y sus cabeceras (firma HMAC-SHA256 y <c>X-Correlation-Id</c>, AC1). Un destino interno (IP privada,
/// loopback, metadata) se bloquea y queda registrado, sin reintentos (AC2). Un destino que no responde o no contesta 2xx
/// se reintenta (10 s, 1 min, 10 min) y termina en <c>notificaciones.webhooks.salientes.dlq</c> (AC3). Cada intento queda
/// en <c>notificaciones.webhooks</c> en su propia transacción.
/// </summary>
internal sealed partial class TrabajoWebhookConsumer(
    IHttpClientFactory http,
    IFiltroDestinosWebhook filtro,
    IServiceScopeFactory scopes,
    TimeProvider time,
    ILogger<TrabajoWebhookConsumer> logger) : IEventConsumer<TrabajoWebhook>
{
    public const string Cola = "notificaciones.webhooks.salientes";
    public const string ClienteHttp = "webhooks-salientes";

    public async Task HandleAsync(EventEnvelope envelope, TrabajoWebhook data, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(data);
        var correlacion = data.Cabeceras.TryGetValue("X-Correlation-Id", out var c) ? c : envelope.CorrelationId;

        if (!await filtro.PermitidoAsync(data.Url, ct).ConfigureAwait(false))
        {
            await RegistrarAsync(envelope, data, "bloqueado", null, "Destino interno o no resoluble (filtro anti-SSRF).", 0, correlacion, ct).ConfigureAwait(false);
            LogBloqueado(logger, envelope.EventId, data.Origen);
            return;
        }

        var reloj = Stopwatch.StartNew();
        int? codigo = null;
        string? motivo = null;
        try
        {
            using var pedido = new HttpRequestMessage(HttpMethod.Post, data.Url)
            {
                Content = new StringContent(data.Cuerpo, Encoding.UTF8, "application/json"),
            };
            foreach (var (nombre, valor) in data.Cabeceras)
                pedido.Headers.TryAddWithoutValidation(nombre, valor);
            using var respuesta = await http.CreateClient(ClienteHttp).SendAsync(pedido, ct).ConfigureAwait(false);
            codigo = (int)respuesta.StatusCode;
            if (!respuesta.IsSuccessStatusCode)
                motivo = $"El destino respondió {codigo}.";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            motivo = ex is TaskCanceledException ? "Tiempo agotado." : $"Sin conexión con el destino ({ex.GetType().Name}).";
        }

        var entregado = codigo is >= 200 and < 300;
        await RegistrarAsync(envelope, data, entregado ? "entregado" : "fallido", codigo, motivo, (int)reloj.ElapsedMilliseconds, correlacion, ct).ConfigureAwait(false);
        if (!entregado)
            throw new WebhookNoEntregadoException(data.Url, codigo);
    }

    private async Task RegistrarAsync(EventEnvelope envelope, TrabajoWebhook data, string resultado, int? codigo, string? motivo, int duracionMs, string? correlacion, CancellationToken ct)
    {
        // Scope propio: el intento queda aunque este mensaje se reintente (la bandeja hace rollback).
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificacionesDb>();
        db.Webhooks.Add(new EntregaWebhook
        {
            Id = Guid.CreateVersion7(),
            TenantId = envelope.TenantId,
            Origen = Recortar(data.Origen, 20),
            Evento = Recortar(data.Evento, 100),
            Url = Recortar(data.Url, 2000),
            Resultado = resultado,
            CodigoHttp = codigo,
            Motivo = motivo is null ? null : Recortar(motivo, 1000),
            DuracionMs = duracionMs,
            CorrelacionId = correlacion is null ? null : Recortar(correlacion, 100),
            TrabajoId = envelope.EventId,
            OcurridoEn = time.GetUtcNow(),
        });
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static string Recortar(string valor, int max) => valor.Length <= max ? valor : valor[..max];

    [LoggerMessage(EventId = 7531, Level = LogLevel.Warning, Message = "Webhook {EventId} de {Origen} bloqueado: el destino no es público")]
    private static partial void LogBloqueado(ILogger logger, Guid eventId, string origen);
}

/// <summary>El destino no respondió 2xx: el consumidor del SDK lo reintenta.</summary>
internal sealed class WebhookNoEntregadoException(string url, int? codigo)
    : Exception(codigo is null ? $"El webhook a {url} no tuvo respuesta; se reintenta." : $"El webhook a {url} respondió {codigo}; se reintenta.");
