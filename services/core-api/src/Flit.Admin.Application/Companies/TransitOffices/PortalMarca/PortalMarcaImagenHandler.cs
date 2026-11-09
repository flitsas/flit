using Flit.Admin.Domain.Companies.TransitOffices.PortalMarca;

namespace Flit.Admin.Application.Companies.TransitOffices.PortalMarca;

/// <summary>Imagen lista para servir: bytes, tipo y SHA-256 (ETag).</summary>
public sealed record PortalMarcaImagenContenido(Stream Contenido, string ContentType, string Sha256);

/// <summary>
/// HU #14376 — cargar, quitar y leer el logo y el banner del portal de un organismo. Cada carga es una versión nueva con id
/// propio y la anterior se conserva. El formato se decide por el contenido real, no por la extensión.
/// </summary>
public sealed class PortalMarcaImagenHandler(
    IPortalMarcaRepository repo, IPortalMarcaImagenStorage storage, PortalMarcaAdminHandler marca)
{
    public async Task<PortalMarcaResultado<PortalMarcaDto>> SubirAsync(
        Guid transitOfficeId, string kind, string? nombreArchivo, Stream archivo, Guid actor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(archivo);
        var limite = PortalMarcaReglas.LimiteBytes(kind);

        // Se lee un byte más que el tope: así se detecta el exceso sin cargar el archivo completo en memoria.
        var memoria = new MemoryStream();
        var excedio = false;
        var buffer = new byte[16 * 1024];
        int leidos;
        while ((leidos = await archivo.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            if (memoria.Length + leidos > limite)
            {
                excedio = true;
                break;
            }

            memoria.Write(buffer, 0, leidos);
        }

        await using (memoria.ConfigureAwait(false))
        {
            memoria.Position = 0;
            var (error, tipo) = await PortalMarcaReglas.ValidarImagenAsync(kind, nombreArchivo, memoria, excedio, ct).ConfigureAwait(false);
            if (error is not null)
                return PortalMarcaAdminHandler.Falla(error);

            memoria.Position = 0;
            var guardada = await storage.GuardarAsync(transitOfficeId, tipo!, memoria, ct).ConfigureAwait(false);
            var anterior = await repo.ObtenerAsync(transitOfficeId, ct).ConfigureAwait(false);
            var (_, registro) = await repo.AgregarImagenAsync(transitOfficeId, kind, guardada, actor, ct).ConfigureAwait(false);

            var anteriorId = kind == PortalMarcaImagen.KindBanner ? anterior.Draft.BannerId : anterior.Draft.LogoId;
            await marca.EscribirAuditoriaAsync(
                $"portal_marca_{kind}", actor, transitOfficeId,
                anteriorId is null ? null : PortalMarcaAdminHandler.Resumen(anterior.Draft),
                PortalMarcaAdminHandler.Resumen(registro.Draft), ct).ConfigureAwait(false);
            return PortalMarcaResultado<PortalMarcaDto>.De(PortalMarcaAdminHandler.ADto(registro));
        }
    }

    public async Task<PortalMarcaDto> QuitarAsync(Guid transitOfficeId, string kind, Guid actor, CancellationToken ct = default)
    {
        var anterior = await repo.ObtenerAsync(transitOfficeId, ct).ConfigureAwait(false);
        var registro = await repo.QuitarImagenAsync(transitOfficeId, kind, actor, ct).ConfigureAwait(false);
        var tenia = kind == PortalMarcaImagen.KindBanner ? anterior.Draft.BannerId : anterior.Draft.LogoId;
        if (tenia is not null)
            await marca.EscribirAuditoriaAsync(
                $"portal_marca_{kind}_quitado", actor, transitOfficeId,
                PortalMarcaAdminHandler.Resumen(anterior.Draft), PortalMarcaAdminHandler.Resumen(registro.Draft), ct).ConfigureAwait(false);
        return PortalMarcaAdminHandler.ADto(registro);
    }

    /// <summary>Metadatos de una imagen (también de borrador). El endpoint verifica el alcance con <c>TransitOfficeId</c> antes de abrirla.</summary>
    public Task<PortalMarcaImagen?> BuscarAsync(Guid imagenId, CancellationToken ct = default) =>
        repo.ObtenerImagenAsync(imagenId, ct);

    public async Task<PortalMarcaImagenContenido?> AbrirAsync(PortalMarcaImagen imagen, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(imagen);
        var stream = await storage.AbrirAsync(imagen.StoragePath, ct).ConfigureAwait(false);
        return stream is null ? null : new PortalMarcaImagenContenido(stream, imagen.ContentType, imagen.Sha256);
    }
}
