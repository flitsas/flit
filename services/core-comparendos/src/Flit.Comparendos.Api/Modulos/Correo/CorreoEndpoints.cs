using Flit.Comparendos.Api.Modulos.Comparendos;
using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Domain.Modelo;
using Microsoft.EntityFrameworkCore;

namespace Flit.Comparendos.Api.Modulos.Correo;

/// <summary>Píxel de apertura (público, sin token): registra la primera apertura y cuenta las demás [Excel C12].</summary>
public static class CorreoEndpoints
{
    private static readonly byte[] Gif = Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7");

    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/publico/t/{archivo}", Abrir).AllowAnonymous();
    }

    private static async Task<IResult> Abrir(string archivo, ComparendosDb db, Timeline timeline, TimeProvider time, CancellationToken ct)
    {
        if (Guid.TryParseExact(archivo.Replace(".gif", string.Empty, StringComparison.OrdinalIgnoreCase), "N", out var id))
        {
            var ahora = time.GetUtcNow();
            // La primera apertura se marca con una condición en el UPDATE: si dos clientes cargan el píxel a la vez, solo
            // uno la registra (y solo ese deja el evento en el timeline).
            var primera = await db.Comunicaciones.Where(c => c.Id == id && c.AbiertoEn == null)
                .ExecuteUpdateAsync(u => u
                    .SetProperty(c => c.Aperturas, c => c.Aperturas + 1)
                    .SetProperty(c => c.AbiertoEn, ahora)
                    .SetProperty(c => c.Estado, c => c.Estado == EstadoComunicacion.Enviado || c.Estado == EstadoComunicacion.Entregado ? EstadoComunicacion.Abierto : c.Estado), ct)
                .ConfigureAwait(false) == 1;
            if (!primera)
            {
                await db.Comunicaciones.Where(c => c.Id == id).ExecuteUpdateAsync(u => u.SetProperty(c => c.Aperturas, c => c.Aperturas + 1), ct).ConfigureAwait(false);
            }
            else if (await db.Comunicaciones.AsNoTracking().Where(x => x.Id == id && x.ComparendoId != null).Select(x => new { x.ComparendoId, x.Asunto }).FirstOrDefaultAsync(ct).ConfigureAwait(false) is { } com
                && await db.Comparendos.FirstOrDefaultAsync(c => c.Id == com.ComparendoId, ct).ConfigureAwait(false) is { } comparendo)
            {
                // §3.17: la apertura también queda en la gestión del comparendo (informativa: el píxel no es prueba de lectura).
                timeline.Agregar(comparendo, TipoEventoTimeline.ComunicacionEstado, $"Se abrió el correo «{com.Asunto}» (apertura informativa).", Actor.Sistema,
                    new { estado = "Abierto" }, "comunicacion_saliente", id);
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
        }

        return Results.File(Gif, "image/gif");
    }
}
