using Flit.Tramites.Domain.Tramites.Estados;

namespace Flit.Tramites.Domain.RuntConfirmation;

/// <summary>
/// El valor de la columna «Confirmado en RUNT» del gestor (HU #12312): exactamente SÍ / NO / — y
/// nada más. No viaja el número de intentos, la marca ni el motivo: eso es del Historial interno.
/// Se deriva de las columnas del trámite, sin join a los intentos.
/// </summary>
public static class RuntConfirmedColumn
{
    public const string Yes = "yes";
    public const string No = "no";
    public const string NotConsulted = "not_consulted";

    public static readonly IReadOnlyList<string> All = [Yes, No, NotConsulted];

    /// <summary>
    /// <c>yes</c> = confirmado; <c>no</c> = ya hubo intentos con veredicto (pendiente, discrepancia, no
    /// verificable o tope, sin distinguirlos); <c>not_consulted</c> = ningún intento; <c>null</c> si el
    /// trámite no está aprobado (la columna no aplica). Un intento con error no cuenta como consulta:
    /// no concluyó nada.
    /// </summary>
    public static string? Derive(string status, DateTimeOffset? runtConfirmedAt, int runtAttempts, string? runtFlag)
    {
        if (!string.Equals(status, TramiteEstado.Aprobado, StringComparison.Ordinal))
            return null;
        if (runtConfirmedAt is not null)
            return Yes;
        return runtAttempts > 0 || runtFlag is not null ? No : NotConsulted;
    }
}
