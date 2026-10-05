using System.Text.Encodings.Web;
using System.Text.Json;

namespace Flit.Api.Identity;

/// <summary>
/// Cierre de sesión en todos los productos a la vez (front-channel logout de OIDC, HU #13004). Al cerrar la sesión del
/// hub, la respuesta es una página corta que abre en segundo plano <c>/auth/frontchannel-logout</c> de cada producto que
/// inició sesión desde ella: cada uno borra su cookie de sesión en ese momento. Sin esto, la cookie de un producto
/// quedaba con una sesión ya revocada y, al volver a entrar, sus primeras llamadas daban 401 antes de recuperarse.
/// </summary>
internal static class OidcFrontChannelLogout
{
    /// <summary>Ruta que cada app de la suite expone con <c>@flit/auth</c>.</summary>
    public const string Path = "/auth/frontchannel-logout";

    /// <summary>Tope de espera: si un producto no responde, el cierre sigue igual.</summary>
    private const int TimeoutMs = 2500;

    /// <summary>Origen (<c>esquema://host[:puerto]</c>) de un <c>redirect_uri</c> absoluto, o <c>null</c>.</summary>
    public static string? OriginOf(string? redirectUri) =>
        Uri.TryCreate(redirectUri, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            ? uri.GetLeftPart(UriPartial.Authority)
            : null;

    /// <summary>
    /// Página que avisa a los productos y luego sigue a <paramref name="target"/>. Con JavaScript sigue apenas todos
    /// responden (o al tope); sin él, a los 3 s por el <c>meta refresh</c>.
    /// </summary>
    public static IResult Page(IReadOnlyCollection<string> origins, string target)
    {
        var nonce = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));
        var urls = origins.Select(o => o + Path).ToList();
        var html = HtmlEncoder.Default;
        var json = JsonSerializer.Serialize(new { urls, target, timeout = TimeoutMs });
        var frames = string.Concat(urls.Select(u => $"<iframe src=\"{html.Encode(u)}\" hidden title=\"Cerrar sesión\"></iframe>"));

        var page = $$"""
            <!doctype html>
            <html lang="es">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <meta http-equiv="refresh" content="3;url={{html.Encode(target)}}">
            <title>Cerrando sesión…</title>
            </head>
            <body style="margin:0;display:grid;place-items:center;min-height:100vh;font-family:Poppins,system-ui,sans-serif;background:#eef5ff;color:#162744">
            <p>Cerrando sesión…</p>
            <noscript>{{frames}}</noscript>
            <script nonce="{{nonce}}">
            (function () {
              var c = {{json}};
              var pending = c.urls.length;
              var done = false;
              function go() { if (!done) { done = true; location.replace(c.target); } }
              c.urls.forEach(function (u) {
                var f = document.createElement("iframe");
                f.hidden = true;
                f.title = "Cerrar sesión";
                f.addEventListener("load", function () { if (--pending <= 0) go(); });
                f.addEventListener("error", function () { if (--pending <= 0) go(); });
                f.src = u;
                document.body.appendChild(f);
              });
              setTimeout(go, c.timeout);
            })();
            </script>
            </body>
            </html>
            """;

        var frameSources = string.Join(' ', origins);
        return new FrontChannelPage(page,
            $"default-src 'none'; script-src 'nonce-{nonce}'; style-src 'unsafe-inline'; frame-src {frameSources}; frame-ancestors 'none'");
    }

    private sealed class FrontChannelPage(string html, string csp) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
        {
            var response = httpContext.Response;
            response.StatusCode = StatusCodes.Status200OK;
            response.ContentType = "text/html; charset=utf-8";
            response.Headers.CacheControl = "no-store";
            response.Headers.ContentSecurityPolicy = csp;
            return response.WriteAsync(html, httpContext.RequestAborted);
        }
    }
}
