namespace Flit.Tramites.Domain.Certifications;

/// <summary>
/// Elige, del histórico de pólizas de SOAT, la que va al certificado (D9: solo la vigente).
/// </summary>
/// <remarks>
/// <b>Selecciona por fecha, no por el texto del estado.</b> Es la decisión que evita el defecto que ya
/// se vio en la RTM: un proveedor puede rotular varias filas con un estado que suena a bueno sin que
/// ninguna esté vigente. La fecha de vencimiento es un hecho; el rótulo es una opinión del proveedor.
///
/// <para>Orden de preferencia: (1) la que cubre hoy, (2) a falta de cobertura, la de vencimiento más
/// reciente —para que el certificado muestre "vencido el …" en vez de una celda muda—, (3) si ninguna
/// tiene fecha, la primera que aporte algún dato.</para>
/// </remarks>
public static class SoatSelection
{
    public static SoatCertification? PickCurrent(
        IReadOnlyList<SoatCertification> history, DateOnly today)
    {
        if (history.Count == 0)
            return null;

        var usable = history.Where(p => p.HasAnyValue).ToList();
        if (usable.Count == 0)
            return null;

        var covering = usable
            .Where(p => p.ValidUntil.Value >= today && (p.ValidFrom.Value is null || p.ValidFrom.Value <= today))
            .OrderByDescending(p => p.ValidUntil.Value)
            .FirstOrDefault();
        if (covering is not null)
            return covering;

        var latestExpired = usable
            .Where(p => p.ValidUntil.Value.HasValue)
            .OrderByDescending(p => p.ValidUntil.Value)
            .FirstOrDefault();

        return latestExpired ?? usable[0];
    }

    /// <summary>
    /// Vigencia real derivada de las fechas, para cuando el proveedor no declara estado o declara uno
    /// que no significa vigencia. Si no hay vencimiento no se inventa nada.
    /// </summary>
    public static VigencyStatus DeriveStatus(SoatCertification policy, DateOnly today)
    {
        if (policy.Status.HasValue)
            return policy.Status.Value;

        if (policy.ValidUntil.Value is not { } until)
            return VigencyStatus.Unknown;

        return until >= today ? VigencyStatus.Vigente : VigencyStatus.Vencido;
    }
}
