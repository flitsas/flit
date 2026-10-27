using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Consultas.Bus;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Consultas;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Flit.Diagnostico.Api.Modulos.Trabajos;

/// <summary>Lo que hizo una vuelta del reconciliador.</summary>
/// <param name="Revisadas">Celdas huérfanas que encontró (sin trabajo, perdidas según la marca de agua o vencidas).</param>
/// <param name="Publicadas">Celdas sin trabajo (de antes del corte) que publicó por primera vez.</param>
/// <param name="Republicadas">Celdas que republicó con un <c>trabajo_id</c> nuevo (<c>republicaciones</c> = 1).</param>
/// <param name="Cerradas">Celdas que cerró en ERROR tras su republicación.</param>
/// <param name="EnPausa">Huérfanas que no tocó porque core-consultas no devolvió resultados en la ventana.</param>
/// <param name="Vencidas">Celdas que cerró en ERROR porque su trabajo venció en el broker (TTL + margen).</param>
public sealed record VueltaReconciliador(int Revisadas, int Publicadas, int Republicadas, int Cerradas, int EnPausa, int Vencidas);

/// <summary>
/// Reconciliador de las consultas por RabbitMQ (ADR-0075, D3, 6r-i, U4 = a, Dec. 6; HU #13727, T11): la red de seguridad para los
/// trabajos que se pierden de verdad (DLQ, mensajes descartados). Corre cada <see cref="OpcionesReconciliador.Intervalo"/> (60 s)
/// y, con las huérfanas de TODAS las empresas (<see cref="ColaConsultas.BuscarHuerfanasAsync"/>, solo ids), trabaja cada una en su
/// propio scope con la empresa de su celda fijada (<see cref="EmpresaDelTrabajo"/>) y en su propia transacción:
/// <list type="bullet">
/// <item>Sin <c>trabajo_id</c> (PENDIENTE no diferida de antes del corte): la publica por primera vez
/// (<see cref="PublicadorCeldas"/>); no duplica nada, así que no espera a la señal de vida.</item>
/// <item>Con trabajo publicado hace más de <see cref="OpcionesReconciliador.TtlTrabajo"/> (24 h) +
/// <see cref="OpcionesReconciliador.Margen"/>: su mensaje ya venció en el broker (salió a la DLQ) y no puede duplicarse; la cierra en
/// ERROR reprocesable con <see cref="MensajeVencido"/> aunque core-consultas esté caído.</item>
/// <item>Con trabajo y sin resultado tras el margen (30 min) solo está <b>perdida</b> si core-consultas ya devolvió resultados de
/// trabajos publicados en su misma cola (lote o Express) más de <see cref="OpcionesReconciliador.ToleranciaOrden"/> (15 min)
/// después que el suyo (marca de agua FIFO de <see cref="PulsoConsultas"/>). Si no, espera su turno en la cola: un lote de 30.000
/// trabajos con concurrencia 8 espera horas sin que se duplique ni se cierre nada. Una réplica sin marca no republica.</item>
/// <item>Perdida y con core-consultas devolviendo algún resultado en la última <see cref="OpcionesReconciliador.VentanaVida"/>: la
/// primera vez la republica con un <c>trabajo_id</c> nuevo (<c>republicaciones</c> = 1); la segunda la cierra en ERROR,
/// reprocesable, con el tipo del último intento (o sin tipo, como la falla inesperada), su intento y, si es la RUNT, el bloqueo de
/// las demás celdas. Sin resultados en la ventana, consultas está caído: no republica ni cierra (acta §3.2).</item>
/// </list>
/// Solo se registra con el consumidor de resultados. Una vuelta que falla (base caída) se reintenta en la siguiente. Los logs
/// llevan ids y conteos; nunca datos de las consultas.
/// </summary>
/// <example>
/// <code>
/// var vuelta = await reconciliador.ReconciliarAsync(ct); // VueltaReconciliador(Revisadas, Publicadas, Republicadas, Cerradas, EnPausa, Vencidas)
/// </code>
/// </example>
internal sealed partial class ReconciliadorConsultas(
    IServiceScopeFactory scopes, PulsoConsultas pulso, IOptions<OpcionesReconciliador> opciones, TimeProvider reloj,
    ILogger<ReconciliadorConsultas> log) : BackgroundService
{
    /// <summary>Mensaje del intento de la celda que se cierra en ERROR sin resultado.</summary>
    public const string MensajeSinResultado = "core-consultas no devolvió resultado tras republicar el trabajo (ADR-0075).";

    /// <summary>Mensaje del intento de la celda que se cierra en ERROR porque su trabajo venció en la cola (sin datos de la consulta).</summary>
    public const string MensajeVencido = "El trabajo venció en la cola sin que core-consultas lo atendiera (TTL del broker); se puede reprocesar (ADR-0075).";

    private enum Accion
    {
        Ninguna,
        Publicada,
        Republicada,
        Cerrada,
        EnPausa,
        Vencida,
    }

    /// <summary>Una vuelta: busca las huérfanas de todas las empresas y reconcilia cada una con la empresa de su celda.</summary>
    public async Task<VueltaReconciliador> ReconciliarAsync(CancellationToken ct)
    {
        var o = opciones.Value;
        var ahora = reloj.GetUtcNow();
        var umbrales = new UmbralesHuerfanas(
            ahora - o.Margen, ahora - o.TtlTrabajo - o.Margen,
            pulso.UltimoAtendido(esExpress: false) - o.ToleranciaOrden, pulso.UltimoAtendido(esExpress: true) - o.ToleranciaOrden);
        IReadOnlyList<CeldaHuerfana> huerfanas;
        await using (var scope = scopes.CreateAsyncScope())
            huerfanas = await scope.ServiceProvider.GetRequiredService<ColaConsultas>().BuscarHuerfanasAsync(umbrales, ahora, o.Maximo, ct).ConfigureAwait(false);

        var vivo = pulso.Vivo(ahora, o.VentanaVida);
        var acciones = new List<Accion>(huerfanas.Count);
        foreach (var huerfana in huerfanas)
            acciones.Add(await ReconciliarAsync(huerfana, ahora, umbrales, vivo, ct).ConfigureAwait(false));

        var vuelta = new VueltaReconciliador(
            huerfanas.Count, acciones.Count(a => a == Accion.Publicada), acciones.Count(a => a == Accion.Republicada),
            acciones.Count(a => a == Accion.Cerrada), acciones.Count(a => a == Accion.EnPausa), acciones.Count(a => a == Accion.Vencida));
        if (vuelta.Revisadas > 0)
            Vuelta(log, vuelta.Revisadas, vuelta.Publicadas, vuelta.Republicadas, vuelta.Cerradas, vuelta.EnPausa, vuelta.Vencidas);
        return vuelta;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var o = opciones.Value;
        Arranca(log, o.Intervalo, o.Margen, o.VentanaVida, o.ToleranciaOrden, o.TtlTrabajo);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(o.Intervalo, reloj, stoppingToken).ConfigureAwait(false);
                await ReconciliarAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // Una vuelta fallida (base caída) no detiene el reconciliador: se reintenta en la siguiente.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                VueltaFallida(log, ex.GetType().Name);
            }
        }
    }

    private async Task<Accion> ReconciliarAsync(CeldaHuerfana huerfana, DateTimeOffset ahora, UmbralesHuerfanas umbrales, bool vivo, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        // ADR-0072 / ADR-0075: la empresa de la celda queda fijada en el scope; el contexto, la outbox y el publicador la usan.
        scope.ServiceProvider.GetRequiredService<EmpresaDelTrabajo>().Fijar(huerfana.TenantId);
        var db = scope.ServiceProvider.GetRequiredService<DiagnosticoDb>();
        var publicador = scope.ServiceProvider.GetRequiredService<PublicadorCeldas>();
        try
        {
            return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                db.ChangeTracker.Clear();
                await using var transaccion = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
                var celda = await db.Set<CeldaConsulta>().SingleOrDefaultAsync(c => c.Id == huerfana.Id, ct).ConfigureAwait(false);
                var accion = await DecidirAsync(db, publicador, celda, huerfana.EsExpress, ahora, umbrales, vivo, ct).ConfigureAwait(false);
                if (accion is Accion.Ninguna or Accion.EnPausa)
                    return accion;

                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                await transaccion.CommitAsync(ct).ConfigureAwait(false);
                return accion;
            }).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Llegó su resultado (u otra réplica la reconcilió) mientras tanto: ya no es huérfana.
            return Accion.Ninguna;
        }
    }

    /// <summary>
    /// Qué hacer con la celda, ya leída con su empresa (se vuelven a comprobar los umbrales: pudo cambiar desde la búsqueda); deja
    /// los cambios en el contexto (sin guardar).
    /// </summary>
    private async Task<Accion> DecidirAsync(
        DiagnosticoDb db, PublicadorCeldas publicador, CeldaConsulta? celda, bool esExpress, DateTimeOffset ahora, UmbralesHuerfanas umbrales, bool vivo,
        CancellationToken ct)
    {
        if (celda is null || celda.Estado is not (EstadoCelda.Pendiente or EstadoCelda.EnCurso) || celda.DisponibleDesde > ahora)
            return Accion.Ninguna;

        if (celda.TrabajoId is null)
        {
            await publicador.PublicarAsync(celda, ct).ConfigureAwait(false);
            Publicada(log, celda.Id, celda.Fuente);
            return Accion.Publicada;
        }

        if (celda.DisponibleDesde >= umbrales.Limite)
            return Accion.Ninguna;

        // Dec. 6: vencido el TTL del broker (+ margen), el mensaje ya no está en la cola: se cierra aunque consultas esté mudo.
        if (celda.DisponibleDesde < umbrales.Vencido)
        {
            await CerrarEnErrorAsync(db, publicador, celda, ahora, MensajeVencido, ct).ConfigureAwait(false);
            Vencida(log, celda.Id, celda.Fuente);
            return Accion.Vencida;
        }

        // Dec. 6: perdida solo si su cola ya devolvió trabajos publicados después del suyo (más allá de la tolerancia).
        if ((esExpress ? umbrales.PerdidasExpress : umbrales.PerdidasLote) is not { } perdidas || celda.DisponibleDesde >= perdidas)
            return Accion.Ninguna;
        if (!vivo)
        {
            EnPausa(log, celda.Id, celda.Fuente);
            return Accion.EnPausa;
        }

        if (celda.Republicaciones == 0)
        {
            await publicador.PublicarAsync(celda, ct).ConfigureAwait(false);
            if (celda.Estado == EstadoCelda.EnCurso)
                celda.Republicaciones = 1;
            Republicada(log, celda.Id, celda.Fuente, celda.TrabajoId);
            return Accion.Republicada;
        }

        var tipo = await CerrarEnErrorAsync(db, publicador, celda, ahora, MensajeSinResultado, ct).ConfigureAwait(false);
        Cerrada(log, celda.Id, celda.Fuente, tipo ?? "-");
        return Accion.Cerrada;
    }

    /// <summary>
    /// Cierra la celda en ERROR reprocesable con el tipo de su último intento (o sin tipo) y <paramref name="mensaje"/> en el intento;
    /// si es la RUNT, bloquea las demás (<see cref="PublicadorCeldas.CerrarAsync"/>). Devuelve el tipo de error usado.
    /// </summary>
    private static async Task<string?> CerrarEnErrorAsync(
        DiagnosticoDb db, PublicadorCeldas publicador, CeldaConsulta celda, DateTimeOffset ahora, string mensaje, CancellationToken ct)
    {
        var tipo = await db.Set<IntentoConsulta>().AsNoTracking()
            .Where(i => i.ConsultaId == celda.Id)
            .OrderByDescending(i => i.Fin).ThenByDescending(i => i.Id)
            .Select(i => i.TipoError)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        var enCurso = CeldaEnCurso.De(celda);
        // Las celdas de persona conservan lo que sí respondió.
        var resultado = new ResultadoCelda(EstadoCelda.Error, EjecutorPorPersonas.EsPorPersona(celda.Fuente) ? celda.Payload : null, null, null, null, tipo)
        {
            Intento = new DatosIntento(null, null, null, mensaje),
        };
        await publicador.CerrarAsync(celda, resultado, RegistroIntentos.CrearTodos(enCurso, ahora, ahora, resultado), versionTablero: null, ct).ConfigureAwait(false);
        return tipo;
    }

    [LoggerMessage(EventId = 137280, Level = LogLevel.Information,
        Message = "Reconciliador de consultas en marcha: cada {Intervalo}, margen {Margen}, ventana de vida {VentanaVida}, tolerancia de orden {ToleranciaOrden}, TTL {TtlTrabajo}")]
    private static partial void Arranca(ILogger logger, TimeSpan intervalo, TimeSpan margen, TimeSpan ventanaVida, TimeSpan toleranciaOrden, TimeSpan ttlTrabajo);

    [LoggerMessage(EventId = 137281, Level = LogLevel.Information,
        Message = "Reconciliador: {Revisadas} huérfanas; {Publicadas} publicadas, {Republicadas} republicadas, {Cerradas} cerradas en ERROR, {EnPausa} en pausa, {Vencidas} vencidas por TTL")]
    private static partial void Vuelta(ILogger logger, int revisadas, int publicadas, int republicadas, int cerradas, int enPausa, int vencidas);

    [LoggerMessage(EventId = 137282, Level = LogLevel.Information, Message = "Reconciliador: la celda {CeldaId} de {Fuente} se publica por primera vez")]
    private static partial void Publicada(ILogger logger, Guid celdaId, Fuente fuente);

    [LoggerMessage(EventId = 137283, Level = LogLevel.Warning, Message = "Reconciliador: la celda {CeldaId} de {Fuente} quedó sin resultado y se republica (trabajo {TrabajoId})")]
    private static partial void Republicada(ILogger logger, Guid celdaId, Fuente fuente, Guid? trabajoId);

    [LoggerMessage(EventId = 137284, Level = LogLevel.Warning, Message = "Reconciliador: la celda {CeldaId} de {Fuente} siguió sin resultado tras republicarse y cierra en ERROR ({TipoError})")]
    private static partial void Cerrada(ILogger logger, Guid celdaId, Fuente fuente, string tipoError);

    [LoggerMessage(EventId = 137285, Level = LogLevel.Information, Message = "Reconciliador: la celda {CeldaId} de {Fuente} espera; core-consultas no devolvió resultados en la ventana de vida")]
    private static partial void EnPausa(ILogger logger, Guid celdaId, Fuente fuente);

    [LoggerMessage(EventId = 137286, Level = LogLevel.Error, Message = "Falló una vuelta del reconciliador de consultas ({TipoError}); se reintenta en la siguiente")]
    private static partial void VueltaFallida(ILogger logger, string tipoError);

    [LoggerMessage(EventId = 137287, Level = LogLevel.Warning, Message = "Reconciliador: el trabajo de la celda {CeldaId} de {Fuente} venció en el broker (TTL) y la celda cierra en ERROR reprocesable")]
    private static partial void Vencida(ILogger logger, Guid celdaId, Fuente fuente);
}
