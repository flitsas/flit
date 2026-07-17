using System.Globalization;
using System.Text;

namespace Flit.Modules.Quipux.Domain.Mapeo;

/// <summary>
/// Construye el <c>documento</c> de Quipux: <c>{empresa}_{prefijo}_{yyyyMMdd_HHmm}_{sufijo}</c>.
/// Réplica exacta de <c>generateFileNameQuipux</c> de FLIT 1.0, porque ese nombre es la llave de
/// correlación con Quipux: cambiar el formato rompería la consulta de estado de todo lo ya radicado.
/// </summary>
/// <remarks>
/// <para>
/// El reloj entra por parámetro y NO se lee aquí. No es solo testabilidad: el nombre se calcula una
/// única vez al encolar la submission y se persiste (<c>QuipuxSubmission.DocumentName</c>). En 1.0
/// se regeneraba en cada intento y, como el formato tiene precisión de minuto, un reintento
/// producía un nombre distinto: el trámite quedaba imposible de consultar y podía duplicarse en
/// Quipux. El <see cref="DateTimeOffset"/> se formatea tal como llega, sin convertir a UTC ni a
/// local: el desfase lo decide quien encola.
/// </para>
/// </remarks>
public static class QuipuxDocumentNameBuilder
{
    /// <summary>Precisión de minuto, sin segundos. Formato de 1.0; no se toca.</summary>
    private const string FormatoFecha = "yyyyMMdd_HHmm";

    /// <summary>
    /// Arma el nombre del documento.
    /// </summary>
    /// <param name="razonSocial">Razón social del cliente, cruda. Se sanitiza con <see cref="SanitizarRazonSocial"/>.</param>
    /// <param name="prefijo">Prefijo del tipo de trámite (TR, TRU, MI, MIL). Sale de <c>external_refs</c>, no del código.</param>
    /// <param name="momento">Instante de la radicación. Se formatea como <c>yyyyMMdd_HHmm</c> tal cual llega.</param>
    /// <param name="sufijo">Identificador del vehículo (placa o VIN, según el tipo de trámite).</param>
    /// <param name="maxLongitudEmpresa">Tope de la parte de empresa: 35 en traspaso, 25 en matrícula. Es parámetro, no constante: sale de la parametrización del tipo de trámite.</param>
    /// <exception cref="ArgumentException">
    /// La razón social queda vacía tras sanitizar. En 1.0 el <c>catch</c> devolvía <c>''</c> y el
    /// flujo caía al UUID del PDF como nombre del documento: un accidente, no un requisito. Un
    /// nombre sin empresa no es correlacionable ni auditable, así que aquí es un fallo explícito.
    /// </exception>
    public static string Build(
        string? razonSocial,
        string prefijo,
        DateTimeOffset momento,
        string sufijo,
        int maxLongitudEmpresa)
    {
        ArgumentNullException.ThrowIfNull(prefijo);
        ArgumentNullException.ThrowIfNull(sufijo);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxLongitudEmpresa);

        var empresa = SanitizarRazonSocial(razonSocial, maxLongitudEmpresa);

        if (empresa.Length == 0)
        {
            throw new ArgumentException(
                $"La razón social '{razonSocial}' queda vacía tras sanitizar: no contiene ningún carácter alfanumérico ASCII y no puede formar un nombre de documento Quipux.",
                nameof(razonSocial));
        }

        var fecha = momento.ToString(FormatoFecha, CultureInfo.InvariantCulture);

        return string.Concat(empresa, "_", prefijo, "_", fecha, "_", sufijo);
    }

    /// <summary>
    /// Sanitiza la razón social como <c>sanitizeStringCompanyName</c> de 1.0, EN ESTE ORDEN:
    /// (1) recortar extremos, (2) eliminar todo lo que no sea <c>[a-zA-Z0-9\s]</c>,
    /// (3) colapsar cada racha de espacios en un único <c>_</c>, (4) truncar.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Dos rarezas de 1.0 que se replican a propósito, porque el nombre ya radicado depende de ellas:
    /// </para>
    /// <para>
    /// — Las tildes y la Ñ se ELIMINAN, no se transliteran: <c>"Ñandú S.A."</c> → <c>"and_SA"</c>.
    /// El paso (2) las borra por no ser ASCII, y como el borrado ocurre ANTES del colapso de
    /// espacios, un carácter suprimido entre espacios puede dejar un <c>_</c> de más
    /// (<c>"A Ñ B"</c> → <c>"A  B"</c> → <c>"A_B"</c>, pero <c>"A Ñ"</c> → <c>"A "</c> → <c>"A_"</c>).
    /// </para>
    /// <para>
    /// — El truncado es el ÚLTIMO paso, así que los <c>_</c> ya insertados cuentan contra el tope y
    /// el resultado puede terminar en <c>_</c> o cortar una palabra por la mitad.
    /// </para>
    /// </remarks>
    public static string SanitizarRazonSocial(string? razonSocial, int maxLongitud)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxLongitud);

        if (string.IsNullOrWhiteSpace(razonSocial))
        {
            return string.Empty;
        }

        var origen = razonSocial.Trim();
        var destino = new StringBuilder(origen.Length);
        var espaciosPendientes = false;

        foreach (var c in origen)
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                if (espaciosPendientes)
                {
                    destino.Append('_');
                    espaciosPendientes = false;
                }

                destino.Append(c);
            }
            else if (char.IsWhiteSpace(c))
            {
                espaciosPendientes = true;
            }
        }

        if (espaciosPendientes)
        {
            destino.Append('_');
        }

        return destino.Length > maxLongitud
            ? destino.ToString(0, maxLongitud)
            : destino.ToString();
    }
}
