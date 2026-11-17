namespace Flit.Diagnostico.Api.Plataforma;

/// <summary>
/// HU #14563, hallazgo M2 de seguridad (D19): las lecturas por celda que traen <c>respuesta_proveedor</c>, <c>respuesta_fuente</c>
/// o <c>personas</c> no se guardan en el navegador ni en cachés intermedias. Agrega <c>Cache-Control: no-store</c> y
/// <c>Pragma: no-cache</c> antes de ejecutar el endpoint.
/// </summary>
/// <example>
/// <code>
/// api.MapGet(RutaDetalleCelda, Detalle).RequirePermiso(Permisos.DiagnosticosVer).SinCacheEnRespuesta();
/// </code>
/// </example>
internal static class SinCache
{
    public static TBuilder SinCacheEnRespuesta<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(static (contexto, siguiente) =>
        {
            var cabeceras = contexto.HttpContext.Response.Headers;
            cabeceras.CacheControl = "no-store";
            cabeceras.Pragma = "no-cache";
            return siguiente(contexto);
        });
}
