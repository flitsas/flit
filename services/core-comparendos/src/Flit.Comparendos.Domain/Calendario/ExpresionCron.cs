namespace Flit.Comparendos.Domain.Calendario;

/// <summary>
/// Expresión cron de 5 campos (minuto hora día-del-mes mes día-de-la-semana), evaluada en hora de Colombia. Admite
/// <c>*</c>, listas (<c>7,13</c>), rangos (<c>1-5</c>), pasos (<c>*/15</c>, <c>8-18/2</c>) y <c>L</c> en el día del mes
/// (último día). Como en cron clásico, si día del mes y día de la semana están restringidos, basta con que coincida
/// uno. Sin dependencias: ADR-0024 descarta librerías de programación de tareas.
/// </summary>
public sealed class ExpresionCron
{
    private readonly bool[] _minutos = new bool[60];
    private readonly bool[] _horas = new bool[24];
    private readonly bool[] _dias = new bool[32];
    private readonly bool[] _meses = new bool[13];
    private readonly bool[] _diasSemana = new bool[7];
    private readonly bool _ultimoDia;
    private readonly bool _diaRestringido;
    private readonly bool _diaSemanaRestringido;

    private ExpresionCron(string texto)
    {
        var partes = texto.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (partes.Length != 5)
            throw new FormatException("La expresión cron debe tener 5 campos: minuto hora día mes día-de-la-semana.");

        Llenar(partes[0], _minutos, 0, 59);
        Llenar(partes[1], _horas, 0, 23);
        if (partes[2] == "L")
        {
            _ultimoDia = true;
        }
        else
        {
            Llenar(partes[2], _dias, 1, 31);
        }

        Llenar(partes[3], _meses, 1, 12);
        Llenar(partes[4].Replace("7", "0", StringComparison.Ordinal), _diasSemana, 0, 6);
        _diaRestringido = partes[2] != "*";
        _diaSemanaRestringido = partes[4] != "*";
        Texto = texto;
    }

    public string Texto { get; }

    public static ExpresionCron Parse(string texto)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(texto);
        return new ExpresionCron(texto.Trim());
    }

    public static bool EsValida(string? texto, out string? error)
    {
        try
        {
            _ = Parse(texto ?? string.Empty);
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>¿Toca en este minuto? <paramref name="momento"/> es la hora local de Colombia.</summary>
    public bool Coincide(DateTime momento) =>
        _minutos[momento.Minute] && _horas[momento.Hour] && _meses[momento.Month] && CoincideDia(momento);

    /// <summary>La siguiente vez que toca después de <paramref name="desde"/> (hora local), buscando hasta un año adelante.</summary>
    public DateTime? Siguiente(DateTime desde)
    {
        var t = new DateTime(desde.Year, desde.Month, desde.Day, desde.Hour, desde.Minute, 0, DateTimeKind.Unspecified).AddMinutes(1);
        var limite = t.AddYears(1);
        while (t < limite)
        {
            if (!_meses[t.Month] || !CoincideDia(t))
            {
                t = t.Date.AddDays(1);
                continue;
            }

            if (!_horas[t.Hour])
            {
                t = t.Date.AddHours(t.Hour + 1);
                continue;
            }

            if (_minutos[t.Minute])
                return t;
            t = t.AddMinutes(1);
        }

        return null;
    }

    private bool CoincideDia(DateTime t)
    {
        var dia = _ultimoDia ? t.Day == DateTime.DaysInMonth(t.Year, t.Month) : _dias[t.Day];
        var semana = _diasSemana[(int)t.DayOfWeek];
        return (_diaRestringido || _ultimoDia, _diaSemanaRestringido) switch
        {
            (true, true) => dia || semana,
            (true, false) => dia,
            (false, true) => semana,
            _ => true,
        };
    }

    private static void Llenar(string campo, bool[] destino, int min, int max)
    {
        foreach (var parte in campo.Split(','))
        {
            var (rango, paso) = parte.Split('/') switch
            {
                [var r] => (r, 1),
                [var r, var p] when int.TryParse(p, out var n) && n > 0 => (r, n),
                _ => throw new FormatException($"Paso inválido en «{parte}»."),
            };

            int desde, hasta;
            if (rango == "*")
            {
                (desde, hasta) = (min, max);
            }
            else if (rango.Split('-') is [var a, var b] && int.TryParse(a, out var x) && int.TryParse(b, out var y))
            {
                (desde, hasta) = (x, y);
            }
            else if (int.TryParse(rango, out var unico))
            {
                (desde, hasta) = (unico, paso > 1 ? max : unico);
            }
            else
            {
                throw new FormatException($"Valor inválido «{parte}».");
            }

            if (desde < min || hasta > max || desde > hasta)
                throw new FormatException($"«{parte}» está fuera de {min}-{max}.");
            for (var v = desde; v <= hasta; v += paso)
                destino[v] = true;
        }
    }
}
