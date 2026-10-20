namespace Flit.Comparendos.Domain.Calendario;

/// <summary>
/// Días hábiles colombianos: ni sábados, ni domingos, ni festivos del calendario maestro (Excel C6). El calendario es
/// inmutable: se arma una vez con los festivos guardados y se comparte.
/// </summary>
public sealed class CalendarioHabil
{
    private readonly HashSet<DateOnly> _festivos;

    public CalendarioHabil(IEnumerable<DateOnly> festivos)
    {
        ArgumentNullException.ThrowIfNull(festivos);
        _festivos = [.. festivos];
    }

    /// <summary>Calendario con los festivos calculados de un rango de años (pruebas y siembra).</summary>
    public static CalendarioHabil Calculado(int desde, int hasta) =>
        new(FestivosColombia.Entre(desde, hasta).Select(f => f.Fecha));

    public bool EsHabil(DateOnly fecha) =>
        fecha.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && !_festivos.Contains(fecha);

    public bool EsFestivo(DateOnly fecha) => _festivos.Contains(fecha);

    /// <summary>
    /// El día hábil número <paramref name="dias"/> contado desde <paramref name="desde"/>, sin contar ese mismo día: con
    /// una notificación el viernes, el día hábil 1 es el lunes siguiente (si no es festivo). Con 0 devuelve la misma
    /// fecha.
    /// </summary>
    public DateOnly SumarHabiles(DateOnly desde, int dias)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(dias);
        var fecha = desde;
        var contados = 0;
        while (contados < dias)
        {
            fecha = fecha.AddDays(1);
            if (EsHabil(fecha))
                contados++;
        }

        return fecha;
    }

    /// <summary>La misma fecha si es hábil; si no, el siguiente día hábil.</summary>
    public DateOnly HabilOSiguiente(DateOnly fecha)
    {
        while (!EsHabil(fecha))
            fecha = fecha.AddDays(1);
        return fecha;
    }

    /// <summary>
    /// Qué día hábil es <paramref name="hoy"/> contado desde <paramref name="desde"/> (sin contarlo): 0 si es la misma
    /// fecha o anterior; si <paramref name="hoy"/> no es hábil, cuenta los hábiles ya cumplidos.
    /// </summary>
    public int HabilesTranscurridos(DateOnly desde, DateOnly hoy)
    {
        if (hoy <= desde)
            return 0;

        var contados = 0;
        for (var fecha = desde.AddDays(1); fecha <= hoy; fecha = fecha.AddDays(1))
        {
            if (EsHabil(fecha))
                contados++;
        }

        return contados;
    }

    /// <summary>Días hábiles que faltan desde <paramref name="hoy"/> (sin contarlo) hasta <paramref name="limite"/> incluido.</summary>
    public int HabilesRestantes(DateOnly hoy, DateOnly limite) =>
        limite <= hoy ? 0 : HabilesTranscurridos(hoy, limite);
}
