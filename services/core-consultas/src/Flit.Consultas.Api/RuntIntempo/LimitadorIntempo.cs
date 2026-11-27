using System.Threading.RateLimiting;

namespace Flit.Consultas.Api.RuntIntempo;

/// <summary>
/// Tope de peticiones hacia Intempo (HU #14752, D5 = a): como mucho <see cref="IntempoOpciones.MaxConcurrentes"/> a la vez y
/// <see cref="IntempoOpciones.MaxPorSegundo"/> por segundo (por defecto 4 y 4, <c>INTEMPO_API_MAX_CONCURRENT</c> e
/// <c>INTEMPO_API_MAX_REQUESTS_PER_SECOND</c>). Primero se espera turno de concurrencia y después el token: así no se gasta un
/// token esperando turno. Lo que no cabe se encola en orden de llegada (hasta 10 000); un permiso que no se obtiene (cola
/// llena) es una <see cref="HttpRequestException"/>, que el proveedor trata como <c>RED</c> (transitorio).
/// <para>
/// El tope es POR INSTANCIA de core-consultas (singleton) y cubre a la vez el bus del diagnóstico y el gRPC directo: con
/// varias réplicas, el tope total es el de cada una por el número de réplicas.
/// </para>
/// </summary>
/// <example>
/// <code>
/// await using var permiso = await limitador.AdquirirAsync(ct);
/// using var respuesta = await cliente.SendAsync(pedido, ct);
/// </code>
/// </example>
internal sealed class LimitadorIntempo : IAsyncDisposable
{
    private const int LimiteCola = 10_000;

    private readonly ConcurrencyLimiter _concurrencia;
    private readonly TokenBucketRateLimiter _tasa;

    /// <param name="o">Opciones con los dos topes ya validados.</param>
    /// <param name="autoReponer">Falso solo en las pruebas: los tokens vuelven con <see cref="Reponer"/>.</param>
    internal LimitadorIntempo(IntempoOpciones o, bool autoReponer = true)
    {
        ArgumentNullException.ThrowIfNull(o);
        _concurrencia = new ConcurrencyLimiter(new ConcurrencyLimiterOptions
        {
            PermitLimit = o.MaxConcurrentes,
            QueueLimit = LimiteCola,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        });
        _tasa = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = o.MaxPorSegundo,
            TokensPerPeriod = o.MaxPorSegundo,
            ReplenishmentPeriod = TimeSpan.FromSeconds(1),
            QueueLimit = LimiteCola,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = autoReponer,
        });
    }

    /// <summary>
    /// Espera turno y token. El permiso devuelto libera el turno de concurrencia al desecharse (el token ya se gastó). La
    /// cancelación del llamador se propaga sin quedarse con el turno.
    /// </summary>
    public async ValueTask<IAsyncDisposable> AdquirirAsync(CancellationToken ct)
    {
        var turno = await _concurrencia.AcquireAsync(1, ct).ConfigureAwait(false);
        if (!turno.IsAcquired)
        {
            turno.Dispose();
            throw new HttpRequestException("Tope de peticiones hacia Intempo: la cola de espera está llena.");
        }

        try
        {
            using var token = await _tasa.AcquireAsync(1, ct).ConfigureAwait(false);
            if (!token.IsAcquired)
                throw new HttpRequestException("Tope de peticiones hacia Intempo: la cola de espera está llena.");
        }
        catch
        {
            turno.Dispose();
            throw;
        }

        return new Permiso(turno);
    }

    /// <summary>Repone los tokens del periodo (solo con <c>autoReponer = false</c>, en las pruebas).</summary>
    internal void Reponer() => _tasa.TryReplenish();

    public async ValueTask DisposeAsync()
    {
        await _concurrencia.DisposeAsync().ConfigureAwait(false);
        await _tasa.DisposeAsync().ConfigureAwait(false);
    }

    private sealed class Permiso(RateLimitLease turno) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            turno.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
