using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

namespace Flit.Diagnostico.Api.Modulos.Derivacion;

/// <summary>
/// Freno hacia Trámites, uno por réplica (Punto 4, incremento 3, diseño §5.3 punto 6, ADR-0084; en memoria de proceso, no distribuido):
/// <list type="bullet">
/// <item>Tasa: <c>TokenBucketRateLimiter</c> con ráfaga = <see cref="OpcionesDerivacion.Concurrencia"/> y una ficha cada
/// 1 s / <see cref="OpcionesDerivacion.TasaPorSegundo"/>; el consumidor espera su ficha antes de cada llamada.</item>
/// <item>Pausa: tras <see cref="OpcionesDerivacion.PausaTrasFallos"/> transitorios SEGUIDOS (RESOURCE_EXHAUSTED, UNAVAILABLE,
/// DEADLINE_EXCEEDED, INTERNAL) abre una pausa de <see cref="OpcionesDerivacion.PausaSegundos"/>: el consumidor espera antes de llamar,
/// el reconciliador no actúa y el individual responde 503 con <c>Retry-After</c>. Un éxito (creado o rechazo de negocio: Trámites
/// respondió) la cierra y reinicia la cuenta.</item>
/// </list>
/// Seguro entre hilos. Los logs llevan conteos y segundos, nunca datos de los vehículos.
/// </summary>
public sealed partial class FrenoTramites : IDisposable
{
    private readonly object _candado = new();
    private readonly TokenBucketRateLimiter _tasa;
    private readonly TimeProvider _reloj;
    private readonly int _umbral;
    private readonly TimeSpan _pausa;
    private readonly ILogger? _log;
    private int _fallosSeguidos;
    private DateTimeOffset? _pausaHasta;

    public FrenoTramites(IOptions<OpcionesDerivacion> opciones, TimeProvider reloj, ILogger<FrenoTramites> log)
        : this((opciones ?? throw new ArgumentNullException(nameof(opciones))).Value, reloj, autoReponer: true, log)
    {
    }

    /// <summary>Con <paramref name="autoReponer"/> = false las fichas solo se reponen con <see cref="Reponer"/> (pruebas).</summary>
    internal FrenoTramites(OpcionesDerivacion opciones, TimeProvider reloj, bool autoReponer, ILogger? log = null)
    {
        ArgumentNullException.ThrowIfNull(opciones);
        ArgumentNullException.ThrowIfNull(reloj);
        _reloj = reloj;
        _log = log;
        _umbral = Math.Max(1, opciones.PausaTrasFallos);
        _pausa = TimeSpan.FromSeconds(Math.Max(1, opciones.PausaSegundos));
        // Una ficha cada 1 s / tasa, pero nunca en periodos menores que PeriodoMinimo: el temporizador del limitador trunca a
        // milisegundos (un periodo menor que 1 ms no se repetiría). Con tasas altas entran varias fichas por periodo.
        var tasa = Math.Max(1, opciones.TasaPorSegundo);
        PeriodoReposicion = TimeSpan.FromTicks(Math.Max(PeriodoMinimo.Ticks, TimeSpan.TicksPerSecond / tasa));
        FichasPorPeriodo = Math.Max(1, (int)Math.Round(tasa * PeriodoReposicion.TotalSeconds));
        _tasa = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = Math.Max(1, (int)opciones.Concurrencia),
            TokensPerPeriod = FichasPorPeriodo,
            ReplenishmentPeriod = PeriodoReposicion,
            AutoReplenishment = autoReponer,
            QueueLimit = 10_000,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        });
    }

    /// <summary>Periodo de reposición más corto (granularidad del temporizador del limitador).</summary>
    public static readonly TimeSpan PeriodoMinimo = TimeSpan.FromMilliseconds(10);

    /// <summary>Cada cuánto entran fichas (1 s / TasaPorSegundo, con un mínimo de <see cref="PeriodoMinimo"/>).</summary>
    public TimeSpan PeriodoReposicion { get; }

    /// <summary>Fichas que entran en cada periodo (1 salvo con tasas de más de 100 por segundo).</summary>
    public int FichasPorPeriodo { get; }

    /// <summary>Si la pausa está abierta ahora.</summary>
    public bool EnPausa => PausaRestante() is not null;

    /// <summary>Lo que falta de la pausa; null si no hay pausa abierta.</summary>
    public TimeSpan? PausaRestante()
    {
        lock (_candado)
        {
            if (_pausaHasta is not { } hasta)
                return null;
            var restante = hasta - _reloj.GetUtcNow();
            if (restante > TimeSpan.Zero)
                return restante;
            _pausaHasta = null;
            return null;
        }
    }

    /// <summary>Segundos para <c>Retry-After</c> del individual (redondeo hacia arriba); null sin pausa.</summary>
    public int? RetryAfterSegundos() => PausaRestante() is { } restante ? (int)Math.Ceiling(restante.TotalSeconds) : null;

    /// <summary>Espera a que cierre la pausa (si la hay) y a una ficha de la tasa. Lo llama el consumidor antes de cada rpc.</summary>
    public async ValueTask EsperarTurnoAsync(CancellationToken ct)
    {
        while (PausaRestante() is { } restante)
            await Task.Delay(restante, _reloj, ct).ConfigureAwait(false);
        using var ficha = await _tasa.AcquireAsync(1, ct).ConfigureAwait(false);
        if (!ficha.IsAcquired)
            throw new InvalidOperationException("El freno hacia Trámites rechazó la ficha (cola del limitador llena).");
    }

    /// <summary>Trámites respondió (creado o rechazo de negocio): cierra la pausa y reinicia la cuenta.</summary>
    public void RegistrarExito()
    {
        bool cerro;
        lock (_candado)
        {
            cerro = _pausaHasta is not null;
            _fallosSeguidos = 0;
            _pausaHasta = null;
        }

        if (cerro && _log is not null)
            PausaCerrada(_log);
    }

    /// <summary>Un transitorio más; al llegar a <see cref="OpcionesDerivacion.PausaTrasFallos"/> seguidos abre la pausa.</summary>
    public void RegistrarFalloTransitorio()
    {
        bool abrio = false;
        lock (_candado)
        {
            if (++_fallosSeguidos >= _umbral)
            {
                _fallosSeguidos = 0;
                _pausaHasta = _reloj.GetUtcNow() + _pausa;
                abrio = true;
            }
        }

        if (abrio && _log is not null)
            PausaAbierta(_log, _umbral, (int)_pausa.TotalSeconds);
    }

    /// <summary>Repone una ficha a mano (solo sin reposición automática).</summary>
    internal bool Reponer() => _tasa.TryReplenish();

    public void Dispose() => _tasa.Dispose();

    [LoggerMessage(EventId = 84310, Level = LogLevel.Warning, Message = "Freno hacia Trámites: {Fallos} fallos transitorios seguidos; pausa de {Segundos} s antes de volver a llamar.")]
    private static partial void PausaAbierta(ILogger logger, int fallos, int segundos);

    [LoggerMessage(EventId = 84311, Level = LogLevel.Information, Message = "Freno hacia Trámites: Trámites volvió a responder; pausa cerrada.")]
    private static partial void PausaCerrada(ILogger logger);
}
