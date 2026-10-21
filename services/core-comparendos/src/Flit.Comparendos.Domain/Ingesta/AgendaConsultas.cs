using System.Globalization;
using Flit.Comparendos.Domain.Calendario;

namespace Flit.Comparendos.Domain.Ingesta;

public enum ModoAgenda
{
    /// <summary>A horas fijas del día (por ejemplo 07:00 y 13:00).</summary>
    HorasFijas,

    /// <summary>Cada N minutos dentro de una ventana (por ejemplo cada hora de 06:00 a 20:00).</summary>
    Intervalo,
}

/// <summary>
/// Agenda de consultas de una empresa por canal (HU #13521), en hora de Colombia. Las horas se guardan como «HH:mm».
/// Un intervalo arranca en el inicio de la ventana: cada 60 desde 06:00 → 06:00, 07:00, … hasta el fin inclusive.
/// </summary>
public sealed record AgendaConsultas(ModoAgenda Modo, IReadOnlyList<TimeOnly> Horas, int? IntervaloMinutos, TimeOnly? Desde, TimeOnly? Hasta, bool SoloDiasHabiles)
{
    public const int IntervaloMinimo = 30;

    public static TimeOnly? Hora(string? s) =>
        TimeOnly.TryParseExact(s?.Trim(), ["HH:mm", "H:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : null;

    /// <summary>Errores de la agenda (lista vacía = válida).</summary>
    public IReadOnlyList<string> Validar()
    {
        var errores = new List<string>();
        if (Modo == ModoAgenda.HorasFijas)
        {
            if (Horas.Count is 0 or > 24)
                errores.Add("Indica entre 1 y 24 horas de consulta.");
            if (Horas.Any(h => h.Second != 0 || h.Millisecond != 0))
                errores.Add("Las horas van en minutos exactos (HH:mm).");
            if (Horas.Distinct().Count() != Horas.Count)
                errores.Add("Hay horas repetidas.");
        }
        else
        {
            if (IntervaloMinutos is not { } i || i < IntervaloMinimo || i > 24 * 60)
                errores.Add($"El intervalo va de {IntervaloMinimo} minutos a 24 horas.");
            if (Desde is not { } d || Hasta is not { } h || h < d)
                errores.Add("Indica la ventana del intervalo (desde y hasta, en el mismo día).");
        }

        return errores;
    }

    /// <summary>¿Toca consultar en este minuto (hora de Colombia)?</summary>
    public bool Toca(DateTime local, CalendarioHabil calendario)
    {
        ArgumentNullException.ThrowIfNull(calendario);
        if (SoloDiasHabiles && !calendario.EsHabil(DateOnly.FromDateTime(local)))
            return false;
        var minuto = new TimeOnly(local.Hour, local.Minute);
        return Modo == ModoAgenda.HorasFijas
            ? Horas.Contains(minuto)
            : Desde is { } d && Hasta is { } h && IntervaloMinutos is > 0 and var i && minuto >= d && minuto <= h
              && ((int)(minuto - d).TotalMinutes) % i == 0;
    }

    /// <summary>La próxima consulta a partir de este minuto (exclusivo), buscando hasta 14 días.</summary>
    public DateTime? Proxima(DateTime local, CalendarioHabil calendario)
    {
        ArgumentNullException.ThrowIfNull(calendario);
        var inicio = new DateTime(local.Year, local.Month, local.Day, local.Hour, local.Minute, 0, local.Kind);
        for (var dia = 0; dia < 14; dia++)
        {
            var fecha = DateOnly.FromDateTime(inicio).AddDays(dia);
            if (SoloDiasHabiles && !calendario.EsHabil(fecha))
                continue;
            foreach (var h in Disparos().OrderBy(h => h))
            {
                var momento = fecha.ToDateTime(h, local.Kind);
                if (momento > inicio)
                    return momento;
            }
        }

        return null;
    }

    private IEnumerable<TimeOnly> Disparos()
    {
        if (Modo == ModoAgenda.HorasFijas)
            return Horas;
        if (Desde is not { } d || Hasta is not { } h || IntervaloMinutos is not > 0)
            return [];
        var lista = new List<TimeOnly>();
        for (var t = d; t <= h; t = t.AddMinutes(IntervaloMinutos.Value))
        {
            lista.Add(t);
            if (t.AddMinutes(IntervaloMinutos.Value) < t)
                break; // dio la vuelta a medianoche
        }

        return lista;
    }

    /// <summary>Texto para la empresa: «Todos los días a las 07:00 y 13:00» o «Días hábiles, cada 60 min de 06:00 a 20:00».</summary>
    public string Describir()
    {
        var dias = SoloDiasHabiles ? "Días hábiles" : "Todos los días";
        return Modo == ModoAgenda.HorasFijas
            ? $"{dias} a las {string.Join(", ", Horas.OrderBy(h => h).Select(h => h.ToString("HH:mm", CultureInfo.InvariantCulture)))}"
            : $"{dias}, cada {IntervaloMinutos} min de {Desde:HH\\:mm} a {Hasta:HH\\:mm}";
    }
}
