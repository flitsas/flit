using Flit.PersonaNatural.Api.Persistence;
using Flit.PersonaNatural.Api.Sesiones;
using Flit.PersonaNatural.Api.Solicitudes;
using Microsoft.EntityFrameworkCore;

namespace Flit.PersonaNatural.Api.Radicacion;

/// <summary>Marca de las rutas que la sesión puede seguir usando cuando la solicitud ya no es editable (en cola o posterior).</summary>
public sealed class PermiteTrasRadicar
{
    public static readonly PermiteTrasRadicar Instancia = new();

    private PermiteTrasRadicar()
    {
    }
}

/// <summary>
/// Sesión de SOLO ESTADO tras radicar (hallazgo B3 de la revisión de seguridad, HU #14049). La sesión que el ciudadano usó para
/// llenar el trámite NO se revoca al radicar (el front necesita sondear el estado y reintentar la radicación con la misma
/// <c>Idempotency-Key</c> si perdió la respuesta), pero deja de servir para todo lo demás: en cuanto la solicitud sale de
/// <c>borrador</c>/<c>pendiente_validacion</c>, solo abren las rutas marcadas con <see cref="PermiteTrasRadicar"/>:
/// <c>GET /solicitudes/{id}</c> y <c>GET /solicitudes/{id}/estado</c> (que ya no devuelven datos personales) y el reintento
/// idempotente de <c>POST /radicar</c> (su respuesta es solo id, radicado y estado). Cualquier otra ruta responde 409
/// <c>estado_no_permite_cambio</c>, el mismo código que ya usaban las escrituras. Se aplica envolviendo el filtro de sesión
/// de la solicitud: primero se valida la sesión (y que sea de ESA solicitud) y solo entonces se mira el estado, para que
/// quien no tiene sesión no pueda averiguar el estado de una solicitud por la diferencia de respuestas.
/// </summary>
public static class SesionTrasRadicar
{
    public static ValueTask<object?> Filtro(EndpointFilterInvocationContext contexto, EndpointFilterDelegate siguiente)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        ArgumentNullException.ThrowIfNull(siguiente);
        return SesionEndpoints.ConSesionDeLaSolicitud(contexto, async c =>
        {
            var http = c.HttpContext;
            if (http.GetEndpoint()?.Metadata.GetMetadata<PermiteTrasRadicar>() is null)
            {
                var id = c.Arguments.OfType<Guid>().FirstOrDefault();
                var db = http.RequestServices.GetRequiredService<PersonaNaturalDb>();
                var estado = await db.Set<SolicitudTramite>().AsNoTracking().Where(s => s.Id == id).Select(s => s.Estado)
                    .FirstOrDefaultAsync(http.RequestAborted).ConfigureAwait(false);
                if (estado is not null && !SolicitudesService.EsEditable(estado))
                {
                    return SesionEndpoints.Problema(
                        StatusCodes.Status409Conflict, "No se puede cambiar", "Este trámite ya fue enviado o cancelado y no admite cambios.", SolicitudesService.CodigoNoPermiteCambio);
                }
            }

            return await siguiente(c).ConfigureAwait(false);
        });
    }
}
