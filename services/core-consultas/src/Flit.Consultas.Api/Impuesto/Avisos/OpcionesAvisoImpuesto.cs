using System.Text;

namespace Flit.Consultas.Api.Impuesto.Avisos;

/// <summary>
/// Configuración del aviso del proveedor de impuesto (HU #14711, plan §3.3). <see cref="ClaveBaseUrl"/> es el origen público por el que
/// el proveedor llega al receptor (el callback queda en <c>{base}/api/v1/impuesto/avisos/{token}</c>); <see cref="ClaveLlaveToken"/>
/// firma el token opaco por empresa y debe tener al menos <see cref="BytesMinimosLlave"/> bytes en UTF-8. Sin cualquiera de las dos,
/// o con una base que no cumpla las reglas del receptor (https, sin usuario ni fragmento; http solo en loopback para el stub local),
/// <see cref="Activo"/> es false: no se pide aviso y la consulta se cierra por polling.
/// </summary>
/// <example>
/// <code>
/// var opciones = OpcionesAvisoImpuesto.Desde(configuration);
/// if (opciones.Activo) url = opciones.CallbackUrl(token);
/// </code>
/// </example>
internal sealed class OpcionesAvisoImpuesto
{
    public const string ClaveBaseUrl = "Consultas:Impuesto:AvisoBaseUrl";
    public const string ClaveLlaveToken = "Consultas:Impuesto:AvisoTokenKey";
    public const int BytesMinimosLlave = 32;
    public const int LargoMaximoUrl = 2048;

    private OpcionesAvisoImpuesto(string? baseUrl, byte[]? llave)
    {
        BaseUrl = baseUrl;
        Llave = llave;
    }

    /// <summary>Origen sin barra final; null si falta o no cumple las reglas.</summary>
    public string? BaseUrl { get; }

    /// <summary>Llave del token; null si falta o es corta.</summary>
    public byte[]? Llave { get; }

    public bool Activo => BaseUrl is not null && Llave is not null;

    /// <summary>La llave está configurada: el receptor puede reconocer tokens aunque la base no esté.</summary>
    public bool PuedeLeerTokens => Llave is not null;

    public static OpcionesAvisoImpuesto Desde(IConfiguration configuracion)
    {
        ArgumentNullException.ThrowIfNull(configuracion);
        var llave = configuracion[ClaveLlaveToken] is { Length: > 0 } texto && Encoding.UTF8.GetByteCount(texto) >= BytesMinimosLlave
            ? Encoding.UTF8.GetBytes(texto)
            : null;
        return new OpcionesAvisoImpuesto(ValidarBase(configuracion[ClaveBaseUrl]), llave);
    }

    /// <summary>Solo pruebas.</summary>
    internal static OpcionesAvisoImpuesto Crear(string? baseUrl, byte[]? llave) =>
        new(ValidarBase(baseUrl), llave is { Length: >= BytesMinimosLlave } ? llave : null);

    /// <summary>La URL completa del aviso; null sin base o si pasa de <see cref="LargoMaximoUrl"/>.</summary>
    public string? CallbackUrl(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        if (BaseUrl is null)
            return null;
        var url = BaseUrl + AvisosImpuestoEndpoints.Prefijo + "/" + token;
        return url.Length > LargoMaximoUrl ? null : url;
    }

    /// <summary>Base sin barra final si es https (o http en loopback), sin usuario, sin consulta ni fragmento; null si no.</summary>
    public static string? ValidarBase(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor) || !Uri.TryCreate(valor.Trim(), UriKind.Absolute, out var uri))
            return null;
        var esquemaValido = uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback);
        if (!esquemaValido || !string.IsNullOrEmpty(uri.UserInfo) || valor.Contains('#', StringComparison.Ordinal)
            || valor.Contains('?', StringComparison.Ordinal))
            return null;
        var limpia = valor.Trim().TrimEnd('/');
        return limpia.Length + AvisosImpuestoEndpoints.Prefijo.Length + 1 + TokenAvisoImpuesto.Largo > LargoMaximoUrl ? null : limpia;
    }
}
