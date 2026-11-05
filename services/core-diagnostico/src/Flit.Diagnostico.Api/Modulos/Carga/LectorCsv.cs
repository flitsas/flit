using System.Text;
using Flit.Diagnostico.Api.Plataforma;

namespace Flit.Diagnostico.Api.Modulos.Carga;

/// <summary>
/// Lector propio de .csv de la carga masiva (HU #13749, F02-H4, decisión del usuario 2026-10-21: sin dependencias nuevas).
/// UTF-8 estricto, con o sin BOM; separador coma o punto y coma, el que más aparezca fuera de comillas en el encabezado
/// (Excel en español guarda con punto y coma); comillas al estilo RFC 4180 (<c>""</c> es una comilla, un campo entre
/// comillas puede llevar separadores y saltos de línea); saltos CRLF o LF. Cada registro lleva el número de la línea en
/// que empieza, con el encabezado en la 1, como la fila de la hoja en el .xlsx. Los registros vacíos se omiten.
/// </summary>
/// <example>
/// <code>
/// LectorCsv.Leer(Encoding.UTF8.GetBytes("Identificador_Vehiculo;Documento_Locatario\nABC123;\n")) // → 1 fila, número 2
/// </code>
/// </example>
internal static class LectorCsv
{
    private static readonly UTF8Encoding Utf8Estricto = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static TablaCarga Leer(byte[] contenido)
    {
        ArgumentNullException.ThrowIfNull(contenido);
        string texto;
        try
        {
            texto = Utf8Estricto.GetString(contenido);
        }
        catch (DecoderFallbackException)
        {
            throw new ReglaException(FormatoCarga.CodigoIlegible,
                "El archivo .csv no está en UTF-8: guárdelo como «CSV UTF-8 (delimitado por comas)» o use la plantilla .xlsx y vuelva a subirlo.",
                StatusCodes.Status415UnsupportedMediaType);
        }

        if (texto.Length > 0 && texto[0] == '﻿')
            texto = texto[1..];

        var registros = Registros(texto, Separador(texto));
        if (registros.Count == 0)
            return new TablaCarga([], []);

        var filas = registros.Skip(1)
            .Where(r => r.Celdas.Any(c => !string.IsNullOrWhiteSpace(c)))
            .Select(r => new FilaLeida(r.Linea, r.Celdas))
            .ToList();
        return new TablaCarga(registros[0].Celdas, filas);
    }

    /// <summary>Punto y coma si aparece más que la coma fuera de comillas en la primera línea; si no, coma.</summary>
    private static char Separador(string texto)
    {
        int comas = 0, puntoYComa = 0;
        var entreComillas = false;
        foreach (var c in texto)
        {
            if (c == '"')
                entreComillas = !entreComillas;
            else if (!entreComillas && (c == '\n' || c == '\r'))
                break;
            else if (!entreComillas && c == ',')
                comas++;
            else if (!entreComillas && c == ';')
                puntoYComa++;
        }

        return puntoYComa > comas ? ';' : ',';
    }

    private static List<(int Linea, string?[] Celdas)> Registros(string texto, char separador)
    {
        var registros = new List<(int, string?[])>();
        var celdas = new List<string?>();
        var celda = new StringBuilder();
        var entreComillas = false;
        var linea = 1;
        var inicio = 1;
        var hayContenido = false;

        for (var i = 0; i < texto.Length; i++)
        {
            var c = texto[i];
            if (entreComillas)
            {
                if (c == '"' && i + 1 < texto.Length && texto[i + 1] == '"')
                {
                    celda.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    entreComillas = false;
                }
                else
                {
                    if (c == '\n')
                        linea++;
                    celda.Append(c);
                }

                continue;
            }

            if (c == '"')
            {
                entreComillas = true;
                hayContenido = true;
            }
            else if (c == separador)
            {
                celdas.Add(celda.ToString());
                celda.Clear();
                hayContenido = true;
            }
            else if (c is '\r' or '\n')
            {
                if (c == '\r' && i + 1 < texto.Length && texto[i + 1] == '\n')
                    i++;
                Cerrar();
                linea++;
                inicio = linea;
            }
            else
            {
                celda.Append(c);
                hayContenido = true;
            }
        }

        Cerrar();
        return registros;

        void Cerrar()
        {
            // Una línea vacía no es un registro (tampoco antes del encabezado), pero sí cuenta para la numeración.
            if (hayContenido || celda.Length > 0)
            {
                celdas.Add(celda.ToString());
                registros.Add((inicio, [.. celdas]));
            }

            celdas.Clear();
            celda.Clear();
            hayContenido = false;
        }
    }
}
