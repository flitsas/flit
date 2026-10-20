using Flit.Admin.Application.Companies.Branding;
using Microsoft.Extensions.Options;

namespace Flit.Infrastructure.Storage;

/// <summary>
/// Logos de marca para core-identity (Epic #13217, HU #13232): <b>solo lectura</b>, para la pantalla de login y el
/// endpoint público de logos. Los logos se suben desde la administración de core-api (que usa el almacenamiento de
/// adjuntos completo); por eso guardar aquí es un error de configuración, no un caso esperado.
/// </summary>
internal sealed class FileManagerBrandLogoReader(HttpClient http, IOptions<FileManagerOptions> options) : IBrandLogoStorage
{
    public Task<StoredBrandLogo> SaveAsync(Guid tenantId, string filename, Stream content, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("core-identity solo lee logos de marca; se suben desde core-api.");

    public Task<Stream?> OpenReadAsync(string storagePath, CancellationToken cancellationToken = default) =>
        FileManagerDownloader.OpenReadAsync(http, options.Value, storagePath, cancellationToken);
}
