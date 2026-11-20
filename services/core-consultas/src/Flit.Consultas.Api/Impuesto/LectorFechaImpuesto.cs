namespace Flit.Consultas.Api.Impuesto;

/// <summary>
/// Fechas informativas del servicio. El primer grupo de <c>d/m/a</c> es el día. No cambian la clasificación.
/// </summary>
internal static class LectorFechaImpuesto
{
    private static readonly string[] Meses =
    [
        "enero", "febrero", "marzo", "abril", "mayo", "junio", "julio", "agosto", "septiembre", "setiembre", "octubre", "noviembre", "diciembre",
    ];

    private static readonly int[] Numeros = [1, 2, 3, 4, 5, 6, 7, 8, 9, 9, 10, 11, 12];

    public static string? Leer(string? texto)
    {
        var normal = TextoImpuesto.Normalizar(texto);
        if (normal.Length == 0)
            return null;
        if (Fecha(normal, '/', out var iso) || Fecha(normal, '-', out iso) || Iso(normal, out iso) || ConMes(normal, out iso))
            return iso;
        return null;
    }

    public static int? Anio(string? texto)
    {
        var normal = TextoImpuesto.Normalizar(texto).Trim();
        if (normal.Length != 4 || !int.TryParse(normal, out var anio))
            return null;
        return anio is >= 1900 and <= 2100 ? anio : null;
    }

    private static bool Fecha(string texto, char separador, out string? iso)
    {
        iso = null;
        var partes = texto.Split(separador);
        if (partes.Length != 3)
            return false;
        if (partes[0].Length == 4)
            return false;
        return Armar(partes[0], partes[1], partes[2], out iso);
    }

    private static bool Iso(string texto, out string? iso)
    {
        iso = null;
        var partes = texto.Split('-');
        if (partes.Length != 3 || partes[0].Length != 4)
            return false;
        return Armar(partes[2], partes[1], partes[0], out iso);
    }

    private static bool ConMes(string texto, out string? iso)
    {
        iso = null;
        const string de = " de ";
        var corte = texto.IndexOf(de, StringComparison.Ordinal);
        if (corte < 0)
            return false;
        var izquierda = texto[..corte];
        var anio = texto[(corte + de.Length)..].Trim();
        var espacio = izquierda.LastIndexOf(' ');
        if (espacio <= 0)
            return false;
        var mes = Mes(izquierda[..espacio].Trim());
        var dia = izquierda[(espacio + 1)..].Trim();
        return mes is not null && Armar(dia, mes.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), anio, out iso);
    }

    private static int? Mes(string nombre)
    {
        for (var i = 0; i < Meses.Length; i++)
        {
            if (Meses[i] == nombre)
                return Numeros[i];
        }

        return null;
    }

    private static bool Armar(string diaTexto, string mesTexto, string anioTexto, out string? iso)
    {
        iso = null;
        if (!int.TryParse(diaTexto, out var dia) || !int.TryParse(mesTexto, out var mes) || !int.TryParse(anioTexto, out var anio))
            return false;
        try
        {
            iso = new DateOnly(anio, mes, dia).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }
}
