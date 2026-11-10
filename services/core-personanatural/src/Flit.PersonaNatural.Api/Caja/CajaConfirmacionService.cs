using Flit.PersonaNatural.Api.Adjuntos;
using Flit.PersonaNatural.Api.Pagos;
using Flit.PersonaNatural.Api.Persistence;
using Flit.PersonaNatural.Api.Solicitudes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Flit.PersonaNatural.Api.Caja;

/// <summary>Lo que la caja envía al confirmar. <see cref="TotalVistoCop"/> solo se COMPARA: el servidor cobra siempre su propio total.</summary>
public sealed record PedidoDeConfirmacion(string? Codigo, string? Medio, string? MedioDetalle, long? TotalVistoCop, string? Llave, string? PrefacturaNombre, byte[]? Prefactura);

/// <summary>
/// Confirmar el cobro en caja (HU #14457) con el medio obligatorio y la prefactura opcional. Usa <see cref="IRegistroDePagos"/>:
/// recotiza dentro de la transacción y gana el primero (el otro recibe <c>ya_pagado</c>). Registra quién cobró.
/// </summary>
public sealed class CajaConfirmacionService(
    PersonaNaturalDb db, IRegistroDePagos registro, AdjuntosService adjuntos, CajaAuditoria auditoria, IOptions<CajaOptions> opciones,
    CajaTopeBusquedas tope, TimeProvider reloj)
{
    public const string CodigoLlaveReutilizada = "idempotency_key_reutilizada";

    public const int MaxDetalle = 120;
    public const string CodigoMedioInvalido = "medio_invalido";
    public const string CodigoDetalleRequerido = "detalle_requerido";
    public const string CodigoLlaveRequerida = "idempotency_key_requerida";
    public const int MaxLlave = 100;

    public async Task<CobroConfirmado> ConfirmarAsync(Cajero cajero, PedidoDeConfirmacion pedido, string? ip, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(cajero);
        ArgumentNullException.ThrowIfNull(pedido);

        // El mismo tope de búsquedas sin resultado que GET /tramites/{codigo}: confirmar con códigos adivinados es otra forma de enumerar IDs.
        var reserva = await tope.ReservarAsync(cajero, ip, ct).ConfigureAwait(false);
        try
        {
            return await ConfirmarConReservaAsync(cajero, pedido, ip, reserva, ct).ConfigureAwait(false);
        }
        finally
        {
            // Si el código existía y era de este organismo la reserva sigue «pendiente» y se libera; si era una búsqueda sin resultado ya cuenta.
            await tope.LiberarAsync(reserva, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task<CobroConfirmado> ConfirmarConReservaAsync(Cajero cajero, PedidoDeConfirmacion pedido, string? ip, Guid reserva, CancellationToken ct)
    {
        var llave = pedido.Llave?.Trim();
        if (string.IsNullOrEmpty(llave) || llave.Length > MaxLlave)
            throw Regla(400, CodigoLlaveRequerida, "Falta la clave de la operación.");

        var medio = (pedido.Medio ?? string.Empty).Trim().ToLowerInvariant();
        if (!MedioDeCaja.Todos.Contains(medio))
            throw Regla(400, CodigoMedioInvalido, "Indique si se cobró en efectivo, datáfono, transferencia u otro.");
        var detalle = pedido.MedioDetalle?.Trim();
        if (medio == MedioDeCaja.Otro && (string.IsNullOrEmpty(detalle) || detalle.Length > MaxDetalle))
            throw Regla(400, CodigoDetalleRequerido, $"Describa el medio de pago en máximo {MaxDetalle} caracteres.");
        if (medio != MedioDeCaja.Otro)
            detalle = null;
        var max = opciones.Value.PrefacturaMaxBytes;
        if (pedido.Prefactura is { } adjunto && adjunto.LongLength > max)
            throw Regla(400, AdjuntosService.CodigoPrefacturaInvalida, $"La prefactura supera los {max / (1024 * 1024)} MB permitidos.");

        var codigo = CajaBusquedaService.Normalizar(pedido.Codigo);
        var previa = codigo is null
            ? null
            : await db.Set<SolicitudTramite>().AsNoTracking().Where(x => x.CodigoPublico == codigo).Select(x => new { x.Id, x.OrganismoId, x.Datos, x.Estado }).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (previa is null || !SolicitudesService.EsCanalPunto(previa.Datos))
        {
            await tope.ResolverAsync(reserva, EventoCaja.AccionBusquedaFallida, codigo, CajaBusquedaService.CodigoNoEncontrado, ct).ConfigureAwait(false);
            throw Regla(404, CajaBusquedaService.CodigoNoEncontrado, "No encontramos ese trámite.");
        }

        if (previa.OrganismoId != cajero.OrganismoId)
        {
            await tope.ResolverAsync(reserva, EventoCaja.AccionBusquedaFallida, codigo, CajaBusquedaService.CodigoOtroOrganismo, ct).ConfigureAwait(false);
            throw Regla(403, CajaBusquedaService.CodigoOtroOrganismo, "Este trámite no es de este organismo.");
        }

        await tope.LiberarAsync(reserva, ct).ConfigureAwait(false);

        // Reintento de la MISMA confirmación (misma clave, mismo usuario): el mismo resultado, sin cobrar otra vez.
        if (await RepeticionAsync(cajero, previa.Id, llave, medio, detalle, pedido.TotalVistoCop, ct).ConfigureAwait(false) is { } repetido)
            return repetido;

        // El cobro debe estar habilitado, vigente y sin pagar ANTES de guardar la prefactura: así una confirmación que va a fallar
        // no deja un archivo huérfano (con datos personales) en el almacenamiento.
        try
        {
            await ValidarCobroAsync(previa.Id, previa.Estado, ct).ConfigureAwait(false);
        }
        catch (ReglaDeNegocioException ex)
        {
            if (ex.Codigo == "ya_pagado" && await RepeticionAsync(cajero, previa.Id, llave, medio, detalle, pedido.TotalVistoCop, ct).ConfigureAwait(false) is { } ganador)
                return ganador;
            await auditoria.RegistrarAsync(cajero, EventoCaja.AccionConfirmar, codigo, ex.Codigo, ip, ct).ConfigureAwait(false);
            throw;
        }

        Guid? prefacturaId = null;
        if (pedido.Prefactura is { } archivo)
            prefacturaId = await adjuntos.GuardarPrefacturaAsync(previa.Id, pedido.PrefacturaNombre, archivo, max, ct).ConfigureAwait(false);

        PagoRegistrado pagado;
        try
        {
            pagado = await registro.RegistrarPagoAsync(
                new PedidoDePago(
                    previa.Id, MedioDePago.Caja, medio, detalle, prefacturaId, cajero.UsuarioId.ToString(), cajero.OrganismoId,
                    cajero.UsuarioId, cajero.Nombre, llave, pedido.TotalVistoCop), ct).ConfigureAwait(false);
        }
        catch (ReglaDeNegocioException ex)
        {
            if (prefacturaId is { } descartar)
                await adjuntos.DescartarPrefacturaAsync(descartar, CancellationToken.None).ConfigureAwait(false);
            // Dos pulsaciones del mismo botón que llegan a la vez: la perdedora encuentra el cobro de la ganadora con su misma clave.
            if (ex.Codigo == "ya_pagado" && await RepeticionAsync(cajero, previa.Id, llave, medio, detalle, pedido.TotalVistoCop, ct).ConfigureAwait(false) is { } ganador)
                return ganador;
            await auditoria.RegistrarAsync(cajero, EventoCaja.AccionConfirmar, codigo, ex.Codigo, ip, ct).ConfigureAwait(false);
            throw;
        }

        await auditoria.RegistrarAsync(cajero, EventoCaja.AccionConfirmar, codigo, "ok", ip, ct).ConfigureAwait(false);
        return new CobroConfirmado(codigo!, pagado.ValorCop, medio, detalle, pagado.PagadoEn);
    }

    /// <summary>Lectura previa (sin bloqueo; la transacción del pago la repite con la fila bloqueada): no pagado, pendiente de pago, habilitado y vigente.</summary>
    private async Task ValidarCobroAsync(Guid solicitudId, string estado, CancellationToken ct)
    {
        var cobros = await db.Set<CobroSolicitud>().AsNoTracking().Where(c => c.SolicitudId == solicitudId && c.Estado != EstadoCobro.Anulado)
            .Select(c => new { c.Estado, c.VenceEn }).ToListAsync(ct).ConfigureAwait(false);
        if (cobros.Any(c => c.Estado == EstadoCobro.Pagado))
            throw RegistroDePagos.YaPagado();
        if (estado != EstadoSolicitud.PendientePago)
            throw RegistroDePagos.Regla(StatusCodes.Status409Conflict, "estado_no_permite_pago", "El trámite no está pendiente de pago.");
        var habilitado = cobros.FirstOrDefault(c => c.Estado == EstadoCobro.Habilitado)
            ?? throw RegistroDePagos.Regla(StatusCodes.Status409Conflict, "no_habilitado", "El pago en caja de este trámite no está habilitado.");
        if (!VigenciaDelCobro.EstaVigente(habilitado.VenceEn, reloj.GetUtcNow()))
            throw RegistroDePagos.Regla(StatusCodes.Status409Conflict, "vencido", "El pago en caja de este trámite venció.");
    }

    /// <summary>
    /// Si ya hay un cobro pagado con esta clave, este usuario y esta solicitud, es la misma confirmación repetida: se devuelve su resultado. Pero si
    /// la clave se reutiliza con OTRO medio u OTRO total visto es una operación distinta con la clave equivocada: 409 <c>idempotency_key_reutilizada</c>.
    /// </summary>
    private async Task<CobroConfirmado?> RepeticionAsync(Cajero cajero, Guid solicitudId, string llave, string medio, string? detalle, long? totalVisto, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var c = await db.Set<CobroSolicitud>().AsNoTracking().FirstOrDefaultAsync(x =>
            x.SolicitudId == solicitudId && x.Estado == EstadoCobro.Pagado && x.LlaveConfirmacion == llave && x.PagadoPorId == cajero.UsuarioId && x.OrganismoId == cajero.OrganismoId, ct).ConfigureAwait(false);
        if (c is null)
            return null;
        if (!string.Equals(c.MedioCaja, medio, StringComparison.Ordinal)
            || (medio == MedioDeCaja.Otro && !string.Equals(c.MedioDetalle, detalle, StringComparison.Ordinal))
            || (totalVisto is { } visto && visto != c.ValorCop))
        {
            throw Regla(409, CodigoLlaveReutilizada, "Esa clave de operación ya se usó con otro medio u otro total.");
        }

        return new CobroConfirmado(c.Codigo, c.ValorCop ?? 0, c.MedioCaja ?? string.Empty, c.MedioDetalle, c.PagadoEn ?? c.UpdatedAt);
    }

    private static ReglaDeNegocioException Regla(int http, string codigo, string mensaje) => new(http, codigo, "No se puede confirmar", mensaje);
}
