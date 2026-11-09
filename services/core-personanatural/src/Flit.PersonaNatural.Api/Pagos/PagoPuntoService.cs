using Flit.PersonaNatural.Api.Cotizaciones;
using Flit.PersonaNatural.Api.Persistence;
using Flit.PersonaNatural.Api.Punto;
using Flit.PersonaNatural.Api.Radicacion;
using Flit.PersonaNatural.Api.Sesiones;
using Flit.PersonaNatural.Api.Solicitudes;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Flit.PersonaNatural.Api.Pagos;

/// <summary>Motivo por el que el paso Pago no puede seguir (sin tarifa, medio apagado): se informa, no es un error de la consulta.</summary>
public sealed record BloqueoDePago(string Code, string Message);

/// <summary>
/// Cobro habilitado en caja. <see cref="Estado"/> es <c>vigente</c> o <c>vencido</c> según la hora de la consulta; el código de
/// cobro es el código público de la solicitud.
/// </summary>
public sealed record CobroEnCaja(string Codigo, string Estado, DateTimeOffset HabilitadoEn, DateTimeOffset VenceEn);

/// <summary>Pago confirmado: lo que queda cuando la solicitud ya está pagada.</summary>
public sealed record PagoConfirmado(long ValorCop, string Medio, string? MedioCaja, DateTimeOffset PagadoEn);

/// <summary>
/// Lo que el front consulta cada pocos segundos en el paso Pago: estado de la solicitud, identidades que faltan, desglose y
/// total vigentes, el cobro en caja y, si ya se pagó, el pago.
/// </summary>
public sealed record EstadoPagoPunto(
    Guid SolicitudId,
    string CodigoPublico,
    string Estado,
    bool PuedePagar,
    string? MotivoIdentidad,
    IReadOnlyList<ParteFaltante> IdentidadesPendientes,
    IReadOnlyList<LineaCobroDto>? Desglose,
    long? TotalCop,
    BloqueoDePago? Bloqueo,
    CobroEnCaja? Cobro,
    PagoConfirmado? Pago);

/// <summary>Respuesta de habilitar el pago en caja.</summary>
public sealed record CajaHabilitada(
    string CodigoCobro, DateTimeOffset HabilitadoEn, DateTimeOffset VenceEn, IReadOnlyList<LineaCobroDto> Desglose, long TotalCop);

/// <summary>
/// Paso Pago del canal del punto (Feature #14447; HU #14448, #14449 y #14450). Orquesta: pasar a pendiente de pago con todas las
/// identidades y requisitos completos, consultar el estado de pago, habilitar el cobro en caja y confirmar el trámite en $0. La
/// transición a «pagada» la hace <see cref="IRegistroDePagos"/>, compartida con el portal de caja.
/// </summary>
public sealed class PagoPuntoService(
    PersonaNaturalDb db,
    SesionService sesiones,
    ICompuertaDeRadicacion compuerta,
    CompletitudRequisitosPuntoService completitud,
    SolicitudesService solicitudes,
    CotizadorDelPunto cotizador,
    IRegistroDePagos registro,
    RadicacionService radicacion,
    TimeProvider reloj)
{
    public const string CodigoIdentidadPendiente = "identity_pending";
    public const string CodigoRequisitosIncompletos = "requisitos_incompletos";
    public const string CodigoEstadoNoPermitePago = "estado_no_permite_pago";

    // ── Pasar a pendiente de pago ────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Pasa la solicitud a <c>pendiente_pago</c> solo con TODAS las identidades validadas y los requisitos completos; si no, 409 con
    /// lo que falta y sin cambiar el estado. Repetir la petición con la solicitud ya en pago devuelve el estado (idempotente).
    /// </summary>
    public async Task<EstadoPagoPunto> IniciarAsync(OrganismoPunto sesion, string? token, CancellationToken ct)
    {
        var s = await ResolverAsync(sesion, token, rastrear: true, ct).ConfigureAwait(false);
        if (s.Estado == EstadoSolicitud.PendientePago)
            return await EstadoAsync(sesion, token, ct).ConfigureAwait(false);
        if (s.Estado is EstadoSolicitud.Pagada or EstadoSolicitud.EnCola or EstadoSolicitud.Creada)
            throw RegistroDePagos.YaPagado();
        if (!SolicitudesService.EsEditable(s.Estado))
            throw SolicitudesService.NoPermiteCambio(s.Estado);

        var veredicto = await compuerta.EvaluarAsync(s.Id, ct).ConfigureAwait(false);
        if (!veredicto.Permite)
        {
            throw new ReglaDeNegocioException(
                StatusCodes.Status409Conflict, CodigoIdentidadPendiente, "Falta validar la identidad", "Todas las partes del trámite deben validar su identidad antes de pagar.",
                new Dictionary<string, object?> { ["motivo"] = veredicto.Motivo, ["partes"] = veredicto.Partes });
        }

        await ExigirRequisitosAsync(sesion, token, s, ct).ConfigureAwait(false);
        await PasarAPendientePagoAsync(s.Id, ct).ConfigureAwait(false);
        return await EstadoAsync(sesion, token, ct).ConfigureAwait(false);
    }

    private async Task ExigirRequisitosAsync(OrganismoPunto sesion, string? token, SolicitudTramite s, CancellationToken ct)
    {
        var resultado = await completitud.EvaluarAsync(sesion, token, null, ct).ConfigureAwait(false);
        switch (resultado)
        {
            case ResultadoCompletitudPunto.Rechazo r:
                throw new ReglaDeNegocioException(r.Http, r.Codigo, "No se puede evaluar", r.Mensaje);
            case ResultadoCompletitudPunto.DatosInvalidos d:
                throw new ReglaDeNegocioException(StatusCodes.Status400BadRequest, "datos_invalidos", "Datos inválidos", d.Mensaje);
            case ResultadoCompletitudPunto.Ok { Completitud.PuedeContinuarPago: false } ok:
                throw Incompletos(ok.Completitud.Pendientes);
        }

        // Lo que la radicación también exige (campos del tipo): mejor avisar antes de cobrar que fallar después de pagar.
        var borrador = await solicitudes.ConstruirAsync(s, conPersonas: false, null, ct).ConfigureAwait(false);
        if (borrador.Pendientes.Count > 0)
            throw Incompletos(borrador.Pendientes);
    }

    private static ReglaDeNegocioException Incompletos(object pendientes) => new(
        StatusCodes.Status409Conflict, CodigoRequisitosIncompletos, "Falta información", "Complete lo que falta antes de pagar.",
        new Dictionary<string, object?> { ["pendientes"] = pendientes });

    private Task<bool> PasarAPendientePagoAsync(Guid id, CancellationToken ct) =>
        Transaccion.EjecutarAsync(db, async ct2 =>
        {
            var s = await Transaccion.BloquearSolicitudAsync(db, id, ct2).ConfigureAwait(false) ?? throw SolicitudesService.NoEncontrada();
            if (s.Estado == EstadoSolicitud.PendientePago)
                return true; // otra petición igual ya lo hizo
            if (!SolicitudesService.EsEditable(s.Estado))
                throw SolicitudesService.NoPermiteCambio(s.Estado);

            var ahora = reloj.GetUtcNow();
            db.Add(new HistorialEstado { Id = Guid.CreateVersion7(), SolicitudId = s.Id, Desde = s.Estado, Hasta = EstadoSolicitud.PendientePago, Origen = "bff", Motivo = "pasa_a_pago", OcurrioEn = ahora });
            s.Estado = EstadoSolicitud.PendientePago;
            s.ExpiraBorradorEn = null;
            s.RowVersion++;
            s.UpdatedAt = ahora;
            await db.SaveChangesAsync(ct2).ConfigureAwait(false);
            return true;
        }, ct);

    // ── Estado de pago ───────────────────────────────────────────────────────────────────────────────────────────────

    public async Task<EstadoPagoPunto> EstadoAsync(OrganismoPunto sesion, string? token, CancellationToken ct)
    {
        var s = await ResolverAsync(sesion, token, rastrear: false, ct).ConfigureAwait(false);

        // Pagada pero sin radicar (FLIT 2 no respondió cuando se pagó): cada consulta lo reintenta hasta que quede en cola.
        if (s.Estado == EstadoSolicitud.Pagada && await radicacion.IntentarRadicarPagadaAsync(s.Id, ct).ConfigureAwait(false))
            s = await ResolverAsync(sesion, token, rastrear: false, ct).ConfigureAwait(false);

        switch (s.Estado)
        {
            case EstadoSolicitud.Borrador or EstadoSolicitud.PendienteValidacion:
                var veredicto = await compuerta.EvaluarAsync(s.Id, ct).ConfigureAwait(false);
                return Armar(s, puedePagar: false, veredicto.Permite ? null : veredicto.Motivo, veredicto.Partes, null, null);
            case EstadoSolicitud.PendientePago:
                return await EstadoPendientePagoAsync(s, ct).ConfigureAwait(false);
            default:
                var pagado = await db.Set<CobroSolicitud>().AsNoTracking().FirstOrDefaultAsync(c => c.SolicitudId == s.Id && c.Estado == EstadoCobro.Pagado, ct).ConfigureAwait(false);
                return Armar(s, puedePagar: false, null, [], null, null, cobro: null, pago: pagado is null ? null : PagoDe(pagado));
        }
    }

    private async Task<EstadoPagoPunto> EstadoPendientePagoAsync(SolicitudTramite s, CancellationToken ct)
    {
        var cobro = await db.Set<CobroSolicitud>().AsNoTracking().FirstOrDefaultAsync(c => c.SolicitudId == s.Id && c.Estado == EstadoCobro.Habilitado, ct).ConfigureAwait(false);
        try
        {
            var cotizacion = await cotizador.CotizarAsync(s.TipoTramiteCodigo, s.OrganismoId, ct).ConfigureAwait(false);
            return Armar(s, puedePagar: true, null, [], cotizacion, null, CobroDe(cobro), null);
        }
        catch (ReglaDeNegocioException ex) when (ex.Codigo is CotizadorDelPunto.CodigoSinTarifa or CotizadorDelPunto.CodigoMedioNoDisponible)
        {
            return Armar(s, puedePagar: false, null, [], null, new BloqueoDePago(ex.Codigo, ex.Message), CobroDe(cobro), null);
        }
    }

    private static EstadoPagoPunto Armar(
        SolicitudTramite s, bool puedePagar, string? motivoIdentidad, IReadOnlyList<ParteFaltante> partes, CotizacionDto? cotizacion, BloqueoDePago? bloqueo,
        CobroEnCaja? cobro = null, PagoConfirmado? pago = null) =>
        new(s.Id, s.CodigoPublico, s.Estado, puedePagar, motivoIdentidad, partes, cotizacion?.Lineas, cotizacion?.TotalCop, bloqueo, cobro, pago);

    private CobroEnCaja? CobroDe(CobroSolicitud? c) =>
        c is { HabilitadoEn: { } habilitado, VenceEn: { } vence }
            ? new CobroEnCaja(c.Codigo, VigenciaDelCobro.EstaVigente(vence, reloj.GetUtcNow()) ? "vigente" : "vencido", habilitado, vence)
            : null;

    private static PagoConfirmado PagoDe(CobroSolicitud c) => new(c.ValorCop ?? 0, c.Medio ?? string.Empty, c.MedioCaja, c.PagadoEn ?? c.UpdatedAt);

    // ── Habilitar el cobro en caja ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Habilita (o renueva si venció) el cobro en caja: el código es el código público de la solicitud y vale hasta las 23:59:59
    /// hora de Colombia de hoy. Con el código aún vigente devuelve el mismo, sin moverlo. El total se recalcula en cada llamada.
    /// </summary>
    public async Task<CajaHabilitada> HabilitarCajaAsync(OrganismoPunto sesion, string? token, CancellationToken ct)
    {
        var s = await ResolverAsync(sesion, token, rastrear: false, ct).ConfigureAwait(false);
        if (await db.Set<CobroSolicitud>().AnyAsync(c => c.SolicitudId == s.Id && c.Estado == EstadoCobro.Pagado, ct).ConfigureAwait(false))
            throw RegistroDePagos.YaPagado();
        if (s.Estado != EstadoSolicitud.PendientePago)
            throw RegistroDePagos.Regla(StatusCodes.Status409Conflict, CodigoEstadoNoPermitePago, "El pago se habilita cuando el trámite está completo y todas las identidades están validadas.");

        var cotizacion = await cotizador.CotizarAsync(s.TipoTramiteCodigo, s.OrganismoId, ct).ConfigureAwait(false);
        if (cotizacion.TotalCop == 0)
            throw RegistroDePagos.Regla(StatusCodes.Status409Conflict, "total_cero", "El trámite no tiene valor a pagar; confirme el paso para continuar.");

        CobroSolicitud cobro;
        try
        {
            cobro = await HabilitarEnTransaccionAsync(s.Id, ct).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Dos «generar» a la vez: la perdedora lee el cobro que dejó la ganadora.
            cobro = await db.Set<CobroSolicitud>().AsNoTracking().SingleAsync(c => c.SolicitudId == s.Id && c.Estado == EstadoCobro.Habilitado, ct).ConfigureAwait(false);
        }

        return new CajaHabilitada(cobro.Codigo, cobro.HabilitadoEn!.Value, cobro.VenceEn!.Value, cotizacion.Lineas, cotizacion.TotalCop);
    }

    private Task<CobroSolicitud> HabilitarEnTransaccionAsync(Guid id, CancellationToken ct) =>
        Transaccion.EjecutarAsync(db, async ct2 =>
        {
            var s = await Transaccion.BloquearSolicitudAsync(db, id, ct2).ConfigureAwait(false) ?? throw SolicitudesService.NoEncontrada();
            if (s.Estado != EstadoSolicitud.PendientePago)
                throw RegistroDePagos.Regla(StatusCodes.Status409Conflict, CodigoEstadoNoPermitePago, "El trámite no está pendiente de pago.");

            var ahora = reloj.GetUtcNow();
            var cobro = await db.Set<CobroSolicitud>().FirstOrDefaultAsync(c => c.SolicitudId == id && c.Estado == EstadoCobro.Habilitado, ct2).ConfigureAwait(false);
            if (cobro is not null && VigenciaDelCobro.EstaVigente(cobro.VenceEn, ahora))
                return cobro;

            var nuevo = cobro is null;
            cobro ??= RegistroDePagos.NuevoCobro(s, ahora);
            cobro.HabilitadoEn = ahora;
            cobro.VenceEn = VigenciaDelCobro.FinDelDia(ahora);
            cobro.UpdatedAt = ahora;
            if (nuevo)
                db.Add(cobro);
            else
                cobro.RowVersion++;
            await db.SaveChangesAsync(ct2).ConfigureAwait(false);
            return cobro;
        }, ct);

    // ── Confirmar un trámite en $0 ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// La persona confirma el paso cuando el total es $0: queda pagada con valor 0 y medio <c>sin_cobro</c>. El total se recalcula
    /// aquí; con valor mayor que cero responde 409 <c>total_mayor_cero</c> y no queda pagada.
    /// </summary>
    public async Task<EstadoPagoPunto> ConfirmarSinCobroAsync(OrganismoPunto sesion, string? token, CancellationToken ct)
    {
        var s = await ResolverAsync(sesion, token, rastrear: false, ct).ConfigureAwait(false);
        await registro.RegistrarPagoAsync(new PedidoDePago(s.Id, MedioDePago.SinCobro, OrganismoEsperado: sesion.OrganismoId), ct).ConfigureAwait(false);
        return await EstadoAsync(sesion, token, ct).ConfigureAwait(false);
    }

    // ── Soporte ──────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>La solicitud de la sesión del trámite, del canal del punto y del organismo de la cookie.</summary>
    private async Task<SolicitudTramite> ResolverAsync(OrganismoPunto sesion, string? token, bool rastrear, CancellationToken ct)
    {
        var validada = await sesiones.ValidarAsync(token, ct).ConfigureAwait(false);
        if (validada.Estado != EstadoSesion.Valida)
            throw new ReglaDeNegocioException(StatusCodes.Status401Unauthorized, "session_invalid", "Sesión del trámite", "La sesión del trámite no es válida.");

        var consulta = rastrear ? db.Set<SolicitudTramite>() : db.Set<SolicitudTramite>().AsNoTracking();
        var s = await consulta.FirstOrDefaultAsync(x => x.Id == validada.SolicitudId, ct).ConfigureAwait(false)
            ?? throw new ReglaDeNegocioException(StatusCodes.Status404NotFound, "solicitud_no_encontrada", "No encontrado", "No encontramos ese trámite.");
        if (!SolicitudesService.EsCanalPunto(s.Datos))
            throw new ReglaDeNegocioException(StatusCodes.Status409Conflict, "no_es_canal_punto", "Canal", "Esta solicitud no se abrió en el punto de atención.");
        if (s.OrganismoId != sesion.OrganismoId)
            throw new ReglaDeNegocioException(StatusCodes.Status403Forbidden, CompletitudRequisitosPuntoService.CodigoOrganismoAjeno, "Organismo", "Este trámite pertenece a otro organismo.");
        return s;
    }
}
