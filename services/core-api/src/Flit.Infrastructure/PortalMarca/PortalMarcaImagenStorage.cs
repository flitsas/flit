using Flit.Admin.Domain.Companies.TransitOffices.PortalMarca;
using Flit.Tramites.Application.Storage;

namespace Flit.Infrastructure.PortalMarca;

/// <summary>
/// Adaptador del puerto <see cref="IPortalMarcaImagenStorage"/> (HU #14376): delega en <see cref="IAttachmentStorage"/>, mismo
/// patrón que el logo de marca blanca y el banner. Un grupo por organismo. No guarda el nombre original del archivo: usa uno
/// neutro con la extensión del tipo real. Las imágenes se sirven por endpoint propio, sin URL prefirmadas.
/// </summary>
internal sealed class PortalMarcaImagenStorage(IAttachmentStorage storage) : IPortalMarcaImagenStorage
{
    private const string Tipo = "portal-marca";

    public async Task<NuevaPortalMarcaImagen> GuardarAsync(
        Guid transitOfficeId, string contentType, Stream contenido, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(contenido);
        var extension = contentType switch
        {
            "image/png" => ".png",
            "image/jpeg" => ".jpg",
            "image/webp" => ".webp",
            _ => throw new ArgumentOutOfRangeException(nameof(contentType), contentType, "Tipo de imagen no admitido."),
        };

        var guardado = await storage.SaveAsync(transitOfficeId, Tipo, "marca" + extension, contenido, ct).ConfigureAwait(false);
        return new NuevaPortalMarcaImagen(contentType, guardado.StoragePath, guardado.Sha256, guardado.SizeBytes);
    }

    public Task<Stream?> AbrirAsync(string storagePath, CancellationToken ct = default) =>
        string.IsNullOrWhiteSpace(storagePath) ? Task.FromResult<Stream?>(null) : storage.OpenReadAsync(storagePath, ct);
}
