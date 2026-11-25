using System.Security.Cryptography;
using System.Text;

namespace Flit.Impuestos.Api.Modulos.Ejecucion;

/// <summary>Token opaco de un ítem y su hash. <see cref="ToString"/> nunca imprime el token.</summary>
public sealed record TokenAviso(string Token, byte[] Hash)
{
    public override string ToString() => "TokenAviso { Token = [REDACTADO] }";
}

/// <summary>
/// Token del <c>callbackUrl</c> de cada envío (plan §1.5, DA-4): 32 bytes aleatorios en base64url sin relleno (43 caracteres, 256
/// bits: no se puede enumerar). En la base solo se guarda <see cref="Hash"/> (<c>SHA-256</c> de los bytes UTF-8 del token, columna
/// <c>liquidacion_items.token_hash</c>); el receptor del webhook (M10) calcula el mismo hash con el token de la ruta.
/// <see cref="CallbackUrl"/> = <c>{Impuestos:Avisos:UrlBase}/{token}</c> con las reglas del receptor de la guía §2.2: https, sin
/// usuario ni contraseña, sin fragmento y de 2048 caracteres como máximo. Se registra la URL final exacta (sin barra al final ni
/// redirecciones: el proveedor no las sigue). http solo con <see cref="ClavePermitirHttpLocal"/>=true y en loopback (stub local, plan
/// §7; apagada por defecto). La base se valida al arrancar: si no cumple, se avisa una vez en el log y no hay <c>callbackUrl</c> (el
/// ítem se concilia por consulta).
/// </summary>
/// <example>
/// <code>
/// var token = generador.Generar();
/// item.TokenHash = token.Hash;
/// var url = generador.CallbackUrl(token.Token); // null si la base no está configurada
/// </code>
/// </example>
public sealed partial class GeneradorTokenAviso
{
    public const string ClaveUrlBase = "Impuestos:Avisos:UrlBase";
    public const string ClavePermitirHttpLocal = "Impuestos:Avisos:PermitirHttpLocal";
    public const int LargoMaximoUrl = 2048;
    public const int BytesToken = 32;

    private readonly string? _base;

    public GeneradorTokenAviso(IConfiguration configuracion, ILogger<GeneradorTokenAviso>? log = null)
    {
        ArgumentNullException.ThrowIfNull(configuracion);
        var valor = configuracion[ClaveUrlBase];
        _base = Validar(valor, configuracion.GetValue(ClavePermitirHttpLocal, false));
        if (_base is null && !string.IsNullOrWhiteSpace(valor) && log is not null)
            BaseInvalida(log);
    }

    /// <summary>Hay una base https válida para armar el <c>callbackUrl</c>.</summary>
    public bool Configurado => _base is not null;

    public TokenAviso Generar()
    {
        var token = Base64Url(RandomNumberGenerator.GetBytes(BytesToken));
        return new TokenAviso(token, Hash(token));
    }

    /// <summary><c>SHA-256</c> del token (UTF-8): lo que se guarda y lo que busca el receptor.</summary>
    public static byte[] Hash(string token)
    {
        ArgumentNullException.ThrowIfNull(token);
        return SHA256.HashData(Encoding.UTF8.GetBytes(token));
    }

    /// <summary>La URL del aviso para el token; null sin base o si pasa de <see cref="LargoMaximoUrl"/>.</summary>
    public string? CallbackUrl(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        if (_base is null)
            return null;
        var url = _base + "/" + token;
        return url.Length > LargoMaximoUrl ? null : url;
    }

    /// <summary>La base sin barra final si cumple la guía §2.2 (ver la clase); null si no.</summary>
    public static string? Validar(string? valor, bool permitirHttpLocal)
    {
        if (string.IsNullOrWhiteSpace(valor) || !Uri.TryCreate(valor.Trim(), UriKind.Absolute, out var uri))
            return null;
        var esquemaValido = uri.Scheme == Uri.UriSchemeHttps || (permitirHttpLocal && uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback);
        if (!esquemaValido || !string.IsNullOrEmpty(uri.UserInfo) || valor.Contains('#', StringComparison.Ordinal))
            return null;
        var base_ = valor.Trim().TrimEnd('/');
        return base_.Length + 1 + 43 > LargoMaximoUrl ? null : base_;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Impuestos__Avisos__UrlBase no cumple las reglas del receptor (https, sin usuario ni fragmento, máximo 2048 caracteres; http solo en loopback con PermitirHttpLocal): las liquidaciones salen sin callbackUrl.")]
    private static partial void BaseInvalida(ILogger logger);

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
