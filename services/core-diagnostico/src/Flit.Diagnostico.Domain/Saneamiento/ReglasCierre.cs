namespace Flit.Diagnostico.Domain.Saneamiento;

/// <summary>
/// Reglas de cierre (plan §3.3, S27): SANEADO exige al menos un renglón, ninguno PENDIENTE y al menos uno SANEADO
/// (<c>422 CIERRE_INCOMPLETO</c>); NO_PROCEDE solo exige el motivo (<c>422 MOTIVO_REQUERIDO</c>) y deja los renglones como estén.
/// Los renglones que cuentan son los no quitados. Pura.
/// </summary>
/// <example>
/// <code>
/// ReglasCierre.ErrorCierreSaneado([ResultadoRenglonSaneamiento.Saneado, ResultadoRenglonSaneamiento.Pendiente]); // CIERRE_INCOMPLETO
/// </code>
/// </example>
public static class ReglasCierre
{
    public const string CierreIncompleto = "CIERRE_INCOMPLETO";

    /// <summary>Null si se puede cerrar como SANEADO.</summary>
    public static string? ErrorCierreSaneado(IReadOnlyCollection<ResultadoRenglonSaneamiento> renglones)
    {
        ArgumentNullException.ThrowIfNull(renglones);
        return renglones.Count > 0
            && !renglones.Contains(ResultadoRenglonSaneamiento.Pendiente)
            && renglones.Contains(ResultadoRenglonSaneamiento.Saneado)
                ? null
                : CierreIncompleto;
    }

    /// <summary>Null si el motivo de NO_PROCEDE es válido; si no, el código de <see cref="TextoLibreSaneamiento.CodigoMotivo"/>.</summary>
    public static string? ErrorCierreNoProcede(string? motivo) => TextoLibreSaneamiento.CodigoMotivo(motivo);
}
