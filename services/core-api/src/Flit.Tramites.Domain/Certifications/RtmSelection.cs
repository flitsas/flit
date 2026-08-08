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
    /// ¿Le aplica RTM al vehículo? Delega en <see cref="Tramites.Services.RtmCertificado"/>, que es
    /// donde vive la regla de negocio (más de cinco años desde la matrícula, y sin fecha se asume
    /// exigible por fallo seguro). <b>No se reimplementa aquí</b>: duplicar el umbral es la forma más
    /// silenciosa de que el certificado y el resto del sistema empiecen a discrepar.
    /// </summary>
    public static bool Applies(VehicleRegistrationFacts vehicle, DateOnly today) =>
        Tramites.Services.RtmCertificado.Aplica(
            esTraspaso: true, vehicle.FechaMatricula.Value, today);

    public static VigencyStatus DeriveStatus(RtmCertification inspection, DateOnly today)
    {
        if (inspection.Status.HasValue)
            return inspection.Status.Value;

        if (inspection.ValidUntil.Value is not { } until)
            return VigencyStatus.Unknown;

        return until >= today ? VigencyStatus.Vigente : VigencyStatus.Vencido;
    }
}
