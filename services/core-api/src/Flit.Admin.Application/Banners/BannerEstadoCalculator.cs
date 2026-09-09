using Flit.Admin.Domain.Banners;

namespace Flit.Admin.Application.Banners;

public static class BannerEstadoCalculator
{
    public static BannerEstado Calculate(bool isActive, DateTimeOffset? validFrom, DateTimeOffset? validUntil, DateTimeOffset now)
    {
        if (!isActive)
        {
            return BannerEstado.Inactivo;
        }

        if (validUntil is not null && now > validUntil)
        {
            return BannerEstado.Expirado;
        }

        if (validFrom is not null && now < validFrom)
        {
            return BannerEstado.Programado;
        }

        return BannerEstado.Activo;
    }
}
