namespace Flit.Tramites.Application.Notifications;

/// <summary>Política de cupos de aviso de estado (espejo del jsonb de la compañía).</summary>
public sealed record TramiteStateEmailRecipientPolicy(
    bool Comprador,
    bool VendedorOPropietario,
    bool Radicador,
    string? ExtraEmail)
{
    public static TramiteStateEmailRecipientPolicy AllOn { get; } = new(true, true, true, null);
}
