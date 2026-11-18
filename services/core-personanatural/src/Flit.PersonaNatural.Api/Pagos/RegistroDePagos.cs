using Flit.PersonaNatural.Api.Caja;
using Flit.PersonaNatural.Api.Cotizaciones;
using Flit.PersonaNatural.Api.Persistence;
using Flit.PersonaNatural.Api.Radicacion;
using Flit.PersonaNatural.Api.Solicitudes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Flit.PersonaNatural.Api.Pagos;

/// <summary>
/// Lo que se pide para dejar una solicitud del punto pagada. NO lleva valor: el servidor recalcula el total al confirmar.
/// <paramref name="OrganismoEsperado"/> (opcional) hace que un pago pedido desde otro organismo se rechace con 403.
/// </summary>
public sealed record PedidoDePago(
    Guid SolicitudId,
    string Medio,
    string? MedioCaja = null,
    string? MedioDetalle = null,
    Guid? PrefacturaAdjuntoId = null,
    string? PagadoPor = null,
    Guid? OrganismoEsperado = null,
    Guid? PagadoPorId = null,
    string? PagadoPorNombre = null,
    string? LlaveConfirmacion = null,
    long? TotalVistoCop = null,
    int? VentanaAnulacionMinutos = null);

/// <summary>Resultado de un pago confirmado. <see cref="EstadoSolicitud"/> es <c>en_cola</c> si la radicación ya se disparó.</summary>
public sealed record PagoRegistrado(
    Guid CobroId, Guid SolicitudId, string CodigoPublico, long ValorCop, string Medio, DateTimeOffset PagadoEn, string EstadoSolicitud, DateTimeOffset? RadicaEn = null);

/// <summary>
/// Transición única a «pagada» (Feature #14447). La usan el pago en caja y la confirmación en $0, y la usará toda forma de pago
/// futura (la pasarela se suma como otro medio sin rehacerla).
/// </summary>
public interface IRegistroDePagos
{
    /// <exception cref="ReglaDeNegocioException">
    /// 400 <c>medio_invalido</c>, <c>medio_caja_invalido</c>, <c>detalle_requerido</c>; 403 <c>otro_organismo</c>; 404 <c>solicitud_no_encontrada</c>;
    /// 409 <c>ya_pagado</c>, <c>no_es_canal_punto</c>, <c>estado_no_permite_pago</c>, <c>no_habilitado</c>, <c>vencido</c>, <c>total_mayor_cero</c>,
    /// <c>total_cero</c>, <c>sin_tarifa</c>, <c>medio_no_disponible</c>.
    /// </exception>
    Task<PagoRegistrado> RegistrarPagoAsync(PedidoDePago pedido, CancellationToken ct);
}

/// <summary>
/// Implementación de <see cref="IRegistroDePagos"/>. Garantías:
/// <list type="bullet">
///   <item>El total sale SIEMPRE de una cotización hecha aquí; el cliente no envía el valor.</item>
///   <item>Un solo ganador: la fila de la solicitud se bloquea (<c>FOR UPDATE</c>), se revisa de nuevo el estado y un índice único
///   parcial (<c>status = 'pagado'</c>) cierra cualquier carrera restante. El perdedor recibe 409 <c>ya_pagado</c>.</item>
///   <item>El cobro, el cambio de estado y el historial salen en UNA transacción.</item>
///   <item>Tras confirmar se dispara la radicación (<see cref="RadicacionService.IntentarRadicarPagadaAsync"/>). Si falla, el pago
///   sigue válido y la solicitud queda <c>pagada</c> para reintentar; el pago nunca se deshace por eso.</item>
/// </list>
/// </summary>
public sealed class RegistroDePagos(
    PersonaNaturalDb db, CotizadorDelPunto cotizador, RadicacionService radicacion, TimeProvider reloj, IOptions<CajaOptions> caja) : IRegistroDePagos
{
    public const string IndicePagado = "uq_payment_charge_paid_per_request";
    public const int MaxDetalle = 200;

    public async Task<PagoRegistrado> RegistrarPagoAsync(PedidoDePago pedido, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(pedido);
        Validar(pedido);

        // Lectura previa sin bloqueo: descarta rápido lo ya decidido; el total se cotiza dentro de la transacción.
        var previa = await db.Set<SolicitudTramite>().AsNoTracking().Where(s => s.Id == pedido.SolicitudId)
            .Select(s => new { s.Estado, s.Datos, s.OrganismoId, s.TipoTramiteCodigo }).FirstOrDefaultAsync(ct).ConfigureAwait(false)
            ?? throw SolicitudesService.NoEncontrada();
        if (!SolicitudesService.EsCanalPunto(previa.Datos))
            throw Regla(StatusCodes.Status409Conflict, "no_es_canal_punto", "Esta solicitud no se abrió en el punto de atención.");
        if (pedido.OrganismoEsperado is { } esperado && esperado != previa.OrganismoId)
            throw Regla(StatusCodes.Status403Forbidden, "otro_organismo", "Este trámite no es de este organismo.");
        if (await db.Set<CobroSolicitud>().AnyAsync(c => c.SolicitudId == pedido.SolicitudId && c.Estado == EstadoCobro.Pagado, ct).ConfigureAwait(false))
            throw YaPagado();
        if (previa.Estado != EstadoSolicitud.PendientePago)
            throw EstadoNoPermitePago();

        var pagado = await ConfirmarEnTransaccionAsync(pedido, previa.TipoTramiteCodigo, ct).ConfigureAwait(false);

        // El pago ya está firme. Un cobro en caja espera la ventana de anulación (Feature #14453) y lo radica el trabajo de fondo; un
        // total $0 (o la ventana en cero) se radica ya. Si no se logra ahora, la solicitud queda «pagada» y se reintenta.
        var ventana = VentanaDe(pedido);
        if (pedido.Medio == MedioDePago.Caja && ventana > TimeSpan.Zero)
            return pagado with { RadicaEn = pagado.PagadoEn + ventana };

        var radicada = await radicacion.IntentarRadicarPagadaAsync(pagado.SolicitudId, ct).ConfigureAwait(false);
        return radicada ? pagado with { EstadoSolicitud = EstadoSolicitud.EnCola } : pagado;
    }

    private async Task<PagoRegistrado> ConfirmarEnTransaccionAsync(PedidoDePago pedido, string tipoCodigo, CancellationToken ct)
    {
        try
        {
            return await Transaccion.EjecutarAsync(db, async ct2 =>
            {
                var s = await Transaccion.BloquearSolicitudAsync(db, pedido.SolicitudId, ct2).ConfigureAwait(false) ?? throw SolicitudesService.NoEncontrada();
                var cobros = await db.Set<CobroSolicitud>().Where(c => c.SolicitudId == s.Id && c.Estado != EstadoCobro.Anulado).ToListAsync(ct2).ConfigureAwait(false);
                if (cobros.Any(c => c.Estado == EstadoCobro.Pagado))
                    throw YaPagado();
                if (s.Estado != EstadoSolicitud.PendientePago)
                    throw EstadoNoPermitePago();

                // Se cotiza AQUÍ, con la fila bloqueada y el estado revalidado: se cobra lo vigente al confirmar. Si la
                // cotización falla, la excepción sale de la transacción y no se registra nada.
                var cotizacion = await cotizador.CotizarAsync(tipoCodigo, s.OrganismoId, ct2).ConfigureAwait(false);
                if (pedido.Medio == MedioDePago.SinCobro && cotizacion.TotalCop != 0)
                    throw Regla(StatusCodes.Status409Conflict, "total_mayor_cero", "El trámite tiene un valor a pagar; debe pagarse en caja.");
                if (pedido.Medio == MedioDePago.Caja && cotizacion.TotalCop == 0)
                    throw Regla(StatusCodes.Status409Conflict, "total_cero", "El trámite no tiene valor a pagar; se confirma sin pasar por caja.");
                if (pedido.TotalVistoCop is { } visto && visto != cotizacion.TotalCop)
                {
                    throw new ReglaDeNegocioException(
                        StatusCodes.Status409Conflict, "total_cambio", "El total cambió", "El total cambió; revise el nuevo valor.",
                        new Dictionary<string, object?> { ["totalActualCop"] = cotizacion.TotalCop });
                }

                var ahora = reloj.GetUtcNow();
                var habilitado = cobros.FirstOrDefault(c => c.Estado == EstadoCobro.Habilitado);
                if (pedido.Medio == MedioDePago.Caja)
                {
                    if (habilitado is null)
                        throw Regla(StatusCodes.Status409Conflict, "no_habilitado", "El pago en caja de este trámite no está habilitado.");
                    if (!VigenciaDelCobro.EstaVigente(habilitado.VenceEn, ahora))
                        throw Regla(StatusCodes.Status409Conflict, "vencido", "El pago en caja de este trámite venció.");
                }

                var cobro = habilitado ?? NuevoCobro(s, ahora);
                cobro.Estado = EstadoCobro.Pagado;
                cobro.ValorCop = cotizacion.TotalCop;
                cobro.Medio = pedido.Medio;
                cobro.MedioCaja = pedido.Medio == MedioDePago.Caja ? pedido.MedioCaja : null;
                cobro.MedioDetalle = pedido.Medio == MedioDePago.Caja && pedido.MedioCaja == MedioDeCaja.Otro ? pedido.MedioDetalle!.Trim() : null;
                cobro.PrefacturaAdjuntoId = pedido.PrefacturaAdjuntoId;
                cobro.PagadoPor = pedido.PagadoPor;
                cobro.PagadoPorId = pedido.PagadoPorId;
                cobro.PagadoPorNombre = pedido.PagadoPorNombre;
                cobro.LlaveConfirmacion = pedido.LlaveConfirmacion;
                cobro.PagadoEn = ahora;
                cobro.PagadoEnDia = VigenciaDelCobro.DiaColombia(ahora);
                // El límite de anulación se fija AQUÍ y no cambia aunque el organismo mueva su ventana después (solo aplica a cobros nuevos).
                cobro.AnulableHasta = pedido.Medio == MedioDePago.Caja ? ahora + VentanaDe(pedido) : null;
                cobro.Lineas = CotizadorDelPunto.SerializarLineas(cotizacion.Lineas);
                cobro.VersionTarifa = cotizacion.VersionTarifa;
                cobro.UpdatedAt = ahora;
                cobro.RowVersion++;
                if (habilitado is null)
                    db.Add(cobro);

                db.Add(new HistorialEstado
                {
                    Id = Guid.CreateVersion7(), SolicitudId = s.Id, Desde = s.Estado, Hasta = EstadoSolicitud.Pagada, Origen = "bff", Motivo = $"pagada_{pedido.Medio}", OcurrioEn = ahora,
                });
                s.Estado = EstadoSolicitud.Pagada;
                s.RowVersion++;
                s.UpdatedAt = ahora;
                await db.SaveChangesAsync(ct2).ConfigureAwait(false);
                return new PagoRegistrado(cobro.Id, s.Id, s.CodigoPublico, cotizacion.TotalCop, pedido.Medio, ahora, s.Estado);
            }, ct).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: IndicePagado })
        {
            throw YaPagado();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw YaPagado();
        }
    }

    /// <summary>
    /// Ventana de anulación de ESTE cobro: la del organismo que mandó core-api (acotada a 0..24 h por seguridad) o, si no la mandó (core-api
    /// anterior), la global. Con la caja apagada es 0 (ver <see cref="CajaOptions.Ventana"/>).
    /// </summary>
    private TimeSpan VentanaDe(PedidoDePago pedido) =>
        pedido.VentanaAnulacionMinutos is { } minutos && caja.Value.Habilitado
            ? TimeSpan.FromMinutes(Math.Clamp(minutos, 0, MaxVentanaMinutos))
            : caja.Value.Ventana;

    private const int MaxVentanaMinutos = 24 * 60;

    internal static CobroSolicitud NuevoCobro(SolicitudTramite s, DateTimeOffset ahora) => new()
    {
        Id = Guid.CreateVersion7(), SolicitudId = s.Id, OrganismoId = s.OrganismoId, Codigo = s.CodigoPublico, Estado = EstadoCobro.Habilitado, CreatedAt = ahora, UpdatedAt = ahora,
    };

    private static void Validar(PedidoDePago pedido)
    {
        if (!MedioDePago.Todos.Contains(pedido.Medio))
            throw Regla(StatusCodes.Status400BadRequest, "medio_invalido", "Indique cómo se paga el trámite.");
        if (pedido.Medio == MedioDePago.SinCobro)
            return;
        if (pedido.MedioCaja is null || !MedioDeCaja.Todos.Contains(pedido.MedioCaja))
            throw Regla(StatusCodes.Status400BadRequest, "medio_caja_invalido", "Indique si se cobró en efectivo, datáfono, transferencia u otro.");
        if (pedido.MedioCaja == MedioDeCaja.Otro && (string.IsNullOrWhiteSpace(pedido.MedioDetalle) || pedido.MedioDetalle.Trim().Length > MaxDetalle))
            throw Regla(StatusCodes.Status400BadRequest, "detalle_requerido", $"Describa el medio de pago en máximo {MaxDetalle} caracteres.");
    }

    internal static ReglaDeNegocioException YaPagado() => Regla(StatusCodes.Status409Conflict, "ya_pagado", "Este trámite ya fue pagado.");

    private static ReglaDeNegocioException EstadoNoPermitePago() =>
        Regla(StatusCodes.Status409Conflict, "estado_no_permite_pago", "El trámite no está pendiente de pago.");

    internal static ReglaDeNegocioException Regla(int estado, string codigo, string detalle) => new(estado, codigo, "No se puede pagar", detalle);
}

/// <summary>Transacción con la estrategia de ejecución de Npgsql y bloqueo de fila de la solicitud.</summary>
internal static class Transaccion
{
    public static Task<T> EjecutarAsync<T>(PersonaNaturalDb db, Func<CancellationToken, Task<T>> trabajo, CancellationToken ct) =>
        db.Database.CreateExecutionStrategy().ExecuteAsync(async ct2 =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct2).ConfigureAwait(false);
            var resultado = await trabajo(ct2).ConfigureAwait(false);
            await tx.CommitAsync(ct2).ConfigureAwait(false);
            return resultado;
        }, ct);

    /// <summary>Lee la solicitud con <c>FOR UPDATE</c>: otra transacción que pida la misma fila espera a que esta termine.</summary>
    public static async Task<SolicitudTramite?> BloquearSolicitudAsync(PersonaNaturalDb db, Guid id, CancellationToken ct) =>
        (await db.Set<SolicitudTramite>()
            .FromSqlInterpolated($"SELECT * FROM personanatural.procedure_request WHERE id = {id} FOR UPDATE")
            .ToListAsync(ct).ConfigureAwait(false)).FirstOrDefault();
}
