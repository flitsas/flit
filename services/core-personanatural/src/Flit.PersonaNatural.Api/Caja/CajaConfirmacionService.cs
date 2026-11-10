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
    PersonaNaturalDb db, IRegistroDePagos registro, AdjuntosService adjuntos, CajaAuditoria auditoria, IOptions<CajaOptions> opciones)
{
    public const int MaxDetalle = 120;
    public const string CodigoMedioInvalido = "medio_invalido";
    public const string CodigoDetalleRequerido = "detalle_requerido";
    public const string CodigoLlaveRequerida = "idempotency_key_requerida";
    public const int MaxLlave = 100;

    public async Task<CobroConfirmado> ConfirmarAsync(Cajero cajero, PedidoDeConfirmacion pedido, string? ip, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(cajero);
        ArgumentNullException.ThrowIfNull(pedido);
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
            : await db.Set<SolicitudTramite>().AsNoTracking().Where(x => x.CodigoPublico == codigo).Select(x => new { x.Id, x.OrganismoId, x.Datos }).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (previa is null || !SolicitudesService.EsCanalPunto(previa.Datos))
            throw Regla(404, CajaBusquedaService.CodigoNoEncontrado, "No encontramos ese trámite.");
        if (previa.OrganismoId != cajero.OrganismoId)
            throw Regla(403, CajaBusquedaService.CodigoOtroOrganismo, "Este trámite no es de este organismo.");

        // Reintento de la MISMA confirmación (misma clave, mismo usuario): el mismo resultado, sin cobrar otra vez.
        if (await RepeticionAsync(cajero, previa.Id, llave, ct).ConfigureAwait(false) is { } repetido)
            return repetido;

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
            if (ex.Codigo == "ya_pagado" && await RepeticionAsync(cajero, previa.Id, llave, ct).ConfigureAwait(false) is { } ganador)
                return ganador;
            await auditoria.RegistrarAsync(cajero, EventoCaja.AccionConfirmar, codigo, ex.Codigo, ip, ct).ConfigureAwait(false);
            throw;
        }

        await auditoria.RegistrarAsync(cajero, EventoCaja.AccionConfirmar, codigo, "ok", ip, ct).ConfigureAwait(false);
        return new CobroConfirmado(codigo!, pagado.ValorCop, medio, detalle, pagado.PagadoEn);
    }

    private async Task<CobroConfirmado?> RepeticionAsync(Cajero cajero, Guid solicitudId, string llave, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var c = await db.Set<CobroSolicitud>().AsNoTracking().FirstOrDefaultAsync(x =>
            x.SolicitudId == solicitudId && x.Estado == EstadoCobro.Pagado && x.LlaveConfirmacion == llave && x.PagadoPorId == cajero.UsuarioId && x.OrganismoId == cajero.OrganismoId, ct).ConfigureAwait(false);
        return c is null ? null : new CobroConfirmado(c.Codigo, c.ValorCop ?? 0, c.MedioCaja ?? string.Empty, c.MedioDetalle, c.PagadoEn ?? c.UpdatedAt);
    }

    private static ReglaDeNegocioException Regla(int http, string codigo, string mensaje) => new(http, codigo, "No se puede confirmar", mensaje);
}
