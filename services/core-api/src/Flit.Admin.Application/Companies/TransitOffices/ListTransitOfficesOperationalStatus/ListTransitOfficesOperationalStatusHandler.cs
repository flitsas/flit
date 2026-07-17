using Flit.Admin.Domain.Companies.TransitOffices;

namespace Flit.Admin.Application.Companies.TransitOffices.ListTransitOfficesOperationalStatus;

/// <summary>
/// Caso de uso del listado de estado operativo de organismos de tránsito (RF01): por
/// cada oficina del catálogo indica si tiene tenant OT y su estado (activo/inactivo).
/// Alimenta la columna Estado y las acciones activar/desactivar/alta del listado
/// SuperAdmin. Delega la lectura cross-tenant al repositorio.
/// </summary>
public sealed class ListTransitOfficesOperationalStatusHandler
{
    private readonly ITransitOfficeOperationalStatusReader _reader;

    public ListTransitOfficesOperationalStatusHandler(ITransitOfficeOperationalStatusReader reader)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
    }

    public async Task<IReadOnlyList<TransitOfficeOperationalStatusResponse>> HandleAsync(
        ListTransitOfficesOperationalStatusQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var items = await _reader.ListAsync(cancellationToken).ConfigureAwait(false);

        return
        [
            .. items.Select(o => new TransitOfficeOperationalStatusResponse(
                o.Id,
                o.Code,
                o.Name,
                o.DepartmentCode,
                o.HasTenant,
                o.TenantId,
                o.EstadoActivo,
                o.OperationMode,
                o.DivipoCode,
                o.QuipuxRegistration,
                o.QuipuxTransfer,
                o.QuipuxOther)),
        ];
    }
}
