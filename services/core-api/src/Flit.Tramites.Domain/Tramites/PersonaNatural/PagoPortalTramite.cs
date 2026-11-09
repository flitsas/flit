namespace Flit.Tramites.Domain.Tramites.PersonaNatural;

/// <summary>
/// HU #14469 — pago confirmado en caja del punto de atención (canal <c>pn_punto</c>), ligado al trámite creado desde el portal.
/// Sin datos personales. <see cref="CobroId"/> es el id del cobro en el portal y la llave de idempotencia del registro.
/// </summary>
public sealed record PagoPortalTramite(
    long ValorCop,
    string Medio,
    string? MedioCaja,
    string? MedioDetalle,
    DateTimeOffset PagadoEn,
    string CobroId)
{
    public const string MedioCajaPago = "caja";
    public const string MedioSinCobro = "sin_cobro";

    private static readonly HashSet<string> MediosCaja = new(StringComparer.Ordinal) { "efectivo", "datafono", "transferencia", "otro" };

    /// <summary>
    /// Normaliza lo que entrega el portal: recorta, deja nulos los textos vacíos y rechaza lo que el DDL no admite
    /// (valor negativo, medio desconocido, medio de caja desconocido, fecha ilegible, cobro sin id). Devuelve null si no es válido.
    /// </summary>
    public static PagoPortalTramite? Crear(long valorCop, string? medio, string? medioCaja, string? medioDetalle, string? pagadoEn, string? cobroId)
    {
        var m = medio?.Trim().ToLowerInvariant();
        var caja = string.IsNullOrWhiteSpace(medioCaja) ? null : medioCaja.Trim().ToLowerInvariant();
        var detalle = string.IsNullOrWhiteSpace(medioDetalle) ? null : medioDetalle.Trim();
        var cobro = cobroId?.Trim();
        if (valorCop < 0 || string.IsNullOrEmpty(cobro) || cobro.Length > 100)
            return null;
        if (m is not (MedioCajaPago or MedioSinCobro))
            return null;
        if (caja is not null && !MediosCaja.Contains(caja))
            return null;
        if (!DateTimeOffset.TryParse(pagadoEn, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var fecha))
            return null;
        if (detalle is { Length: > 200 })
            detalle = detalle[..200];
        return new PagoPortalTramite(valorCop, m, caja, caja == "otro" ? detalle : null, fecha, cobro);
    }
}
