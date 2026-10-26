using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Modulos.Tablero;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Platform.Sdk.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Flit.Diagnostico.Api.Modulos.Consultas.Bus;

/// <summary>
/// Publica o cierra la celda que está lista para consultarse (ADR-0075, HU #13727): es el único camino por el que una celda sale a
/// core-consultas y por el que se cierra (desde el consumidor de resultados, el encolado, la liberación tras la RUNT, el reproceso
/// y el reconciliador). <see cref="PublicarAsync"/>:
/// <list type="number">
/// <item>Reutilización del mismo día (HU #13739, D-10; Q3 del diseño): si la misma compañía ya tiene hoy el dato de esa placa y
/// fuente, se copia y la celda se cierra en la misma transacción, sin mensaje ni consumo. El reproceso (HU #13746, R-1) nunca
/// reutiliza.</item>
/// <item>Las consultas las arma el <see cref="IConstructorSolicitud"/> de su fuente. Si no hay ninguna (sin vehículo, sin placa,
/// sin personas, departamento no soportado) la celda se cierra con <see cref="IEjecutorCelda.SinConsultaAsync"/>.</item>
/// <item>Si hay consultas: <c>trabajo_id</c> nuevo (UUIDv7, <see cref="CeldaConsulta.TrabajoId"/>), la celda queda EN_CURSO con
/// <c>disponible_desde</c> = ahora (el reconciliador mide desde ahí) y en la outbox del scope un trabajo
/// <c>consultas.diagnostico[-express].&lt;fuente&gt;</c> por consulta (en las celdas de persona, uno por persona con el mismo
/// <c>trabajo_id</c>, y el payload conserva solo a las que ya respondieron OK más, en ERROR, las que no tienen documento consultable),
/// con la solicitud en proto-JSON y en claro (U1 = b).
/// El prefijo <c>-express</c> sale de <see cref="CabeceraDiagnostico.EsExpress"/> del lote.</item>
/// </list>
/// <see cref="CerrarAsync"/> cierra la celda (<see cref="EscritorCeldas"/>) con sus intentos y efectos; al cerrar la RUNT libera
/// (y publica, ya con los datos de la RUNT guardados en la transacción) o bloquea las demás celdas del vehículo
/// (<see cref="OrquestadorVehiculo"/>, HU #13730) y recalcula el estado del lote (<see cref="RecalculadorEstado"/>, HU #13743; si
/// queda terminado, <c>diagnostico.lote.terminado</c> sale por la misma outbox). No confirma: todo va en la transacción de quien
/// llama, así que si esa se revierte no queda mensaje. La celda debe venir del MISMO contexto que la outbox (el del scope, con
/// <c>EmpresaDelTrabajo</c> en los trabajos en segundo plano). Los logs llevan ids y fuente, nunca la solicitud.
/// </summary>
/// <example>
/// <code>
/// await publicador.PublicarAsync(celda, ct);   // reutiliza, cierra sin consulta o publica: sin guardar
/// await db.SaveChangesAsync(ct);               // todo en la misma transacción
/// </code>
/// </example>
internal sealed partial class PublicadorCeldas(
    DiagnosticoDb db, IPlatformOutbox outbox, IEnumerable<IConstructorSolicitud> constructores, IEnumerable<IEjecutorCelda> ejecutores,
    TimeProvider reloj, ILogger<PublicadorCeldas> log)
{
    private readonly Dictionary<Guid, bool> _express = [];

    /// <summary>
    /// Reutiliza, cierra sin consulta o publica el trabajo de la celda; devuelve cuántas consultas dejó en la outbox (0 si la celda
    /// quedó cerrada). <paramref name="versionTablero"/> es la versión que ya subió quien llama (encolado, liberación, reproceso);
    /// sin ella, un cierre la sube. Con <paramref name="recalcular"/> en false el cierre no recalcula el lote (lo hace quien llama:
    /// la RUNT que libera).
    /// </summary>
    public async Task<int> PublicarAsync(CeldaConsulta celda, CancellationToken ct, long? versionTablero = null, bool recalcular = true)
    {
        ArgumentNullException.ThrowIfNull(celda);
        if (db.Entry(celda).State == EntityState.Detached)
            throw new InvalidOperationException("La celda que se publica debe venir del contexto del scope, el mismo de la outbox.");

        var ahora = reloj.GetUtcNow();
        var contexto = new ContextoCelda(CeldaEnCurso.De(celda), db);
        // HU #13739 (D-10): el dato de hoy de la misma compañía se copia sin mensaje; HU #13746 (R-1): el reproceso nunca reutiliza.
        if (celda.ReprocesoUsuarioId is null
            && await ReutilizacionMismoDia.BuscarAsync(contexto, ahora, ct).ConfigureAwait(false) is { } reutilizado)
        {
            Reutilizada(log, celda.Id, celda.Fuente, reutilizado.ReutilizadaDeId!.Value);
            await CerrarAsync(celda, reutilizado, RegistroIntentos.CrearTodos(contexto.Celda, ahora, ahora, reutilizado), versionTablero, ct, recalcular)
                .ConfigureAwait(false);
            return 0;
        }

        IReadOnlyList<SolicitudConsulta> solicitudes;
        ResultadoCelda? sinConsulta = null;
        try
        {
            solicitudes = constructores.FirstOrDefault(c => c.Fuente == celda.Fuente) is { } constructor
                ? await constructor.ConstruirAsync(db, celda, ct).ConfigureAwait(false)
                : [];
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // Un documento que no se puede descifrar no tumba la transacción: la celda queda en ERROR definitivo.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            SinSolicitud(log, celda.Id, celda.Fuente, ex.GetType().Name);
            solicitudes = [];
            sinConsulta = ResultadoCelda.Error() with { Intento = new DatosIntento(null, null, null, $"No se pudo armar la solicitud ({ex.GetType().Name}).") };
        }

        if (solicitudes.Count == 0)
        {
            celda.TrabajoId = null;
            sinConsulta ??= await Ejecutor(celda.Fuente).SinConsultaAsync(contexto, ct).ConfigureAwait(false);
            await CerrarAsync(celda, sinConsulta, RegistroIntentos.CrearTodos(contexto.Celda, ahora, ahora, sinConsulta), versionTablero, ct, recalcular)
                .ConfigureAwait(false);
            return 0;
        }

        var esExpress = await EsExpressAsync(celda.DiagnosticoId, ct).ConfigureAwait(false);
        var trabajo = Guid.CreateVersion7();
        celda.TrabajoId = trabajo;
        celda.Estado = EstadoCelda.EnCurso;
        celda.TipoError = null;
        celda.DisponibleDesde = ahora;
        celda.ActualizadoEn = ahora;
        celda.Republicaciones = 0;
        if (EjecutorPorPersonas.EsPorPersona(celda.Fuente))
        {
            // Las personas sin documento consultable no publican: entran al payload en ERROR para no bloquear el cierre (HU #13727).
            celda.Payload = Ejecutor(celda.Fuente) is EjecutorPorPersonas porPersonas
                ? porPersonas.AlPublicar(celda.Payload, await EjecutorPorPersonas.PersonasDelRolAsync(db, celda, ct).ConfigureAwait(false),
                    solicitudes.Select(s => s.PersonaId).OfType<Guid>().ToHashSet())
                : EjecutorPorPersonas.SoloRespondidas(celda.Payload);
        }
        foreach (var solicitud in solicitudes)
        {
            outbox.EnqueueJob(TrabajoConsultaDiagnostico.Clave(solicitud.Fuente, esExpress), TrabajoConsultaDiagnostico.VersionContrato,
                celda.TenantId, TrabajoConsultaDiagnostico.Crear(trabajo, celda.Id, solicitud));
        }

        Publicada(log, celda.Id, celda.Fuente, trabajo, solicitudes.Count, esExpress);
        return solicitudes.Count;
    }

    /// <summary>
    /// Cierra la celda con su resultado e intentos en la transacción de quien llama (<see cref="EscritorCeldas"/>), con los efectos
    /// del resultado; si es la RUNT, libera (y publica) o bloquea las demás celdas del vehículo, y recalcula el estado del lote.
    /// </summary>
    public async Task CerrarAsync(
        CeldaConsulta celda, ResultadoCelda resultado, IReadOnlyCollection<IntentoConsulta> intentos, long? versionTablero, CancellationToken ct,
        bool recalcular = true)
    {
        ArgumentNullException.ThrowIfNull(celda);
        ArgumentNullException.ThrowIfNull(resultado);
        ArgumentNullException.ThrowIfNull(intentos);
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("El cierre de una celda exige la transacción de quien la cierra.");

        var ahora = reloj.GetUtcNow();
        var version = versionTablero ?? await EscritorCeldas.SubirVersionTableroAsync(db, celda.DiagnosticoId, ct).ConfigureAwait(false);
        var reprocesoDeLaRunt = celda.ReprocesoUsuarioId;
        EscritorCeldas.Aplicar(celda, resultado, version, ahora);
        if (intentos.Count > 0)
            db.AddRange(intentos);
        if (resultado.Efectos is { } efectos)
            await efectos(db, version, ct).ConfigureAwait(false);

        if (celda.Fuente == Fuente.Runt && resultado.Estado == EstadoCelda.Completado)
        {
            var liberadas = await OrquestadorVehiculo.LiberarCeldasDelVehiculoAsync(db, celda.VehiculoId, ahora, version, ct, reprocesoDeLaRunt).ConfigureAwait(false);
            if (liberadas.Count > 0)
            {
                // Las liberadas se arman con lo que acaba de dejar la RUNT (placa, datos técnicos, personas, departamento): se guarda
                // primero, dentro de la misma transacción, para que la reutilización y los constructores lo lean.
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                foreach (var liberada in liberadas)
                    await PublicarAsync(liberada, ct, version, recalcular: false).ConfigureAwait(false);
            }
        }
        else if (celda.Fuente == Fuente.Runt && resultado.Estado == EstadoCelda.Error)
        {
            await OrquestadorVehiculo.BloquearCeldasDelVehiculoAsync(db, celda.VehiculoId, ahora, version, ct).ConfigureAwait(false);
        }

        // HU #13743: tras cada celda terminal, el estado global del lote se recalcula en la misma transacción de cierre.
        if (recalcular)
            await RecalculadorEstado.RecalcularAsync(db, outbox, celda.DiagnosticoId, version, ahora, ct).ConfigureAwait(false);
    }

    private IEjecutorCelda Ejecutor(Fuente fuente) =>
        // Gana el primero registrado para la fuente: las pruebas ponen su doble antes que Program (como con TryAdd).
        ejecutores.FirstOrDefault(e => e.Fuente == fuente)
        ?? throw new InvalidOperationException($"La fuente {fuente} no tiene ejecutor: no se puede cerrar su celda.");

    private async Task<bool> EsExpressAsync(Guid diagnosticoId, CancellationToken ct)
    {
        if (_express.TryGetValue(diagnosticoId, out var esExpress))
            return esExpress;
        esExpress = db.ChangeTracker.Entries<CabeceraDiagnostico>().FirstOrDefault(e => e.Entity.Id == diagnosticoId)?.Entity.EsExpress
            ?? await db.Set<CabeceraDiagnostico>().Where(c => c.Id == diagnosticoId).Select(c => c.EsExpress).SingleAsync(ct).ConfigureAwait(false);
        _express[diagnosticoId] = esExpress;
        return esExpress;
    }

    [LoggerMessage(EventId = 137275, Level = LogLevel.Information,
        Message = "La celda {CeldaId} de {Fuente} publicó el trabajo {TrabajoId} con {Consultas} consultas (express: {EsExpress})")]
    private static partial void Publicada(ILogger logger, Guid celdaId, Fuente fuente, Guid trabajoId, int consultas, bool esExpress);

    [LoggerMessage(EventId = 137276, Level = LogLevel.Warning,
        Message = "La celda {CeldaId} de {Fuente} no pudo armar su solicitud ({TipoError}); queda en ERROR sin publicar")]
    private static partial void SinSolicitud(ILogger logger, Guid celdaId, Fuente fuente, string tipoError);

    [LoggerMessage(EventId = 137390, Level = LogLevel.Information, Message = "La celda {CeldaId} de {Fuente} reutilizó sin consumo el dato del mismo día de la celda {Original}")]
    private static partial void Reutilizada(ILogger logger, Guid celdaId, Fuente fuente, Guid original);
}
