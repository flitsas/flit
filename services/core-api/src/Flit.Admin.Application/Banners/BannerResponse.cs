using Flit.Admin.Domain.Banners;

namespace Flit.Admin.Application.Banners;

public sealed record BannerResponse(
    Guid Id,
    string Name,
    string ImageUrl,
    string ImageSha256,
    string? LinkUrl,
    DateTimeOffset? ValidFrom,
    DateTimeOffset? ValidUntil,
    bool IsActive,
    string Estado,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    long RowVersion)
{
    public const string EstadoProgramado = "programado";
    public const string EstadoActivo = "activo";
    public const string EstadoInactivo = "inactivo";
    public const string EstadoExpirado = "expirado";

    public static BannerResponse From(BannerListItem item, DateTimeOffset now)
    {
        var estado = BannerEstadoCalculator.Calculate(item.IsActive, item.ValidFrom, item.ValidUntil, now);
        return new BannerResponse(
            item.Id,
            item.Name,
            BuildImageUrl(item.Id),
            item.ImageSha256,
            item.LinkUrl,
            item.ValidFrom,
            item.ValidUntil,
            item.IsActive,
            ToEstadoString(estado),
            item.CreatedAt,
            item.UpdatedAt,
            item.RowVersion);
    }

    private static string BuildImageUrl(Guid id) =>
        string.Concat("/public/banners/", id.ToString(), "/image");

    private static string ToEstadoString(BannerEstado estado) => estado switch
    {
        BannerEstado.Programado => EstadoProgramado,
        BannerEstado.Activo => EstadoActivo,
        BannerEstado.Inactivo => EstadoInactivo,
        BannerEstado.Expirado => EstadoExpirado,
        _ => EstadoInactivo,
    };
}
