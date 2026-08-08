namespace Flit.Tramites.Domain.Certifications;

/// <summary>
/// Elige, del histórico de revisiones técnico-mecánicas, la que va al certificado (D9: solo la vigente),
/// y decide si la RTM le es exigible al vehículo.
/// </summary>
/// <remarks>
/// Mismo criterio que <see cref="SoatSelection"/> y por la misma razón, aquí verificada en datos
/// reales: hay vehículos con cuatro revisiones rotuladas <c>APROBADA</c> y ninguna vigente. Se
/// selecciona por fecha de vencimiento.
/// </remarks>
public static class RtmSelection
{
    /// <summary>
    /// Meses desde la matrícula durante los cuales un vehículo nuevo no debe revisión
    /// técnico-mecánica. Es el criterio que ya aplica el generador del certificado.
    /// </summary>
    public const int GraceMonthsForNewVehicles = 24;

    public static RtmCertification? PickCurrent(
        IReadOnlyList<RtmCertification> history, DateOnly today)
    {
        if (history.Count == 0)
            return null;

        var usable = history.Where(r => r.HasAnyValue).ToList();
        if (usable.Count == 0)
            return null;

        var covering = usable
            .Where(r => r.ValidUntil.Value >= today && (r.ValidFrom.Value is null || r.ValidFrom.Value <= today))
            .OrderByDescending(r => r.ValidUntil.Value)
            .FirstOrDefault();
        if (covering is not null)
            return covering;

        var latestExpired = usable
            .Where(r => r.ValidUntil.Value.HasValue)
            .OrderByDescending(r => r.ValidUntil.Value)
            .FirstOrDefault();

        return latestExpired ?? usable[0];
    }

    /// <summary>
    /// ¿Le aplica RTM al vehículo? Un vehículo dentro del periodo de gracia desde su matrícula no la
    /// debe todavía, y el certificado debe decir "no aplica" en vez de dejar la tabla en blanco.
    /// Sin fecha de matrícula no se puede afirmar: se asume exigible, que es el lado seguro.
    /// </summary>
    public static bool Applies(VehicleRegistrationFacts vehicle, DateOnly today)
    {
        if (vehicle.FechaMatricula.Value is not { } registered)
            return true;

        return registered.AddMonths(GraceMonthsForNewVehicles) <= today;
    }

    public static VigencyStatus DeriveStatus(RtmCertification inspection, DateOnly today)
    {
        if (inspection.Status.HasValue)
            return inspection.Status.Value;

        if (inspection.ValidUntil.Value is not { } until)
            return VigencyStatus.Unknown;

        return until >= today ? VigencyStatus.Vigente : VigencyStatus.Vencido;
    }
}
