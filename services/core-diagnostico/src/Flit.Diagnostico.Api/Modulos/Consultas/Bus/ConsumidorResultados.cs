using System.Data.Common;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Platform.Sdk.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Flit.Diagnostico.Api.Modulos.Consultas.Bus;

/// <summary>
/// Consumidor de <c>consultas.resultado.diagnostico</c> en la cola <see cref="OpcionesBus.Cola"/> (ADR-0075, Q2, Q4; HU #13727 y
/// #13737): cierra la celda con lo que respondió core-consultas. El SDK ya abrió la transacción y anota la bandeja (un
/// <c>eventId</c> repetido no repite el efecto) y solo acepta mensajes del productor <c>consultas</c> con su <c>user_id</c> validado.
/// Por cada resultado:
/// <list type="number">
/// <item>Fija la empresa del sobre (<see cref="EmpresaDelTrabajo"/>), lo primero: el contexto, la outbox y el publicador del scope la
/// usan; una celda de otra empresa no se ve y el resultado se descarta.</item>
/// <item>Correlación por <c>trabajo_id</c>: si el del resultado ya no es el vigente de la celda (reproceso, republicación o un
/// duplicado tardío), se descarta sin efecto. Si la celda está PENDIENTE con ese trabajo, es el resultado demorado de un reintento:
/// <see cref="ProgramadorReintentos.ReanudarAsync"/> publica el trabajo nuevo o le da otra vuelta. Si no está EN_CURSO, se
/// descarta.</item>
/// <item>Lee el <c>*Response</c> (proto-JSON) o el <c>grpcStatus</c> y lo aplica con el <see cref="IEjecutorCelda"/> de la fuente
/// (<c>Mapeos/*</c>). En una celda de persona que aún espera a otras solo guarda la respuesta de esa persona y su intento.</item>
/// <item>Un ERROR transitorio con reintentos disponibles (<see cref="ProgramadorReintentos.Politica"/>) se programa en un scope
/// propio y lanza <see cref="ReintentoProgramadoException"/> (el SDK lleva el resultado a la cola de reintentos de 5, 10 y 20 s).
/// Lo demás (OK, definitivo o reintentos agotados) cierra la celda con <see cref="PublicadorCeldas.CerrarAsync"/>: intentos, datos
/// del vehículo, liberación o bloqueo de las demás celdas si es la RUNT y recálculo del lote, todo en la transacción del mensaje.</item>
/// </list>
/// Nunca lanza por errores del proveedor: solo la señal de reintento o fallas de infraestructura (la base), que el SDK reintenta.
/// Una falla inesperada al aplicar la respuesta deja la celda en ERROR (definitivo, reprocesable). Los logs llevan ids, fuente y
/// tipo de error; nunca placa, VIN, documentos, la respuesta ni <c>ex.Message</c>.
/// </summary>
/// <example>
/// <code>
/// services.AddFlitConsumer&lt;DiagnosticoDb, ConsumidorResultados, ResultadoConsultaDiagnostico&gt;(
///     configuration, OpcionesBus.Cola, OpcionesBus.Productor, [ResultadoConsultaDiagnostico.Tipo], o =&gt; o.Concurrencia = 4);
/// </code>
/// </example>
internal sealed partial class ConsumidorResultados(
    EmpresaDelTrabajo empresa, DiagnosticoDb db, PublicadorCeldas publicador, IEnumerable<IEjecutorCelda> ejecutores, ProgramadorReintentos programador,
    TimeProvider reloj, ILogger<ConsumidorResultados> log) : IEventConsumer<ResultadoConsultaDiagnostico>
{
    public async Task HandleAsync(EventEnvelope envelope, ResultadoConsultaDiagnostico data, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(data);
        if (envelope.TenantId == Guid.Empty)
        {
            Descartado(log, data.ConsultaId, data.TrabajoId, "SIN_EMPRESA");
            return;
        }

        // ADR-0075 / ADR-0072: la empresa del sobre es la primera línea; todo lo demás del scope trabaja con ella.
        empresa.Fijar(envelope.TenantId);
        var celda = await db.Set<CeldaConsulta>().SingleOrDefaultAsync(c => c.Id == data.ConsultaId, ct).ConfigureAwait(false);
        if (celda is null)
        {
            Descartado(log, data.ConsultaId, data.TrabajoId, "SIN_CELDA");
            return;
        }

        if (celda.TrabajoId is null || celda.TrabajoId != data.TrabajoId)
        {
            Descartado(log, celda.Id, data.TrabajoId, "TRABAJO_NO_VIGENTE");
            return;
        }

        var ahora = reloj.GetUtcNow();
        if (celda.Estado == EstadoCelda.Pendiente)
        {
            // Resultado demorado de un reintento programado (Q4, paso 3): publica el trabajo nuevo o da otra vuelta.
            await ProgramadorReintentos.ReanudarAsync(celda, ahora, publicador, ct).ConfigureAwait(false);
            return;
        }

        if (celda.Estado != EstadoCelda.EnCurso)
        {
            Descartado(log, celda.Id, data.TrabajoId, "CELDA_CERRADA");
            return;
        }

        if (ejecutores.FirstOrDefault(e => e.Fuente == celda.Fuente) is not { } ejecutor)
        {
            Descartado(log, celda.Id, data.TrabajoId, "SIN_EJECUTOR");
            return;
        }

        var enCurso = CeldaEnCurso.De(celda);
        var respuesta = data.Leer(envelope.OccurredAt);
        ResultadoCelda resultado;
        try
        {
            resultado = await ejecutor.AplicarAsync(new ContextoCelda(enCurso, db), respuesta, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // Una respuesta que no se puede aplicar no se reintenta: la celda queda en ERROR (reprocesable). La base caída sí se reintenta.
        catch (Exception ex) when (ex is not (DbException or DbUpdateException or TimeoutException))
#pragma warning restore CA1031
        {
            AplicacionFallida(log, celda.Id, celda.Fuente, ex.GetType().Name);
            resultado = ResultadoCelda.Error() with { Intento = new DatosIntento(null, null, null, $"Falla inesperada al aplicar la respuesta ({ex.GetType().Name}).") };
        }

        var intentos = RegistroIntentos.CrearTodos(enCurso, respuesta.Inicio, respuesta.Fin, resultado);
        if (resultado.Estado == EstadoCelda.EnCurso)
        {
            // HU #13792: una persona de la celda respondió y faltan otras; la celda sigue en curso con lo que ya hay.
            celda.Payload = resultado.Payload;
            celda.ActualizadoEn = ahora;
            db.AddRange(intentos);
            return;
        }

        if (resultado.Estado == EstadoCelda.Error)
        {
            var decision = programador.Politica.Decidir(resultado.TipoError, celda.IntentosAuto, respuesta.Fin);
            if (decision.Reintentar)
            {
                // Lanza ReintentoProgramadoException (o vuelve sin efecto si la celda ya no es de este trabajo).
                await programador.ProgramarAsync(enCurso, resultado, decision, intentos, ct).ConfigureAwait(false);
                return;
            }

            if (resultado.TipoErrorSinReintento is { } final)
                resultado = resultado with { TipoError = final };
        }

        await publicador.CerrarAsync(celda, resultado, intentos, versionTablero: null, ct).ConfigureAwait(false);
        Cerrada(log, celda.Id, celda.Fuente, celda.Estado, resultado.TipoError ?? "-");
    }

    [LoggerMessage(EventId = 137277, Level = LogLevel.Information,
        Message = "Resultado de consulta descartado para la celda {CeldaId} (trabajo {TrabajoId}): {Motivo}")]
    private static partial void Descartado(ILogger logger, Guid celdaId, Guid trabajoId, string motivo);

    [LoggerMessage(EventId = 137278, Level = LogLevel.Information, Message = "La celda {CeldaId} de {Fuente} cerró en {Estado} ({TipoError})")]
    private static partial void Cerrada(ILogger logger, Guid celdaId, Fuente fuente, EstadoCelda estado, string tipoError);

    [LoggerMessage(EventId = 137270, Level = LogLevel.Warning, Message = "La celda {CeldaId} de {Fuente} no pudo aplicar la respuesta ({TipoError}) y queda en ERROR")]
    private static partial void AplicacionFallida(ILogger logger, Guid celdaId, Fuente fuente, string tipoError);
}
