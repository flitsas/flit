using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Consultas;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Flit.Diagnostico.Api.Modulos.Consultas.Bus;

/// <summary>
/// Reintentos de las consultas por el bus (ADR-0075, Q4, U2 = a; HU #13737, D-12; HU #14752, D5 = a): los decide Diagnóstico con
/// <see cref="PoliticaReintentos"/> y core-consultas consulta una sola vez. Backoff 1, 2 y 4 min ±20 % (<c>Diagnostico:Consultas</c>:
/// <c>MaxReintentos</c> 0..3, por defecto 3; <c>EsperaReintento</c>, la base, por defecto 1 min; <c>FactorAleatorio</c>, por defecto
/// 0,2; <c>EsperaMinimaLimiteProveedor</c>, por defecto 1 min, el mínimo de un <c>LIMITE_PROVEEDOR</c>). El azar sale de
/// <see cref="AzarReintentos"/>.
/// Un resultado transitorio con reintentos disponibles sigue tres pasos:
/// <list type="number">
/// <item><see cref="ProgramarAsync"/>: en un scope PROPIO (el del mensaje se revierte) la celda vuelve a PENDIENTE con su intento
/// auditable, <c>intentos_auto + 1</c> y <c>disponible_desde = fin + espera(n)</c> (<see cref="Decidir"/>), y lanza
/// <see cref="ReintentoProgramadoException"/>.</item>
/// <item>El SDK lleva el resultado a <c>diagnostico.consultas-resultados.espera.n</c> (<see cref="Esperas"/>): colas con un tick FIJO
/// de <see cref="TickCola"/> (15 s), que no depende de la configuración, así que cambiar el backoff nunca redeclara una cola con otro
/// TTL (406). La espera real la da <c>disponible_desde</c>: el mensaje da vueltas de 15 s hasta que vence.</item>
/// <item><see cref="ReanudarAsync"/>: el resultado demorado vuelve; si la espera de la celda no venció da otra vuelta, y si venció
/// publica el trabajo nuevo (<c>trabajo_id</c> nuevo) y se confirma.</item>
/// </list>
/// Un definitivo o los reintentos agotados cierran la celda en ERROR con su <c>tipo_error</c>; eso lo hace el consumidor. Los logs
/// llevan ids, fuente y tipo de error; nunca documentos ni datos de la fuente.
/// </summary>
/// <example>
/// <code>
/// var decision = programador.Decidir(resultado.TipoError, celda.IntentosAuto, fin);
/// if (decision.Reintentar)
///     await programador.ProgramarAsync(celda, resultado, decision, intentos, ct); // lanza ReintentoProgramadoException
/// </code>
/// </example>
internal sealed partial class ProgramadorReintentos(
    IServiceScopeFactory scopes, TimeProvider reloj, IOptions<OpcionesConsultas> opciones, AzarReintentos azar, ILogger<ProgramadorReintentos> log)
{
    /// <summary>TTL de cada cola de espera: constante, NO se configura (así nunca hay 406 al redeclarar).</summary>
    public static readonly TimeSpan TickCola = TimeSpan.FromSeconds(15);

    /// <summary>Sufijo de las colas de espera: <c>diagnostico.consultas-resultados.espera.1..N</c>.</summary>
    public const string SufijoCola = "espera";

    /// <summary>Vueltas extra por encima de la espera máxima: conflicto de concurrencia, desfase de reloj o un mensaje que llegó tarde.</summary>
    private const int VueltasExtra = 4;

    /// <summary>
    /// Esperas de la cola del consumidor de resultados para una política: <see cref="TickCola"/> repetido
    /// <c>ceil(EsperaMaximaEfectiva / TickCola) + 4</c> veces (24 con la configuración por defecto: 4 min 48 s). Agregar colas con el
    /// mismo TTL no choca con las que ya existen.
    /// </summary>
    public static IReadOnlyList<TimeSpan> EsperasDe(PoliticaReintentos politica)
    {
        ArgumentNullException.ThrowIfNull(politica);
        var vueltas = (int)Math.Ceiling(politica.EsperaMaximaEfectiva.Ticks / (double)TickCola.Ticks) + VueltasExtra;
        return [.. Enumerable.Repeat(TickCola, vueltas)];
    }

    /// <summary>La política con las claves de <paramref name="opciones"/> (las inválidas toman su valor por defecto).</summary>
    public static PoliticaReintentos PoliticaDe(OpcionesConsultas opciones)
    {
        ArgumentNullException.ThrowIfNull(opciones);
        return opciones.Politica();
    }

    /// <summary>La política con los reintentos, la base, el factor y el mínimo del límite configurados.</summary>
    public PoliticaReintentos Politica { get; } = PoliticaDe(opciones.Value);

    /// <summary>Esperas de la cola con la política configurada (<see cref="EsperasDe"/>).</summary>
    public IReadOnlyList<TimeSpan> Esperas => EsperasDe(Politica);

    /// <summary>Decisión de la política con el azar inyectado (<see cref="AzarReintentos"/>).</summary>
    public DecisionReintento Decidir(string? tipoError, int intentosAuto, DateTimeOffset fin) =>
        Politica.Decidir(tipoError, intentosAuto, fin, azar.Siguiente());

    /// <summary>
    /// Deja la celda PENDIENTE con su reintento programado, en un scope propio, y lanza <see cref="ReintentoProgramadoException"/>.
    /// Si la celda ya no es de ese trabajo (o ya no está en curso) no cambia nada y no lanza: el resultado se descarta.
    /// </summary>
    public async Task ProgramarAsync(
        CeldaEnCurso celda, ResultadoCelda resultado, DecisionReintento decision, IReadOnlyCollection<IntentoConsulta> intentos, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(celda);
        ArgumentNullException.ThrowIfNull(resultado);
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(intentos);
        if (!decision.Reintentar || decision.DisponibleDesde is not { } desde)
            throw new ArgumentException("Solo se programa una decisión de reintento.", nameof(decision));

        // H2 del diseño: lo que escribe el mensaje se revierte al lanzar; el reintento y su intento van en su propia transacción.
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<EmpresaDelTrabajo>().Fijar(celda.TenantId);
        var db = scope.ServiceProvider.GetRequiredService<DiagnosticoDb>();
        var programado = await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaccion = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            var fila = await db.Set<CeldaConsulta>().SingleOrDefaultAsync(c => c.Id == celda.Id, ct).ConfigureAwait(false);
            if (fila is null || fila.Estado != EstadoCelda.EnCurso || fila.TrabajoId is null || fila.TrabajoId != celda.TrabajoId)
                return false;

            var version = await EscritorCeldas.SubirVersionTableroAsync(db, fila.DiagnosticoId, ct).ConfigureAwait(false);
            fila.Estado = EstadoCelda.Pendiente;
            fila.Payload = resultado.Payload;
            // HU #14087 AC8: con el payload, la respuesta completa (en las de persona, la de las que ya respondieron).
            fila.RespuestaFuenteCifrada = resultado.RespuestaFuenteCifrada;
            fila.TipoError = null;
            fila.DisponibleDesde = desde;
            fila.IntentosAuto = decision.IntentosAuto;
            // HU #13746: el reproceso se consumió en esta ejecución; el reintento es AUTO.
            fila.ReprocesoUsuarioId = null;
            fila.ActualizadoEn = reloj.GetUtcNow();
            fila.VersionTablero = version;
            db.AddRange(intentos);
            try
            {
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
            catch (DbUpdateConcurrencyException)
            {
                return false;
            }

            await transaccion.CommitAsync(ct).ConfigureAwait(false);
            return true;
        }).ConfigureAwait(false);

        if (!programado)
        {
            NoProgramado(log, celda.Id, celda.Fuente);
            return;
        }

        var esperaSegundos = (long)Math.Max(0, Math.Round((desde - reloj.GetUtcNow()).TotalSeconds));
        Programado(log, celda.Id, celda.Fuente, resultado.TipoError ?? "-", decision.IntentosAuto, Politica.MaxReintentos, esperaSegundos);
        throw new ReintentoProgramadoException("Reintento programado de la celda: el resultado espera en la cola de reintentos.");
    }

    /// <summary>
    /// Volvió el resultado demorado de una celda PENDIENTE de su reintento: si la espera no venció lanza
    /// <see cref="ReintentoProgramadoException"/> (otra vuelta por la cola de reintentos); si venció, publica el trabajo nuevo con el
    /// publicador del scope (el del mismo contexto que <paramref name="celda"/>). No guarda.
    /// </summary>
    public static async Task ReanudarAsync(CeldaConsulta celda, DateTimeOffset ahora, PublicadorCeldas publicador, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(celda);
        ArgumentNullException.ThrowIfNull(publicador);
        if (celda.DisponibleDesde > ahora)
            throw new ReintentoProgramadoException("La espera del reintento no ha vencido: el resultado da otra vuelta por la cola de reintentos.");

        // El publicador da el trabajo_id nuevo y deja la celda EN_CURSO (o la cierra si ya no hay qué consultar o hay dato de hoy).
        await publicador.PublicarAsync(celda, ct).ConfigureAwait(false);
    }

    [LoggerMessage(EventId = 137372, Level = LogLevel.Information,
        Message = "La celda {CeldaId} de {Fuente} terminó en {TipoError} (transitorio): reintento {Reintento} de {Maximo} en {EsperaSegundos} s; el resultado espera en la cola de reintentos")]
    private static partial void Programado(ILogger logger, Guid celdaId, Fuente fuente, string tipoError, int reintento, int maximo, long esperaSegundos);

    [LoggerMessage(EventId = 137373, Level = LogLevel.Information,
        Message = "La celda {CeldaId} de {Fuente} ya no está en curso con ese trabajo: el resultado se descarta sin reintento")]
    private static partial void NoProgramado(ILogger logger, Guid celdaId, Fuente fuente);
}

/// <summary>
/// Azar del backoff (HU #14752): un valor en [0, 1) por decisión. Las pruebas lo reemplazan por uno fijo para que
/// <c>disponible_desde</c> sea determinista.
/// </summary>
internal class AzarReintentos
{
    public virtual double Siguiente() => Random.Shared.NextDouble();
}
