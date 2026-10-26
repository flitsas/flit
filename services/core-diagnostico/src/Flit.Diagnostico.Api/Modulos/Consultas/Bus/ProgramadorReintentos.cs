using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Consultas;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Flit.Diagnostico.Api.Modulos.Consultas.Bus;

/// <summary>
/// Reintentos de las consultas por el bus (ADR-0075, Q4, U2 = a; HU #13737, D-12): los decide Diagnóstico con
/// <see cref="PoliticaReintentos"/> (<c>Diagnostico:Consultas:MaxReintentos</c>, 0..3) y core-consultas consulta una sola vez.
/// Un resultado transitorio con reintentos disponibles sigue tres pasos:
/// <list type="number">
/// <item><see cref="ProgramarAsync"/>: en un scope PROPIO (el del mensaje se revierte) la celda vuelve a PENDIENTE con su intento
/// auditable, <c>intentos_auto + 1</c> y <c>disponible_desde = fin + Espera(n)</c>, y lanza
/// <see cref="ReintentoProgramadoException"/>.</item>
/// <item>El SDK lleva el resultado a <c>diagnostico.consultas-resultados.retry.n</c> (<see cref="Esperas"/>: 5, 10 y 20 s).</item>
/// <item><see cref="ReanudarAsync"/>: el resultado demorado vuelve; si la espera de la celda no venció (un segundo o tercer
/// reintento espera más que la cola en que cayó) da otra vuelta, y si venció publica el trabajo nuevo (<c>trabajo_id</c> nuevo) y
/// se confirma.</item>
/// </list>
/// Un definitivo o los reintentos agotados cierran la celda en ERROR con su <c>tipo_error</c>; eso lo hace el consumidor. Los logs
/// llevan ids, fuente y tipo de error; nunca documentos ni datos de la fuente.
/// </summary>
/// <example>
/// <code>
/// var decision = programador.Politica.Decidir(resultado.TipoError, celda.IntentosAuto, fin);
/// if (decision.Reintentar)
///     await programador.ProgramarAsync(celda, resultado, decision, intentos, ct); // lanza ReintentoProgramadoException
/// </code>
/// </example>
internal sealed partial class ProgramadorReintentos(
    IServiceScopeFactory scopes, TimeProvider reloj, IOptions<OpcionesConsultas> opciones, ILogger<ProgramadorReintentos> log)
{
    /// <summary>Esperas de la cola de reintentos del consumidor de resultados: <c>PoliticaReintentos.Espera(1..3)</c>.</summary>
    public static IReadOnlyList<TimeSpan> Esperas { get; } = [.. Enumerable.Range(1, PoliticaReintentos.Maximo).Select(PoliticaReintentos.Espera)];

    /// <summary>La política con los reintentos configurados.</summary>
    public PoliticaReintentos Politica { get; } = new(opciones.Value.MaxReintentos);

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

        Programado(log, celda.Id, celda.Fuente, resultado.TipoError ?? "-", decision.IntentosAuto, Politica.MaxReintentos);
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

        celda.DisponibleDesde = ahora;
        celda.ActualizadoEn = ahora;
        if (await publicador.PublicarAsync(celda, ct).ConfigureAwait(false) > 0)
            celda.Estado = EstadoCelda.EnCurso;
    }

    [LoggerMessage(EventId = 137372, Level = LogLevel.Information,
        Message = "La celda {CeldaId} de {Fuente} terminó en {TipoError} (transitorio): reintento {Reintento} de {Maximo}; el resultado espera en la cola de reintentos")]
    private static partial void Programado(ILogger logger, Guid celdaId, Fuente fuente, string tipoError, int reintento, int maximo);

    [LoggerMessage(EventId = 137373, Level = LogLevel.Information,
        Message = "La celda {CeldaId} de {Fuente} ya no está en curso con ese trabajo: el resultado se descarta sin reintento")]
    private static partial void NoProgramado(ILogger logger, Guid celdaId, Fuente fuente);
}
