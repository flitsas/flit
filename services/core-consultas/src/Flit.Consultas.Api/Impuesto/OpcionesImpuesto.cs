using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace Flit.Consultas.Api.Impuesto;

/// <summary>Teléfono, correo y dirección fijos que el paso 3 manda al servicio. Los valores viven en configuración.</summary>
internal sealed record ContactoImpuesto(
    string Celular,
    string Telefono,
    string Correo,
    string Departamento,
    string Ciudad,
    string TipoVia,
    string Numero1,
    string Letra1,
    bool Bis1,
    string Sufijo1,
    string Cardinalidad1,
    string Numero2,
    string Letra2,
    bool Bis2,
    string Sufijo2,
    string Placa,
    string Cardinalidad2,
    string DireccionCompleta)
{
    public static ContactoImpuesto Vacio { get; } = new("", "", "", "", "", "", "", "", false, "", "", "", "", false, "", "", "", "");
}

/// <summary>
/// Variables <c>IMPUESTO_API_*</c>. Sin URL en código. Los departamentos encendidos viven en la tabla, no aquí (ADR-0080).
/// </summary>
internal sealed record OpcionesImpuesto(Uri? BaseUrl, string Ruta, int TimeoutSegundos, ContactoImpuesto Contacto)
{
    public const string VariableBaseUrl = "IMPUESTO_API_BASE_URL";
    public const string VariableRuta = "IMPUESTO_API_PATH";
    public const string VariableTimeout = "IMPUESTO_API_TIMEOUT_SECONDS";
    public const int TimeoutPorDefecto = 280;

    public bool TieneRuta => BaseUrl is not null && Ruta.Length > 0;

    public static string Unir(string baseUrl, string ruta)
    {
        var izquierda = baseUrl.Trim().TrimEnd('/');
        var derecha = ruta.Trim().TrimStart('/');
        return $"{izquierda}/{derecha}";
    }

    public static OpcionesImpuesto Desde(IConfiguration configuracion)
    {
        ArgumentNullException.ThrowIfNull(configuracion);
        string Valor(string clave) => configuracion[clave]?.Trim() ?? "";
        var timeout = int.TryParse(Valor(VariableTimeout), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n > 0
            ? n
            : TimeoutPorDefecto;
        if (timeout >= 300)
            throw new InvalidOperationException($"{VariableTimeout} debe quedar por debajo del tope de la fuente (300).");
        var baseUrl = Uri.TryCreate(Valor(VariableBaseUrl), UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? uri
            : null;
        return new(baseUrl, Valor(VariableRuta).Trim('/'), timeout, LeerContacto(configuracion));
    }

    private static ContactoImpuesto LeerContacto(IConfiguration configuracion)
    {
        string C(string sufijo) => configuracion[$"IMPUESTO_CONTACTO_{sufijo}"]?.Trim() ?? "";
        static bool Bis(string texto) => string.Equals(texto, "true", StringComparison.OrdinalIgnoreCase);
        return new(
            C("CELULAR"), C("TELEFONO"), C("CORREO"), C("DIRECCION_DEPARTAMENTO"), C("DIRECCION_CIUDAD"),
            C("TIPO_VIA"), C("NUMERO_1"), C("LETRA_1"), Bis(C("BIS_1")), C("SUFIJO_1"), C("CARDINALIDAD_1"),
            C("NUMERO_2"), C("LETRA_2"), Bis(C("BIS_2")), C("SUFIJO_2"), C("PLACA"), C("CARDINALIDAD_2"),
            C("DIRECCION_COMPLETA"));
    }
}
