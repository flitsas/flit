using Flit.Diagnostico.Api.Plataforma;

namespace Flit.Diagnostico.Api.Modulos.Carga;

/// <summary>
/// Formato del archivo de carga (HU #13749, F02-C6, decisión del usuario 2026-10-21): solo <c>.xlsx</c> y <c>.csv</c>, por
/// extensión y por firma del contenido. Un <c>.xls</c> (por extensión o por la firma OLE2 de los libros de Excel 97-2003,
/// aunque venga renombrado) es 422 <see cref="CodigoXls"/> con el mensaje de guardarlo como .xlsx; no se agrega un lector
/// de .xls. Otra extensión, o un .csv con firma de zip, PDF u OLE2 o con bytes nulos, es 415 <see cref="CodigoNoAdmitido"/>.
/// Un .xlsx sin firma zip o que no abre es 415 <see cref="CodigoIlegible"/>.
/// </summary>
/// <example>
/// <code>
/// FormatoCarga.Leer("vehiculos.csv", bytes) // → TablaCarga del LectorCsv
/// FormatoCarga.Leer("vehiculos.xls", bytes) // → ReglaException 422 FORMATO_XLS_NO_ADMITIDO
/// </code>
/// </example>
internal static class FormatoCarga
{
    public const string CodigoXls = "FORMATO_XLS_NO_ADMITIDO";
    public const string MensajeXls = "El formato .xls no se admite: guarde el archivo como .xlsx y vuelva a subirlo";
    public const string CodigoNoAdmitido = "FORMATO_NO_ADMITIDO";
    public const string CodigoIlegible = "ARCHIVO_ILEGIBLE";

    private static readonly byte[] FirmaZip = [0x50, 0x4B, 0x03, 0x04];
    private static readonly byte[] FirmaOle = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];
    private static readonly byte[] FirmaPdf = "%PDF"u8.ToArray();

    public static TablaCarga Leer(string? nombre, byte[] contenido)
    {
        ArgumentNullException.ThrowIfNull(contenido);
        var extension = Path.GetExtension(nombre ?? string.Empty).ToUpperInvariant();
        if (extension is not (".XLSX" or ".CSV" or ".XLS"))
            throw NoAdmitido();
        if (extension == ".XLS" || Empieza(contenido, FirmaOle))
            throw new ReglaException(CodigoXls, MensajeXls);

        if (extension == ".XLSX")
        {
            if (!Empieza(contenido, FirmaZip))
            {
                throw new ReglaException(CodigoIlegible,
                    "El archivo no es un libro .xlsx válido: ábralo en Excel, guárdelo como .xlsx y vuelva a subirlo.",
                    StatusCodes.Status415UnsupportedMediaType);
            }

            return LectorXlsx.Leer(contenido);
        }

        if (Empieza(contenido, FirmaZip) || Empieza(contenido, FirmaPdf) || contenido.AsSpan().Contains((byte)0))
            throw NoAdmitido();
        return LectorCsv.Leer(contenido);
    }

    private static bool Empieza(byte[] contenido, byte[] firma) => contenido.AsSpan().StartsWith(firma);

    private static ReglaException NoAdmitido() => new(
        CodigoNoAdmitido,
        "Formato de archivo no admitido: suba la plantilla en .xlsx o un .csv en UTF-8 separado por coma o punto y coma.",
        StatusCodes.Status415UnsupportedMediaType);
}
