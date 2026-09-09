namespace Flit.Admin.Domain.Banners;

/// <summary>
/// Lectura de <c>admin.banners</c> (Feature #12236, ADR-0058: tabla GLOBAL sin
/// <c>tenant_id</c>). Todas las consultas excluyen borrado logico (<c>deleted_at IS NULL</c>);
/// no hay filtro de tenant porque la tabla es la misma para todos (ADR-0058). La
/// implementacion EF Core vive en Flit.Infrastructure (BannerRepository).
/// </summary>
public interface IBannerRepository
{
    /// <summary>
    /// Banners visibles para consumo publico (HU #12240, AC1): <c>is_active = true</c> y, si
    /// tiene vigencia programada, <paramref name="nowUtc"/> dentro de [valid_from, valid_until].
    /// Un banner sin vigencia programada (ambas columnas NULL) aparece siempre que este activo.
    /// Un banner Programado (valid_from aun no llega) o Expirado (valid_until ya paso) queda
    /// excluido aunque is_active sea true.
    /// <para><paramref name="nowUtc"/> es un INSTANTE (UTC). Colombia (America/Bogota) tiene
    /// offset fijo -05:00 sin horario de verano, asi que comparar el instante UTC contra
    /// columnas <c>timestamptz</c> es equivalente a comparar en hora de Bogota — mismo
    /// argumento que <c>Flit.Infrastructure.Persistence.Repositories.BogotaDays</c> aplica a
    /// rangos de dia calendario. No hace falta convertir el reloj: alcanza con no usar hora
    /// local del servidor.</para>
    /// </summary>
    Task<IReadOnlyList<ActiveBannerItem>> ListActiveAsync(
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Referencia de storage de la imagen de un banner NO borrado logicamente (HU #12240,
    /// AC2/AC3). Devuelve <c>null</c> si el banner no existe o esta soft-deleted — el caller
    /// responde 404 sin distinguir el motivo (evita enumeracion).
    /// </summary>
    Task<BannerImageRef?> GetImageRefAsync(Guid id, CancellationToken cancellationToken = default);
}
