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

    private static async Task<IResult> Abrir(string archivo, ComparendosDb db, TimeProvider time, CancellationToken ct)
    {
        if (Guid.TryParseExact(archivo.Replace(".gif", string.Empty, StringComparison.OrdinalIgnoreCase), "N", out var id))
        {
            var ahora = time.GetUtcNow();
            await db.Comunicaciones.Where(c => c.Id == id)
                .ExecuteUpdateAsync(u => u
                    .SetProperty(c => c.Aperturas, c => c.Aperturas + 1)
                    .SetProperty(c => c.AbiertoEn, c => c.AbiertoEn ?? ahora)
                    .SetProperty(c => c.Estado, c => c.Estado == EstadoComunicacion.Enviado || c.Estado == EstadoComunicacion.Entregado ? EstadoComunicacion.Abierto : c.Estado), ct)
                .ConfigureAwait(false);
        }

        return Results.File(Gif, "image/gif");
    }
}
