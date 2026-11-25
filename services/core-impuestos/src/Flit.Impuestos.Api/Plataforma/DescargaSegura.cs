namespace Flit.Impuestos.Api.Plataforma;

/// <summary>
/// Cabeceras de toda descarga de archivo (forma de SP-18 de la suite, copiada y no referenciada, EP-CF1). Antes de ejecutar el
/// endpoint agrega <c>X-Content-Type-Options: nosniff</c>, <c>Content-Security-Policy: sandbox; default-src 'none'</c>,
/// <c>Cache-Control: no-store</c> y <c>Pragma: no-cache</c>. El <c>Content-Disposition</c> lo genera el framework con
/// <c>Results.File(…, fileDownloadName)</c>.
/// </summary>
/// <example>
/// <code>
/// grupo.MapGet(RutaRecibo, DescargarAsync).RequirePermiso(Permisos.RecibosDescargar).DescargaSegura();
/// </code>
/// </example>
internal static class DescargaSeguraFiltro
{
    public const string PoliticaContenido = "sandbox; default-src 'none'";

    public static TBuilder DescargaSegura<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(static (contexto, siguiente) =>
        {
            var cabeceras = contexto.HttpContext.Response.Headers;
            cabeceras.XContentTypeOptions = "nosniff";
            cabeceras.ContentSecurityPolicy = PoliticaContenido;
            cabeceras.CacheControl = "no-store";
            cabeceras.Pragma = "no-cache";
            return siguiente(contexto);
        });
}
