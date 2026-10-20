using Flit.Comparendos.Api.Modulos.Comparendos;
using Flit.Comparendos.Api.Modulos.Correo;
using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Persistence.Modelo;
using Flit.Comparendos.Domain.Modelo;
using Microsoft.EntityFrameworkCore;

namespace Flit.Comparendos.Api.Modulos.Avisos;

/// <summary>
/// Un aviso 1 a 3 que no llegó no cuenta como enviado (§4.5, RN-114): el estado del conductor vuelve al último aviso que
/// sí salió. FALLIDO libera el aviso para que el scheduler lo reintente; REBOTADO lo deja registrado (no se insiste a
/// una dirección que rebota: hay que corregir el correo del conductor, y el nuevo correo recibe su propio aviso).
/// </summary>
internal sealed class ReaccionAvisos(ComparendosDb db, Timeline timeline) : IReaccionCorreo
{
    private static readonly TipoComunicacion[] Tipos = [TipoComunicacion.Aviso1, TipoComunicacion.Aviso2, TipoComunicacion.Aviso3];

    public async Task AlTerminarAsync(ComunicacionSaliente comunicacion, ResultadoEnvioCorreo resultado, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(comunicacion);
        ArgumentNullException.ThrowIfNull(resultado);
        if (!Tipos.Contains(comunicacion.Tipo) || comunicacion.Estado is not (EstadoComunicacion.Fallido or EstadoComunicacion.Rebotado))
            return;
        var aviso = await db.AvisosConductor.FirstOrDefaultAsync(a => a.ComunicacionId == comunicacion.Id, ct).ConfigureAwait(false);
        if (aviso is null)
            return;
        var c = await db.Comparendos.FirstAsync(x => x.Id == aviso.ComparendoId, ct).ConfigureAwait(false);
        if (comunicacion.Estado == EstadoComunicacion.Fallido)
            db.AvisosConductor.Remove(aviso);

        // El estado vuelve al mayor aviso que sí salió (los que siguen pendientes o entregados).
        var fallidas = new[] { EstadoComunicacion.Fallido, EstadoComunicacion.Rebotado };
        var vigentes = await (from a in db.AvisosConductor
                              join m in db.Comunicaciones on a.ComunicacionId equals m.Id
                              where a.ComparendoId == c.Id && a.Id != aviso.Id && !fallidas.Contains(m.Estado) && a.TipoAviso != TipoAviso.Aviso4
                              select a.TipoAviso).ToListAsync(ct).ConfigureAwait(false);
        c.EstadoNotificacionConductor = vigentes.Count == 0 ? EstadoNotificacionConductor.SinNotificar
            : vigentes.Max() switch
            {
                TipoAviso.Aviso1 => EstadoNotificacionConductor.Aviso1,
                TipoAviso.Aviso2 => EstadoNotificacionConductor.Aviso2,
                _ => EstadoNotificacionConductor.Aviso3,
            };
        timeline.Agregar(c, TipoEventoTimeline.ComunicacionEstado,
            $"{ServicioAvisos.Nombre(aviso.TipoAviso)} no llegó a {aviso.CorreoDestino} ({(comunicacion.Estado == EstadoComunicacion.Rebotado ? "rebotó: corrige el correo del conductor" : $"falló: {resultado.Mensaje ?? resultado.Resultado}; el scheduler lo reintentará")}).",
            Actor.Sistema, relacionadoTipo: "comunicacion_saliente", relacionadoId: comunicacion.Id);
    }
}
