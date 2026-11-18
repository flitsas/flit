using Flit.PersonaNatural.Api.Pagos;
using Flit.PersonaNatural.Api.Persistence;
using Flit.PersonaNatural.Api.Sesiones;
using Flit.PersonaNatural.Api.Solicitudes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Flit.PersonaNatural.Api.Caja;

/// <summary>
/// Anular un cobro en caja desde el hub del OT (HU #14461, lado BFF). Solo si el cobro es del organismo indicado, sigue
/// <c>pagado</c>, la solicitud sigue <c>pagada</c> (aún no se radicó) y la ventana de anulación del cobro (<c>annulable_until</c>, o la global en cobros previos) no venció. Entonces el cobro pasa a
/// <c>anulado</c> con motivo y quién lo anuló, y la solicitud vuelve a <c>pendiente_pago</c>. Si no: <c>cobro_no_anulable</c>
/// (<c>ya_en_flit2</c>) y no se cambia nada.
/// </summary>
public sealed class CajaAnulacionService(PersonaNaturalDb db, IOptions<CajaOptions> opciones, PiiHasher hasher, TimeProvider reloj, ICorreoDeAnulacion correoAnulacion)
{
    internal static readonly TimeSpan TopeCorreoAnulacion = TimeSpan.FromSeconds(3);

    public const string CodigoNoAnulable = "cobro_no_anulable";
    public const string CodigoNoEncontrado = "cobro_no_encontrado";
    public const string CodigoMotivoInvalido = "motivo_invalido";
    public const string CodigoVersionRequerida = "row_version_requerido";
    public const string MotivoYaEnFlit2 = "ya_en_flit2";
    public const int MotivoMin = 10;
    public const int MotivoMax = 300;

    public async Task<CobroSolicitud> AnularAsync(
        Guid organismoId, Guid cobroId, string? motivo, Guid actorId, string? actorNombre, long rowVersion, CancellationToken ct)
    {
        motivo = motivo?.Trim();
        if (string.IsNullOrEmpty(motivo) || motivo.Length < MotivoMin || motivo.Length > MotivoMax)
            throw Regla(400, CodigoMotivoInvalido, $"El motivo debe tener entre {MotivoMin} y {MotivoMax} caracteres.");
        if (rowVersion <= 0)
            throw Regla(400, CodigoVersionRequerida, "Falta la versión del cobro que está revisando. Actualice la lista.");
        if (actorId == Guid.Empty)
            throw Regla(400, "actor_invalido", "Falta quién anula.");
        var nombre = (actorNombre ?? string.Empty).Trim();
        if (nombre.Length > 160)
            nombre = nombre[..160];

        var previo = await db.Set<CobroSolicitud>().AsNoTracking().Where(c => c.Id == cobroId && c.OrganismoId == organismoId)
            .Select(c => new { c.SolicitudId }).FirstOrDefaultAsync(ct).ConfigureAwait(false)
            ?? throw Regla(404, CodigoNoEncontrado, "No encontramos ese cobro.");

        CobroSolicitud anulado;
        try
        {
            anulado = await Transaccion.EjecutarAsync(db, async ct2 =>
            {
                var s = await Transaccion.BloquearSolicitudAsync(db, previo.SolicitudId, ct2).ConfigureAwait(false) ?? throw Regla(404, CodigoNoEncontrado, "No encontramos ese cobro.");
                var cobro = await db.Set<CobroSolicitud>().FirstOrDefaultAsync(c => c.Id == cobroId && c.OrganismoId == organismoId, ct2).ConfigureAwait(false)
                    ?? throw Regla(404, CodigoNoEncontrado, "No encontramos ese cobro.");

                var ahora = reloj.GetUtcNow();
                var (anulable, razon) = CajaListaService.Evaluar(cobro.Estado, s.Estado, cobro.PagadoEn, cobro.AnulableHasta, ahora, opciones.Value.Ventana);
                if (!anulable || cobro.Medio != MedioDePago.Caja)
                    throw NoAnulable(razon ?? CajaListaService.MotivoYaEnFlit2);
                if (rowVersion != cobro.RowVersion)
                    throw NoAnulable("El cobro cambió mientras lo revisaba. Actualice la lista.");

                cobro.Estado = EstadoCobro.Anulado;
                cobro.AnuladoPorId = actorId;
                cobro.AnuladoPorNombre = nombre.Length == 0 ? null : nombre;
                cobro.AnuladoPor = actorId.ToString();
                cobro.AnuladoEn = ahora;
                cobro.AnulacionMotivo = motivo;
                cobro.UpdatedAt = ahora;
                cobro.RowVersion++;

                db.Add(new HistorialEstado { Id = Guid.CreateVersion7(), SolicitudId = s.Id, Desde = s.Estado, Hasta = EstadoSolicitud.PendientePago, Origen = "bff", Motivo = "pago_anulado", OcurrioEn = ahora });
                s.Estado = EstadoSolicitud.PendientePago;
                s.RowVersion++;
                s.UpdatedAt = ahora;
                db.Add(CajaAuditoria.Nuevo(organismoId, actorId, EventoCaja.AccionAnular, cobro.Codigo, "ok", null, ahora, hasher));
                await db.SaveChangesAsync(ct2).ConfigureAwait(false);
                return cobro;
            }, ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw NoAnulable("El cobro cambió mientras lo revisaba. Actualice la lista.");
        }

        // Fuera de la transacción y con tope: un fallo o una demora del correo nunca deshace la anulación. Solo la anulación que ganó llega aquí.
        using var tope = CancellationTokenSource.CreateLinkedTokenSource(ct);
        tope.CancelAfter(TopeCorreoAnulacion);
        try
        {
            await correoAnulacion.EnviarSiCorrespondeAsync(anulado.Id, tope.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Venció el tope: la anulación ya es firme.
        }

        return anulado;
    }

    private static ReglaDeNegocioException NoAnulable(string detalle) =>
        new(StatusCodes.Status409Conflict, CodigoNoAnulable, "No se puede anular", detalle, new Dictionary<string, object?> { ["motivo"] = MotivoYaEnFlit2 });

    private static ReglaDeNegocioException Regla(int http, string codigo, string detalle) => new(http, codigo, "No se puede anular", detalle);
}
