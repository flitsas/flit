using System.Globalization;
using System.Text;
using Flit.Impuestos.Api.Infraestructura.Excel;
using Flit.Impuestos.Api.Plataforma;
using Flit.Impuestos.Domain.Liquidaciones;

namespace Flit.Impuestos.Api.Modulos.Carga;

/// <summary>Archivo de la carga masiva tal como llegó: nombre del cliente y bytes (a lo sumo <see cref="AnalizadorCarga.MaximoBytes"/>).</summary>
public sealed record ArchivoCarga(string Nombre, byte[] Contenido);

/// <summary>Fila inválida del esquema <c>AnalisisCarga.invalidas</c>: número de fila, valor tal cual (recortado) y motivo.</summary>
public sealed record FilaInvalidaDto(int Fila, string Valor, string Motivo);

/// <summary>Esquema <c>AnalisisCarga</c> de impuestos.v1.yaml 1.1.0.</summary>
public sealed record AnalisisCargaDto(int Validas, int Duplicadas, IReadOnlyList<FilaInvalidaDto> Invalidas, int Tope, bool SuperaTope);

/// <summary>Placa única válida del archivo con la fila donde apareció primero.</summary>
public sealed record PlacaCargada(int Fila, string Placa);

/// <summary>Resultado del análisis: placas únicas en orden de aparición, repetidas e inválidas.</summary>
public sealed record ResultadoCarga(IReadOnlyList<PlacaCargada> Placas, int Duplicadas, IReadOnlyList<FilaInvalidaDto> Invalidas)
{
    public AnalisisCargaDto Resumen(int tope) => new(Placas.Count, Duplicadas, Invalidas, tope, Placas.Count > tope);
}

/// <summary>
/// Análisis del archivo de placas de la carga masiva (HU #14694, M12; F3-CF1, S4, S16; plan §2.3):
/// <list type="number">
///   <item>Tamaño: más de <see cref="MaximoBytes"/> ⇒ 413 <c>ARCHIVO_DEMASIADO_GRANDE</c>.</item>
///   <item>Formato por extensión (<c>.csv</c> o <c>.xlsx</c>) y por contenido: un .xlsx debe ser un zip que se abre como libro; un
///   .csv no puede ser un zip ni traer bytes nulos ni UTF-8 inválido. Si no, 415 <c>FORMATO_NO_SOPORTADO</c>.</item>
///   <item>CSV: UTF-8 con o sin BOM; separador <c>;</c> o <c>,</c> según cuál aparece más en el encabezado (fuera de comillas);
///   campos entre comillas dobles con <c>""</c> escapado. XLSX: la primera hoja (<see cref="LectorXlsx"/>). El encabezado es la
///   primera fila con algún valor; las filas vacías se saltan.</item>
///   <item>Columna «placa»: el encabezado que, recortado, sin tildes y en minúsculas, es <see cref="Columna"/>; si no hay, 422
///   <c>COLUMNA_PLACA_AUSENTE</c>.</item>
///   <item>Cada fila: celda vacía ⇒ <c>PLACA_VACIA</c>; placa que no se normaliza (S16) o de menos de 4 caracteres ⇒
///   <c>PLACA_INVALIDA</c>; repetida tras normalizar ⇒ cuenta como duplicada contra su primera aparición (S4).</item>
///   <item>Topes (B-1, M-3, N-2), iguales en CSV y XLSX: se deja de leer al juntar <see cref="MaximoPlacas"/> placas únicas válidas;
///   más de <see cref="MaximoFilasLeidas"/> filas leídas antes de eso ⇒ 415 <c>FORMATO_NO_SOPORTADO</c>; cada fila se lee hasta la
///   columna <see cref="LectorXlsx.MaximoColumnas"/>.</item>
/// </list>
/// No escribe nada: la masiva lo vuelve a correr al crear el lote.
/// </summary>
/// <example>
/// <code>
/// var r = AnalizadorCarga.Analizar(new ArchivoCarga("placas.csv", bytes));
/// var resumen = r.Resumen(tope: 500); // { validas, duplicadas, invalidas[], tope, superaTope }
/// </code>
/// </example>
public static class AnalizadorCarga
{
    public const int MaximoBytes = 5 * 1024 * 1024;

    /// <summary>
    /// Placas únicas válidas que se juntan como mucho (B-1): el tope de placas más alto que admite cualquier compañía
    /// (<see cref="Persistence.Siembra.ConfiguracionPorDefecto.MaxRegistrosLoteMaximo"/>) más una, que basta para saber que lo supera.
    /// Al llegar ahí se deja de leer, en CSV y en XLSX; las filas siguientes no cuentan como duplicadas ni inválidas.
    /// </summary>
    public const int MaximoPlacas = Persistence.Siembra.ConfiguracionPorDefecto.MaxRegistrosLoteMaximo + 1;

    /// <summary>
    /// Filas que se leen como mucho, contando encabezado, vacías, repetidas e inválidas (M-3, B-1, N-2): 4 veces el tope de placas
    /// más alto, holgura para un archivo real con el tope completo y tres filas de ruido por placa. Si se pasa antes de juntar
    /// <see cref="MaximoPlacas"/>, el archivo se rechaza con 415 <c>FORMATO_NO_SOPORTADO</c>, como los demás topes contra la bomba.
    /// </summary>
    public const int MaximoFilasLeidas = 4 * Persistence.Siembra.ConfiguracionPorDefecto.MaxRegistrosLoteMaximo;
    public const string Columna = "placa";
    public const string MotivoVacia = "PLACA_VACIA";
    public const string MotivoInvalida = "PLACA_INVALIDA";

    /// <summary>Largo máximo del <c>valor</c> de una fila inválida en la respuesta (el archivo puede traer cualquier cosa).</summary>
    public const int LargoMaximoValor = 50;

    private static readonly byte[] FirmaZip = [0x50, 0x4B, 0x03, 0x04];

    public static ResultadoCarga Analizar(ArchivoCarga archivo)
    {
        ArgumentNullException.ThrowIfNull(archivo);
        if (archivo.Contenido.Length > MaximoBytes)
            throw ErroresCarga.DemasiadoGrande();
        var extension = Path.GetExtension(archivo.Nombre ?? string.Empty).ToUpperInvariant();
        var esZip = archivo.Contenido.AsSpan().StartsWith(FirmaZip);
        var recolector = new Recolector();
        var tabla = extension switch
        {
            ".XLSX" when esZip => LectorXlsx.Leer(archivo.Contenido, MaximoFilasLeidas, recolector.Seguir),
            ".CSV" when !esZip => LeerCsv(archivo.Contenido, MaximoFilasLeidas, recolector.Seguir),
            _ => null,
        } ?? throw ErroresCarga.Formato();

        if (ColumnaPlaca(tabla.Encabezados) < 0)
            throw ErroresCarga.ColumnaAusente();

        return recolector.Resultado();
    }

    /// <summary>Índice de la columna «placa» en los encabezados; -1 si no está.</summary>
    private static int ColumnaPlaca(IReadOnlyList<string?> encabezados)
    {
        for (var i = 0; i < encabezados.Count; i++)
        {
            if (string.Equals(Normalizar(encabezados[i]), Columna, StringComparison.Ordinal))
                return i;
        }

        return -1;
    }

    /// <summary>
    /// Clasifica las filas a medida que se leen, igual en CSV y XLSX, y pide parar al juntar <see cref="MaximoPlacas"/> placas únicas
    /// válidas (o en la primera fila, si no hay columna «placa»).
    /// </summary>
    private sealed class Recolector
    {
        private readonly List<PlacaCargada> placas = [];
        private readonly HashSet<string> vistas = new(StringComparer.Ordinal);
        private readonly List<FilaInvalidaDto> invalidas = [];
        private int duplicadas;
        private int? columna;

        public bool Seguir(IReadOnlyList<string?> encabezados, FilaLeida fila)
        {
            columna ??= ColumnaPlaca(encabezados);
            var indice = columna.Value;
            if (indice < 0)
                return false;

            var valor = (indice < fila.Celdas.Count ? fila.Celdas[indice] : null)?.Trim() ?? string.Empty;
            if (valor.Length == 0)
                invalidas.Add(new FilaInvalidaDto(fila.Numero, string.Empty, MotivoVacia));
            else if (!Placa.TryNormalizar(valor, out var placa) || placa.Length < Liquidaciones.CrearLiquidacionIndividual.LargoMinimoPlaca)
                invalidas.Add(new FilaInvalidaDto(fila.Numero, valor.Length <= LargoMaximoValor ? valor : valor[..LargoMaximoValor], MotivoInvalida));
            else if (vistas.Add(placa))
                placas.Add(new PlacaCargada(fila.Numero, placa));
            else
                duplicadas++;

            return placas.Count < MaximoPlacas;
        }

        public ResultadoCarga Resultado() => new(placas, duplicadas, invalidas);
    }

    /// <summary>Encabezado comparable: recortado, sin BOM ni tildes y en minúsculas.</summary>
    internal static string Normalizar(string? encabezado)
    {
        var limpio = (encabezado ?? string.Empty).Trim().Trim('﻿').Trim().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(limpio.Length);
        foreach (var c in limpio)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(char.ToLowerInvariant(c));
        }

        return sb.ToString();
    }

    /// <summary>
    /// El CSV como tabla, con las reglas de <see cref="LectorXlsx"/>: cada fila con valor después del encabezado va a
    /// <paramref name="seguir"/> hasta que devuelva false, se leen a lo sumo <see cref="LectorXlsx.MaximoColumnas"/> campos por registro
    /// y todo registro (encabezado y líneas vacías incluidos) cuenta contra <paramref name="maximoFilasLeidas"/>. Null si no es texto
    /// UTF-8 (bytes nulos o secuencias inválidas) o si pasa ese tope.
    /// </summary>
    internal static TablaLeida? LeerCsv(byte[] contenido, int maximoFilasLeidas, Func<IReadOnlyList<string?>, FilaLeida, bool>? seguir = null)
    {
        string texto;
        try
        {
            texto = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(contenido);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }

        if (texto.Contains('\0', StringComparison.Ordinal))
            return null;
        if (texto.Length > 0 && texto[0] == '﻿')
            texto = texto[1..];

        var registros = Registros(texto);
        List<string?>? encabezados = null;
        char separador = ',';
        var filas = new List<FilaLeida>();
        var leidas = 0;
        foreach (var (linea, crudo) in registros)
        {
            if (++leidas > maximoFilasLeidas)
                return null;
            if (encabezados is null)
            {
                if (string.IsNullOrWhiteSpace(crudo))
                    continue;
                separador = Separador(crudo);
                encabezados = Campos(crudo, separador);
                continue;
            }

            var celdas = Campos(crudo, separador);
            if (celdas.Any(c => !string.IsNullOrWhiteSpace(c)))
            {
                var fila = new FilaLeida(linea, celdas);
                filas.Add(fila);
                if (seguir is not null && !seguir(encabezados, fila))
                    break;
            }
        }

        return new TablaLeida(encabezados ?? [], filas);
    }

    /// <summary>Registros del CSV con la línea donde empieza cada uno (un campo entre comillas puede traer saltos de línea).</summary>
    private static List<(int Linea, string Texto)> Registros(string texto)
    {
        var registros = new List<(int, string)>();
        var actual = new StringBuilder();
        var linea = 1;
        var inicio = 1;
        var enComillas = false;
        for (var i = 0; i < texto.Length; i++)
        {
            var c = texto[i];
            if (c == '"')
            {
                enComillas = !enComillas;
                actual.Append(c);
                continue;
            }

            if ((c == '\r' || c == '\n') && !enComillas)
            {
                if (c == '\r' && i + 1 < texto.Length && texto[i + 1] == '\n')
                    i++;
                registros.Add((inicio, actual.ToString()));
                actual.Clear();
                linea++;
                inicio = linea;
                continue;
            }

            if (c == '\n')
                linea++;
            actual.Append(c);
        }

        if (actual.Length > 0)
            registros.Add((inicio, actual.ToString()));
        return registros;
    }

    /// <summary><c>;</c> si aparece al menos tantas veces como <c>,</c> fuera de comillas (y alguna vez); si no, <c>,</c>.</summary>
    private static char Separador(string encabezado)
    {
        int puntoYComa = 0, coma = 0;
        var enComillas = false;
        foreach (var c in encabezado)
        {
            if (c == '"')
                enComillas = !enComillas;
            else if (!enComillas && c == ';')
                puntoYComa++;
            else if (!enComillas && c == ',')
                coma++;
        }

        return puntoYComa > 0 && puntoYComa >= coma ? ';' : ',';
    }

    /// <summary>Campos del registro; después del <see cref="LectorXlsx.MaximoColumnas"/>-ésimo se ignoran (N-2, igual que en XLSX).</summary>
    private static List<string?> Campos(string registro, char separador)
    {
        var campos = new List<string?>();
        var actual = new StringBuilder();
        var enComillas = false;
        for (var i = 0; i < registro.Length; i++)
        {
            var c = registro[i];
            if (enComillas)
            {
                if (c == '"' && i + 1 < registro.Length && registro[i + 1] == '"')
                {
                    actual.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    enComillas = false;
                }
                else
                {
                    actual.Append(c);
                }
            }
            else if (c == '"')
            {
                enComillas = true;
            }
            else if (c == separador)
            {
                campos.Add(actual.ToString());
                actual.Clear();
                if (campos.Count == LectorXlsx.MaximoColumnas)
                    return campos;
            }
            else
            {
                actual.Append(c);
            }
        }

        campos.Add(actual.ToString());
        return campos;
    }
}

/// <summary>Errores de la carga masiva con el literal exacto de <c>x-flit-literales.errores</c> (impuestos.v1.yaml 1.1.0).</summary>
internal static class ErroresCarga
{
    public const string FormatoNoSoportado = "FORMATO_NO_SOPORTADO";
    public const string ArchivoDemasiadoGrande = "ARCHIVO_DEMASIADO_GRANDE";
    public const string ColumnaPlacaAusente = "COLUMNA_PLACA_AUSENTE";
    public const string ArchivoSinPlacas = "ARCHIVO_SIN_PLACAS";
    public const string LimiteSuperado = "LIMITE_SUPERADO";

    public const string LiteralFormato = "Solo se admiten archivos CSV y XLSX.";
    public const string LiteralDemasiadoGrande = "El archivo supera el máximo de 5 MB.";
    public const string LiteralColumnaAusente = "Falta la columna de placa.";
    public const string LiteralSinPlacas = "El archivo no tiene placas válidas.";
    public const string LiteralLimite = "El archivo supera el tope de {tope} placas por lote.";

    public static ReglaException Formato() => new(FormatoNoSoportado, LiteralFormato, StatusCodes.Status415UnsupportedMediaType) { Campo = "archivo" };

    public static ReglaException DemasiadoGrande() =>
        new(ArchivoDemasiadoGrande, LiteralDemasiadoGrande, StatusCodes.Status413PayloadTooLarge) { Campo = "archivo" };

    public static ReglaException ColumnaAusente() => new(ColumnaPlacaAusente, LiteralColumnaAusente) { Campo = "archivo" };

    public static ReglaException SinPlacas() => new(ArchivoSinPlacas, LiteralSinPlacas) { Campo = "archivo" };

    public static ReglaException Limite(int tope) =>
        new(LimiteSuperado, LiteralLimite.Replace("{tope}", tope.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)) { Campo = "archivo", Tope = tope };
}
