namespace Flit.Comparendos.Domain.Calendario;

/// <summary>
/// Festivos nacionales de Colombia de un año, calculados: fijos, los que la Ley 51 de 1983 (Ley Emiliani) traslada al
/// lunes siguiente y los que dependen de la Pascua. Sirve para sembrar el calendario maestro (que el SuperAdmin puede
/// corregir) y para las pruebas; el cálculo de plazos usa siempre el calendario guardado.
/// </summary>
public static class FestivosColombia
{
    public static IReadOnlyList<Festivo> DelAnio(int anio)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(anio, 1984);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(anio, 2200);

        var pascua = DomingoDePascua(anio);
        var festivos = new List<Festivo>
        {
            new(new DateOnly(anio, 1, 1), "Año Nuevo"),
            new(Emiliani(new DateOnly(anio, 1, 6)), "Día de los Reyes Magos"),
            new(Emiliani(new DateOnly(anio, 3, 19)), "Día de San José"),
            new(pascua.AddDays(-3), "Jueves Santo"),
            new(pascua.AddDays(-2), "Viernes Santo"),
            new(new DateOnly(anio, 5, 1), "Día del Trabajo"),
            new(pascua.AddDays(43), "Ascensión del Señor"),
            new(pascua.AddDays(64), "Corpus Christi"),
            new(pascua.AddDays(71), "Sagrado Corazón de Jesús"),
            new(Emiliani(new DateOnly(anio, 6, 29)), "San Pedro y San Pablo"),
            new(new DateOnly(anio, 7, 20), "Día de la Independencia"),
            new(new DateOnly(anio, 8, 7), "Batalla de Boyacá"),
            new(Emiliani(new DateOnly(anio, 8, 15)), "La Asunción de la Virgen"),
            new(Emiliani(new DateOnly(anio, 10, 12)), "Día de la Raza"),
            new(Emiliani(new DateOnly(anio, 11, 1)), "Día de Todos los Santos"),
            new(Emiliani(new DateOnly(anio, 11, 11)), "Independencia de Cartagena"),
            new(new DateOnly(anio, 12, 8), "Día de la Inmaculada Concepción"),
            new(new DateOnly(anio, 12, 25), "Día de Navidad"),
        };
        // Dos festivos trasladados pueden caer el mismo lunes (1-jul-2019: Sagrado Corazón y San Pedro): un solo día.
        return festivos
            .GroupBy(f => f.Fecha)
            .Select(g => new Festivo(g.Key, string.Join(" y ", g.Select(f => f.Nombre))))
            .OrderBy(f => f.Fecha)
            .ToList();
    }

    /// <summary>Festivos de un rango de años, ambos incluidos.</summary>
    public static IReadOnlyList<Festivo> Entre(int desde, int hasta)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(desde, hasta);
        return Enumerable.Range(desde, hasta - desde + 1).SelectMany(DelAnio).ToList();
    }

    /// <summary>Domingo de Pascua (algoritmo anónimo gregoriano, Meeus/Jones/Butcher).</summary>
    public static DateOnly DomingoDePascua(int anio)
    {
        var a = anio % 19;
        var b = anio / 100;
        var c = anio % 100;
        var d = b / 4;
        var e = b % 4;
        var f = (b + 8) / 25;
        var g = (b - f + 1) / 3;
        var h = ((19 * a) + b - d - g + 15) % 30;
        var i = c / 4;
        var k = c % 4;
        var l = (32 + (2 * e) + (2 * i) - h - k) % 7;
        var m = (a + (11 * h) + (22 * l)) / 451;
        var mes = (h + l - (7 * m) + 114) / 31;
        var dia = ((h + l - (7 * m) + 114) % 31) + 1;
        return new DateOnly(anio, mes, dia);
    }

    private static DateOnly Emiliani(DateOnly fecha)
    {
        var hastaLunes = ((int)DayOfWeek.Monday - (int)fecha.DayOfWeek + 7) % 7;
        return fecha.AddDays(hastaLunes);
    }
}

public sealed record Festivo(DateOnly Fecha, string Nombre);
