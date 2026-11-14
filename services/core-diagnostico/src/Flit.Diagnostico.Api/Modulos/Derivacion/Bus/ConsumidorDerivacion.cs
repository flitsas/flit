using Flit.Diagnostico.Api.Modulos.Consultas.Bus;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Derivacion;
using Flit.Diagnostico.Domain.Personas;
using Flit.Platform.Sdk.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Flit.Diagnostico.Api.Modulos.Derivacion.Bus;

/// <summary>
/// Consumidor de <see cref="DerivacionSolicitada"/> en la cola <see cref="DerivacionSolicitada.Cola"/> (Punto 4, incremento 3, diseño
/// §5.3 punto 3, §7 segundo diagrama, ADR-0084). El SDK ya abrió la transacción, anota la bandeja (un <c>eventId</c> repetido no
/// repite el efecto) y solo acepta mensajes del propio Diagnóstico. Por cada mensaje:
/// <list type="number">
/// <item>Fija la empresa del sobre (<see cref="EmpresaDelTrabajo"/>), lo primero: el contexto, la outbox y el despachador del scope la
/// usan; un ítem de otra empresa no se ve y el mensaje se descarta.</item>
/// <item>Carga el ítem; si ya no está ENVIADO (re-entrega de uno cerrado), se confirma sin efecto. Si su espera de reintento no venció,
/// lanza <see cref="ReintentoProgramadoException"/> (otra vuelta por la cola de reintentos) sin llamar.</item>
/// <item>Revalida (4.6, AC7) con <see cref="LecturaElegibilidad"/> excluyendo su propio ítem: no elegible → OMITIDO con el motivo, sin
/// llamar.</item>
/// <item>Arma la solicitud con <see cref="MapeoBorradorTramite"/> (descifra el documento SOLO aquí; autor: <see cref="ItemDerivacion.Autor"/>, quien
/// reintentó el ítem o, si nadie, quien lanzó la operación) y llama con
/// <see cref="EjecutorItemDerivacion"/> (freno + rpc).</item>
/// <item>Reintentar: <see cref="ReintentoDerivacion.ProgramarAsync"/> (scope propio; conserva el crédito) y la señal de reintento.
/// Cerrar: <see cref="CierreItemDerivacion"/> (único punto de cierre) y, en la MISMA transacción,
/// <see cref="DespachadorCredito.LiberarAsync"/> libera el siguiente de la compañía.</item>
/// </list>
/// Nunca lanza por errores de Trámites: solo la señal de reintento o fallas de infraestructura (la base), que el SDK reintenta (una
/// re-entrega llama otra vez y Trámites responde el mismo borrador con <c>ya_existia</c>). Logs: ids, estado y código; nunca placa, VIN
/// ni documento.
/// </summary>
/// <example>
/// <code>
/// services.AddFlitConsumer&lt;DiagnosticoDb, ConsumidorDerivacion, DerivacionSolicitada&gt;(
///     configuration, DerivacionSolicitada.Cola, DerivacionSolicitada.Productor, [DerivacionSolicitada.Tipo], o =&gt; o.Concurrencia = 4);
/// </code>
/// </example>
internal sealed partial class ConsumidorDerivacion(
    EmpresaDelTrabajo empresa,
    DiagnosticoDb db,
    EjecutorItemDerivacion ejecutor,
    DespachadorCredito despachador,
    ReintentoDerivacion reintento,
    IProtectorDocumentoPersona protector,
    TimeProvider reloj,
    ILogger<ConsumidorDerivacion> log) : IEventConsumer<DerivacionSolicitada>
{
    public async Task HandleAsync(EventEnvelope envelope, DerivacionSolicitada data, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(data);
        if (envelope.TenantId == Guid.Empty)
        {
            Descartado(log, data.ItemId, "SIN_EMPRESA");
            return;
        }

        // ADR-0072 / ADR-0084: la empresa del sobre es la primera línea; todo lo demás del scope trabaja con ella.
        empresa.Fijar(envelope.TenantId);
        var item = await db.Set<ItemDerivacion>().SingleOrDefaultAsync(i => i.Id == data.ItemId, ct).ConfigureAwait(false);
        if (item is null || item.Estado != EstadoItemDerivacion.Enviado)
        {
            Descartado(log, data.ItemId, item is null ? "SIN_ITEM" : "ITEM_CERRADO");
            return;
        }

        if (item.DisponibleDesde is { } desde && desde > reloj.GetUtcNow() + ReintentoDerivacion.Tolerancia)
            throw new ReintentoProgramadoException("La espera del reintento del ítem de derivación no ha vencido: otra vuelta por la cola de reintentos.");

        var operacion = await db.Set<OperacionDerivacion>().SingleAsync(o => o.Id == item.OperacionId, ct).ConfigureAwait(false);
        var vehiculo = await db.Set<VehiculoDiagnostico>().SingleAsync(v => v.Id == item.VehiculoId, ct).ConfigureAwait(false);
        var modo = await db.Set<CabeceraDiagnostico>().AsNoTracking().Where(c => c.Id == operacion.DiagnosticoId).Select(c => c.Modo).SingleAsync(ct).ConfigureAwait(false);
        var elegibilidad = await LecturaElegibilidad.LeerAsync(db, operacion.DiagnosticoId, modo, [vehiculo.Id], ct, excluirItem: item.Id).ConfigureAwait(false);
        var motivo = elegibilidad.TryGetValue(vehiculo.Id, out var e) ? e.Motivo : MotivoDerivacion.NoTerminado;
        if (motivo is not null)
        {
            await CerrarAsync(operacion, item, vehiculo, new ResultadoBorrador.Rechazado(motivo, MotivoDerivacion.Mensaje(motivo)), envelope.TenantId, ct).ConfigureAwait(false);
            return;
        }

        var solicitud = MapeoBorradorTramite.Armar(
            vehiculo, operacion.TipoTramiteCodigo, item.Autor(operacion), item.Id,
            await ApoyoDerivacion.OrganismoCodigoAsync(db, vehiculo.OrganismoNombre, ct).ConfigureAwait(false), protector);
        var paso = await ejecutor.EjecutarAsync(envelope.TenantId, solicitud, item.Intentos, ct).ConfigureAwait(false);
        switch (paso)
        {
            case PasoDerivacion.Reintentar r:
                // Lanza ReintentoProgramadoException (o vuelve sin efecto si el ítem ya no está en vuelo).
                await reintento.ProgramarAsync(envelope.TenantId, item.Id, r.Intentos, r.DisponibleDesde, ct).ConfigureAwait(false);
                return;
            case PasoDerivacion.Cerrar c:
                item.Intentos = c.Intentos;
                await CerrarAsync(operacion, item, vehiculo, c.Resultado, envelope.TenantId, ct).ConfigureAwait(false);
                return;
        }
    }

    private async Task CerrarAsync(OperacionDerivacion operacion, ItemDerivacion item, VehiculoDiagnostico vehiculo, ResultadoBorrador resultado, Guid tenant, CancellationToken ct)
    {
        // HU #14563: la versión de fila sube en la transacción del consumidor (la del SDK), antes de que se guarde el ítem.
        var ahora = reloj.GetUtcNow();
        await CierreItemDerivacion.CerrarAsync(db, operacion, item, vehiculo, resultado, ahora, ct).ConfigureAwait(false);
        await Notificaciones.EscritorNotificaciones.SiTerminoDerivacionMasivaAsync(db, operacion, ahora, ct).ConfigureAwait(false);
        await despachador.LiberarAsync(tenant, ct).ConfigureAwait(false);
        Cerrado(log, item.Id, operacion.Id, item.Estado, item.MotivoCodigo ?? "-", item.Intentos);
    }

    [LoggerMessage(EventId = 84330, Level = LogLevel.Information, Message = "Derivación: mensaje del ítem {ItemId} descartado ({Motivo}).")]
    private static partial void Descartado(ILogger logger, Guid itemId, string motivo);

    [LoggerMessage(EventId = 84331, Level = LogLevel.Information, Message = "Derivación masiva: ítem {ItemId} de la operación {OperacionId} cerró en {Estado} ({Motivo}) tras {Intentos} intentos.")]
    private static partial void Cerrado(ILogger logger, Guid itemId, Guid operacionId, EstadoItemDerivacion estado, string motivo, short intentos);
}
