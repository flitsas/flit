namespace Flit.Admin.Domain.Companies.TransitOffices;

/// <summary>
/// Escritura de la parametrización Quipux de una secretaría del catálogo (HU #10710):
/// <c>catalogs.transit_offices.divipo_code</c> + las tres banderas por familia de trámite.
/// Es una escritura de SuperAdmin sobre un catálogo global (no multi-tenant), análoga en
/// alcance a <see cref="ITransitOfficeOperationalStatusReader"/>.
/// </summary>
/// <remarks>
/// No confundir con <c>admin.transit_office_profiles.operation_mode</c>: aquello describe al
/// OT-CLIENTE de FLIT (consola en solo lectura). Esto describe a la secretaría DESTINO a la
/// que FLIT radica, que normalmente ni siquiera es cliente de FLIT.
/// </remarks>
public interface ITransitOfficeQuipuxSettingsWriter
{
    /// <summary>
    /// Reemplaza la parametrización Quipux de la oficina. <paramref name="divipoCode"/> en
    /// <c>null</c> deja la secretaría como no elegible para radicar (estado normal mientras
    /// no se conozca el código). Devuelve <c>false</c> si la oficina no existe o no está
    /// activa en el catálogo.
    /// </summary>
    Task<bool> UpdateQuipuxSettingsAsync(
        Guid transitOfficeId,
        string? divipoCode,
        bool quipuxRegistration,
        bool quipuxTransfer,
        bool quipuxOther,
        CancellationToken cancellationToken = default);
}
