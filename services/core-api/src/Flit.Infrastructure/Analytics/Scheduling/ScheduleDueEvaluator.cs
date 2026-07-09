using System.Globalization;

namespace Flit.Infrastructure.Analytics.Scheduling;

/// <summary>
/// Decisión PURA de vencimiento de un informe programado (Reportes 2.0, HU-D §8): sin I/O,
/// apta para tests unitarios deterministas. Un schedule "vence" si está activo (lo filtra el
/// caller), es su día/hora LOCAL (America/Bogota) y <c>last_sent_at</c> no pertenece a la
/// ventana actual (día para daily, semana ISO para weekly, mes para monthly).
/// </summary>
public static class ScheduleDueEvaluator
{
    public const string Daily = "daily";
    public const string Weekly = "weekly";
    public const string Monthly = "monthly";

    /// <summary>
    /// Zona horaria de negocio (§0 del contrato). Se intenta el id IANA <c>America/Bogota</c>
    /// (Linux/contenedores); en Windows sin ICU cae al id nativo <c>SA Pacific Standard Time</c>
    /// y, como última red, a un huso fijo UTC-5 — Colombia NO tiene horario de verano, así que
    /// los tres son equivalentes (mismo criterio que <c>FurCommand.ColombiaOffset</c>).
    /// </summary>
    public static TimeZoneInfo BogotaTimeZone { get; } = ResolveBogotaTimeZone();

    private static TimeZoneInfo ResolveBogotaTimeZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("America/Bogota"); }
        catch (TimeZoneNotFoundException) { }
        catch (InvalidTimeZoneException) { }

        try { return TimeZoneInfo.FindSystemTimeZoneById("SA Pacific Standard Time"); }
        catch (TimeZoneNotFoundException) { }
        catch (InvalidTimeZoneException) { }

        return TimeZoneInfo.CreateCustomTimeZone(
            "America/Bogota", TimeSpan.FromHours(-5), "Hora de Colombia", "Hora de Colombia");
    }

    /// <summary>¿Está vencido este schedule en el instante <paramref name="nowUtc"/>?</summary>
    /// <param name="dayOfWeek">0 (domingo) … 6 (sábado) — solo weekly.</param>
    /// <param name="dayOfMonth">1..28 — solo monthly.</param>
    public static bool IsDue(
        string frequency,
        int? dayOfWeek,
        int? dayOfMonth,
        int sendHour,
        DateTimeOffset? lastSentAtUtc,
        DateTimeOffset nowUtc,
        TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);

        var nowLocal = TimeZoneInfo.ConvertTime(nowUtc, timeZone);
        if (nowLocal.Hour != sendHour)
            return false;

        var dayMatches = frequency switch
        {
            Daily => true,
            // Convención del contrato (§4.4/§4.7): 0 = domingo … 6 = sábado, igual que System.DayOfWeek.
            Weekly => dayOfWeek is int dw && (int)nowLocal.DayOfWeek == dw,
            Monthly => dayOfMonth is int dm && nowLocal.Day == dm,
            _ => false,
        };
        if (!dayMatches)
            return false;

        if (lastSentAtUtc is null)
            return true;

        // Ya enviado en ESTA ventana → no re-enviar (idempotencia dentro del día/semana ISO/mes).
        var lastLocal = TimeZoneInfo.ConvertTime(lastSentAtUtc.Value, timeZone);
        return !SameWindow(frequency, lastLocal, nowLocal);
    }

    /// <summary>
    /// Periodo "vencido" que cubre el informe (fechas LOCALES, inclusive): daily → ayer;
    /// weekly → la semana ISO anterior (lunes a domingo); monthly → el mes calendario anterior.
    /// </summary>
    public static (DateOnly From, DateOnly To) GetElapsedPeriod(string frequency, DateTimeOffset nowLocal)
    {
        var today = DateOnly.FromDateTime(nowLocal.Date);
        switch (frequency)
        {
            case Weekly:
                // Lunes de la semana ISO actual → la semana anterior completa.
                var isoDay = ((int)nowLocal.DayOfWeek + 6) % 7; // 0 = lunes … 6 = domingo
                var currentMonday = today.AddDays(-isoDay);
                return (currentMonday.AddDays(-7), currentMonday.AddDays(-1));
            case Monthly:
                var firstOfCurrent = new DateOnly(today.Year, today.Month, 1);
                var firstOfPrevious = firstOfCurrent.AddMonths(-1);
                return (firstOfPrevious, firstOfCurrent.AddDays(-1));
            default: // daily
                var yesterday = today.AddDays(-1);
                return (yesterday, yesterday);
        }
    }

    /// <summary>
    /// Etiqueta en español del periodo para el asunto del correo. Formato dd/MM/yyyy con
    /// InvariantCulture: el repo compila con <c>InvariantGlobalization</c> (es-CO no existe).
    /// </summary>
    public static string DescribePeriod(string frequency, DateOnly from, DateOnly to)
    {
        var inv = CultureInfo.InvariantCulture;
        return frequency == Daily
            ? from.ToString("dd/MM/yyyy", inv)
            : $"{from.ToString("dd/MM/yyyy", inv)} al {to.ToString("dd/MM/yyyy", inv)}";
    }

    private static bool SameWindow(string frequency, DateTimeOffset lastLocal, DateTimeOffset nowLocal) =>
        frequency switch
        {
            Weekly => ISOWeek.GetYear(lastLocal.Date) == ISOWeek.GetYear(nowLocal.Date)
                      && ISOWeek.GetWeekOfYear(lastLocal.Date) == ISOWeek.GetWeekOfYear(nowLocal.Date),
            Monthly => lastLocal.Year == nowLocal.Year && lastLocal.Month == nowLocal.Month,
            _ => lastLocal.Date == nowLocal.Date,
        };
}
