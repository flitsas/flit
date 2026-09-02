namespace Flit.Tramites.Application.Ocr;

/// <summary>Qué respondió la sonda sobre la orientación de una página.</summary>
public enum PageOrientation
{
    /// <summary>El texto se lee en horizontal, tal cual: no hay que girar nada.</summary>
    Upright,

    /// <summary>Está girada. La sonda NO dice hacia dónde, a propósito — ver <see cref="IDocumentOrientationProbe"/>.</summary>
    Rotated,

    /// <summary>No se pudo averiguar (proveedor caído, respuesta ilegible). Se trata como «déjalo como está».</summary>
    Unknown,
}

/// <summary>
/// HU #12036 — pregunta al modelo si una página está DERECHA, sin pedirle que lea nada.
///
/// <para>Existe porque un escaneo denso y girado hace que el modelo <b>invente</b> datos con toda
/// confianza: tres archivos byte a byte idénticos devolvieron tres marcas de vehículo distintas y las
/// tres falsas, con <c>es_valido: true</c> y sin ninguna señal de baja confianza.</para>
///
/// <para><b>Devuelve un binario a propósito.</b> Se midió también preguntándole hacia qué lado estaba
/// girada, y acertó 3 de 4: en un caso invirtió el sentido. Detectar «no está derecha» es una
/// apreciación gruesa y sale fiable; deducir el giro exacto, no. Así que quien corrige prueba los
/// giros y vuelve a preguntar, en vez de fiarse de una dirección.</para>
/// </summary>
public interface IDocumentOrientationProbe
{
    /// <summary>
    /// Mira el PDF —se le pasa UNA página, para que la llamada sea barata— y dice si está derecha.
    /// Nunca lanza: ante cualquier fallo devuelve <see cref="PageOrientation.Unknown"/>.
    /// </summary>
    Task<PageOrientation> ProbeAsync(ReadOnlyMemory<byte> pdf, CancellationToken ct);
}
