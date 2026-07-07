using Flit.Admin.Domain.Companies.TransitOffices;

namespace Flit.Admin.Tests.TestDoubles;

/// <summary>
/// Test double en memoria de <see cref="ITransitOfficeOperationalStatusReader"/> para los
/// tests de aplicación (HU #10518), sin depender de <c>DbTransitOfficeOperationalStatusReader</c>
/// / EF Core. Por defecto reporta cualquier oficina como OPERATIVA (tenant OT activo); se
/// pueden fijar estados concretos (sin alta / inactivo) con <see cref="Set"/> o
/// <see cref="SetNotInCatalog"/>.
///
/// Uso de ejemplo:
/// <code>
/// var reader = new StubTransitOfficeOperationalStatusReader();
/// reader.Set(officeId, hasTenant: true, estadoActivo: false); // OT inactivo
/// </code>
/// </summary>
public sealed class StubTransitOfficeOperationalStatusReader : ITransitOfficeOperationalStatusReader
{
    private readonly Dictionary<Guid, TransitOfficeOperationalStatusItem?> _byId = new();

    /// <summary>Estado por defecto para oficinas no configuradas: operativo (tenant activo).</summary>
    public bool DefaultOperable { get; set; } = true;

    /// <summary>Fija un estado operativo concreto para una oficina.</summary>
    public StubTransitOfficeOperationalStatusReader Set(Guid officeId, bool hasTenant, bool? estadoActivo)
    {
        _byId[officeId] = new TransitOfficeOperationalStatusItem
        {
            Id = officeId,
            Code = "TEST",
            Name = "OT de prueba",
            DepartmentCode = "00",
            HasTenant = hasTenant,
            TenantId = hasTenant ? Guid.NewGuid() : null,
            EstadoActivo = hasTenant ? estadoActivo : null,
            OperationMode = hasTenant ? "dashboard" : null,
        };
        return this;
    }

    /// <summary>Marca una oficina como inexistente/ inactiva en el catálogo (GetById → null).</summary>
    public StubTransitOfficeOperationalStatusReader SetNotInCatalog(Guid officeId)
    {
        _byId[officeId] = null;
        return this;
    }

    public Task<IReadOnlyList<TransitOfficeOperationalStatusItem>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<TransitOfficeOperationalStatusItem> items =
            [.. _byId.Values.Where(v => v is not null).Select(v => v!)];
        return Task.FromResult(items);
    }

    public Task<TransitOfficeOperationalStatusItem?> GetByIdAsync(
        Guid transitOfficeId,
        CancellationToken cancellationToken = default)
    {
        if (_byId.TryGetValue(transitOfficeId, out var item))
        {
            return Task.FromResult(item);
        }

        // No configurada → comportamiento por defecto.
        return Task.FromResult<TransitOfficeOperationalStatusItem?>(
            DefaultOperable
                ? new TransitOfficeOperationalStatusItem
                {
                    Id = transitOfficeId,
                    Code = "TEST",
                    Name = "OT de prueba",
                    DepartmentCode = "00",
                    HasTenant = true,
                    TenantId = Guid.NewGuid(),
                    EstadoActivo = true,
                    OperationMode = "dashboard",
                }
                : null);
    }
}
