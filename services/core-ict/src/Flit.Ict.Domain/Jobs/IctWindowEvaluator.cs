namespace Flit.Ict.Domain.Jobs;

/// <summary>
/// Evalúa si la hora actual cae dentro de la ventana operativa (por defecto 08-20, zona
/// America/Bogotá = UTC-5, sin horario de verano). Lógica pura para testear sin I/O.
/// </summary>
public static class IctWindowEvaluator
{
    private const int BogotaUtcOffsetHours = -5;

    public static bool IsWithinWindow(DateTime utcNow, int startHour, int endHour)
    {
        var localHour = utcNow.AddHours(BogotaUtcOffsetHours).Hour;
        return localHour >= startHour && localHour < endHour;
    }
}
