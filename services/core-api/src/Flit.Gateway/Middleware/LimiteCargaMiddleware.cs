using Microsoft.AspNetCore.Http.Features;

namespace Flit.Gateway.Middleware;

/// <summary>
/// GDC punto 10: la carga de paquetes OCR de Comparendos (<c>POST /api/v1/comparendos/ocr/paquetes</c>) admite hasta
/// 100 MB de archivos por petición. Sin esto, Kestrel corta a 30.000.000 bytes (su valor por defecto) con un 413 sin
/// cuerpo, y la pantalla que promete 200 archivos de 20 MB solo dejaba pasar unos 14 PDF de 2 MB.
/// El resto de rutas conserva el límite por defecto de Kestrel. Debe ir antes de cualquier middleware que lea el cuerpo.
/// </summary>
public sealed class LimiteCargaMiddleware(RequestDelegate next)
{
    /// <summary>100 MB de archivos (lo que ve la pantalla) + 1 MB para el sobre multipart (separadores y cabeceras de
    /// hasta 200 partes). Mismo valor en el gateway, core-comparendos y el proxy de Next.</summary>
    public const long LimiteOcrBytes = 101L * 1024 * 1024;

    public const string RutaOcrPaquetes = "/api/v1/comparendos/ocr/paquetes";

    public Task InvokeAsync(HttpContext context)
    {
        if (HttpMethods.IsPost(context.Request.Method)
            && string.Equals(context.Request.Path.Value?.TrimEnd('/'), RutaOcrPaquetes, StringComparison.OrdinalIgnoreCase)
            && context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limite)
        {
            limite.MaxRequestBodySize = LimiteOcrBytes;
        }

        return next(context);
    }
}
