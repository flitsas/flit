namespace Flit.Admin.Domain.Companies.Settings;

/// <summary>
/// Destinatarios combinables de avisos aprobado/rechazado (Feature #11791 local).
/// </summary>
public sealed record TramiteStateEmailRecipients(
    bool Comprador,
    bool VendedorOPropietario,
    bool Radicador,
    string? ExtraEmail)
{
    public static TramiteStateEmailRecipients AllOn => new(true, true, true, null);

    public static TramiteStateEmailRecipients FromJson(
        bool comprador, bool vendedorOPropietario, bool radicador, string? extraEmail) =>
        new(comprador, vendedorOPropietario, radicador, NormalizeExtra(extraEmail));

    public static string? NormalizeExtra(string? extraEmail)
    {
        if (string.IsNullOrWhiteSpace(extraEmail))
            return null;
        return extraEmail.Trim();
    }
}
