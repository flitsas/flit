using Flit.Modules.Platform.Application.TenantProducts;
using Flit.Modules.Platform.Domain.Companies;

namespace Flit.Modules.Platform.Application.Companies;

/// <summary>
/// HU #14694 (plan §1.3 decisión P1; D-TAXID, D-EMAIL, DA-8) — los datos que el servicio del producto enviará al alta en
/// su proveedor: razón social, NIT base (<see cref="NitEmpresa.NormalizarBase"/>) y correo del AdminCompany activo más
/// antiguo. Solo lectura; no llama al proveedor. Lo usan <c>GET …/products/{code}/alta-previa</c> (diálogo de activación)
/// y el rpc <c>flit.identidad.v1.EmpresasService.ObtenerDatosAltaProveedor</c>.
/// </summary>
public sealed class ObtenerDatosAltaProveedorHandler
{
    private readonly IDatosAltaProveedorStore _store;

    public ObtenerDatosAltaProveedorHandler(IDatosAltaProveedorStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <returns><c>null</c> si la compañía no existe.</returns>
    /// <exception cref="TenantProductException"><see cref="TenantProductException.ProductWithoutProvisioning"/> (404).</exception>
    public async Task<DatosAltaProveedor?> HandleAsync(Guid tenantId, string productCode, CancellationToken cancellationToken = default)
    {
        if (!ProductosConAprovisionamiento.Tiene(productCode))
        {
            throw new TenantProductException(
                TenantProductException.ProductWithoutProvisioning,
                $"El producto '{productCode?.Trim().ToLowerInvariant()}' no tiene aprovisionamiento.");
        }

        var empresa = await _store.ObtenerAsync(tenantId, cancellationToken).ConfigureAwait(false);
        if (empresa is null)
            return null;

        var nit = NitEmpresa.NormalizarBase(empresa.TaxId, empresa.TaxIdsAncestros);
        var correo = string.IsNullOrWhiteSpace(empresa.ContactEmail) ? null : empresa.ContactEmail.Trim();
        return new DatosAltaProveedor(
            empresa.LegalName,
            nit.Base,
            nit.Estado,
            correo,
            correo is null ? BloqueoAlta.SinAdmin : null,
            empresa.Activa);
    }
}

/// <summary>Lectura de los datos crudos de la compañía para el alta.</summary>
public interface IDatosAltaProveedorStore
{
    /// <summary>Datos de la compañía, o <c>null</c> si no existe.</summary>
    Task<DatosEmpresaParaAlta?> ObtenerAsync(Guid tenantId, CancellationToken cancellationToken = default);
}

/// <summary>Datos crudos de la compañía.</summary>
/// <param name="LegalName"><c>identity.tenants.legal_name</c>.</param>
/// <param name="TaxId"><c>identity.tenants.tax_id</c> tal cual.</param>
/// <param name="Activa"><c>identity.tenants.is_active</c>.</param>
/// <param name="TaxIdsAncestros">NIT de cada compañía ancestro, de la cabeza inmediata hacia arriba.</param>
/// <param name="ContactEmail">Correo del AdminCompany activo más antiguo; <c>null</c> si no hay.</param>
public sealed record DatosEmpresaParaAlta(
    string LegalName,
    string? TaxId,
    bool Activa,
    IReadOnlyList<string?> TaxIdsAncestros,
    string? ContactEmail);

/// <summary>Bloqueo de la activación.</summary>
public enum BloqueoAlta
{
    /// <summary>No hay AdminCompany activo (<c>SIN_ADMIN</c>).</summary>
    SinAdmin = 1,
}

/// <summary>Lo que se enviará al proveedor.</summary>
/// <param name="LegalName">Razón social.</param>
/// <param name="TaxIdBase">NIT base; <c>null</c> si <paramref name="EstadoNit"/> no es válido.</param>
/// <param name="EstadoNit">Resultado de <see cref="NitEmpresa.NormalizarBase"/>.</param>
/// <param name="ContactEmail">Correo del AdminCompany activo más antiguo; <c>null</c> si no hay.</param>
/// <param name="Bloqueo"><see cref="BloqueoAlta.SinAdmin"/> sin AdminCompany activo; <c>null</c> sin bloqueo.</param>
/// <param name="EmpresaActiva">La compañía está activa.</param>
public sealed record DatosAltaProveedor(
    string LegalName,
    string? TaxIdBase,
    EstadoNit EstadoNit,
    string? ContactEmail,
    BloqueoAlta? Bloqueo,
    bool EmpresaActiva)
{
    /// <summary>Código del bloqueo en el contrato (<c>SIN_ADMIN</c>) o <c>null</c>.</summary>
    public string? CodigoBloqueo => Bloqueo is BloqueoAlta.SinAdmin ? "SIN_ADMIN" : null;
}
