namespace Flit.Tramites.Domain.RevocationRequests;

/// <summary>
/// Implementación simple de <see cref="IBusinessDayCalculator"/>: excluye sábados y domingos, sin
/// festivos (ver justificación en la interfaz). Determinística y sin IO: apta para el dominio puro.
/// </summary>
public sealed class BusinessDayCalculator : IBusinessDayCalculator
{
    public DateTimeOffset AddBusinessDays(DateTimeOffset start, int businessDays)
    {
        if (businessDays < 0)
            throw new ArgumentOutOfRangeException(nameof(businessDays), businessDays, "businessDays no puede ser negativo.");

        var result = start;
        var remaining = businessDays;
        while (remaining > 0)
        {
            result = result.AddDays(1);
            if (result.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                remaining--;
        }

        return result;
    }
}
