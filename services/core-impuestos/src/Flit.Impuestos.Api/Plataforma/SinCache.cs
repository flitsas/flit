namespace Flit.Impuestos.Api.Plataforma;

/// <summary>
/// Las respuestas con PII del propietario o el resultado completo de una liquidación no se guardan en el navegador ni en cachés
/// intermedias: agrega <c>Cache-Control: no-store</c> y <c>Pragma: no-cache</c> antes de ejecutar el endpoint.
/// </summary>
/// <example>
/// <code>
/// api.MapGet("/liquidaciones/{id}/items/{itemId}", Detalle).RequirePermiso(Permisos.LiquidacionesVer).SinCacheEnRespuesta();
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
