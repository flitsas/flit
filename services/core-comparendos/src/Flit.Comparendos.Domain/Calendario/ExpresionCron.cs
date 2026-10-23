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

    private static readonly string[] Dias = ["domingo", "lunes", "martes", "miércoles", "jueves", "viernes", "sábado", "domingo"];

    /// <summary>
    /// La expresión en palabras para la tabla de trabajos (hallazgo B4-2): «Cada 15 minutos», «Todos los días a las 7:00 y
    /// 13:00», «Los viernes a las 17:00», «De lunes a viernes a las 6:00», «El último día de cada mes a las 18:00». Null si
    /// no es una forma conocida (la pantalla muestra la expresión).
    /// </summary>
    public static string? Describir(string? texto)
    {
        var c = (texto ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (c.Length != 5 || c[3] != "*")
            return null;
        var (min, hora, dia, semana) = (c[0], c[1], c[2], c[4]);
        if (hora == "*" && dia == "*" && semana == "*" && min.StartsWith("*/", StringComparison.Ordinal) && int.TryParse(min[2..], out var cada))
            return cada == 1 ? "Cada minuto" : $"Cada {cada} minutos";
        if (!int.TryParse(min, out var m) || m is < 0 or > 59)
            return null;
        var horas = hora.Split(',').Select(h => int.TryParse(h, out var x) && x is >= 0 and <= 23 ? x : -1).ToList();
        if (horas.Contains(-1))
            return null;
        var lista = horas.Select(h => $"{h}:{m:00}").ToList();
        var cuando = (horas.Count == 1 && horas[0] == 1 ? "a la " : "a las ")
            + (lista.Count == 1 ? lista[0] : $"{string.Join(", ", lista[..^1])} y {lista[^1]}");
        if (dia == "*" && semana == "*")
            return $"Todos los días {cuando}";
        if (dia == "L" && semana == "*")
            return $"El último día de cada mes {cuando}";
        if (int.TryParse(dia, out var d) && semana == "*")
            return $"El día {d} de cada mes {cuando}";
        if (dia != "*")
            return null;
        if (semana.Split('-') is [var a, var b] && int.TryParse(a, out var ia) && int.TryParse(b, out var ib) && ia is >= 0 and <= 7 && ib is >= 0 and <= 7)
            return $"De {Dias[ia]} a {Dias[ib]} {cuando}";
        var dias = semana.Split(',').Select(s => int.TryParse(s, out var x) && x is >= 0 and <= 7 ? Dias[x] : null).ToList();
        if (dias.Contains(null))
            return null;
        var nombres = dias.Select(n => n![^1] == 's' ? n : n + "s").ToList(); // «los viernes», «los martes», «los domingos»
        return $"Los {(nombres.Count == 1 ? nombres[0] : $"{string.Join(", ", nombres[..^1])} y {nombres[^1]}")} {cuando}";
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
