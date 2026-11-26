using Flit.Modules.Platform.Domain.Access;
using Flit.Modules.Platform.Domain.Products;
using Flit.Modules.Platform.Domain.TenantProducts;
using Flit.Modules.Security.Application.Products;

namespace Flit.Modules.Platform.Application.TenantProducts;

/// <summary>
/// Enciende o apaga un producto para una empresa (ADR-0063). Idempotente: repetir la misma orden no
/// escribe ni audita. La auditoría la escribe el repositorio en el mismo guardado.
/// </summary>
/// <remarks>
/// Jerarquía (ADR-0057, contrato §4): no se enciende un producto a una empresa hija si alguno de sus
/// ancestros lo tiene apagado (HU #12966). Apagar la cabeza no reescribe a las hijas: el resolutor de
/// acceso (B-05) ya las deja sin el producto, y al volver a encender la cabeza recuperan su estado.
/// HU #14694 (plan §1.1, decisión E2): en un producto con aprovisionamiento (<see cref="ProductosConAprovisionamiento"/>)
/// encender exige un AdminCompany activo (409 <see cref="TenantProductException.ProvisioningPrerequisiteMissing"/>) y todo
/// cambio deja el trabajo <c>&lt;producto&gt;.producto.cambiado</c> en la outbox del host, en el mismo guardado. El canal
/// <c>platform.tenant_product.changed</c> sigue sin emitirse (deuda R-10 del plan).
/// HU #14711 (AC-DC-03): encender <c>diagnostico</c> deja, en el mismo guardado, el alta de la credencial del impuesto en
/// <c>PENDIENTE</c> (<see cref="ITenantProductProvisioningStore.PrepararPendienteAltaDiagnosticoAsync"/>), solo si estaba apagado o
/// si la fila del alta no existe: editar las notas de un <c>diagnostico</c> encendido no reinicia el alta.
/// </remarks>
public sealed class SetTenantProductEnabledHandler
{
    /// <summary>Largo máximo de las notas (<c>varchar(500)</c> en el DDL 119).</summary>
    public const int NotesMaxLength = 500;

    /// <summary>Literal del 409 <c>PROVISIONING_PREREQUISITE_MISSING</c> (platform.v1 1.1.0); el front lo muestra tal cual.</summary>
    public const string ProvisioningPrerequisiteMissingDetail = "La compañía no tiene un Administrador de Compañía activo";

    private readonly IProductCatalog _catalog;
    private readonly ITenantProductRepository _repository;
    private readonly IProductAccessStore _access;
    private readonly ITenantProductChangeJobs _jobs;
    private readonly IPrecondicionActivacion _precondicion;
    private readonly ITenantProductProvisioningStore _provisioning;

    public SetTenantProductEnabledHandler(
        IProductCatalog catalog,
        ITenantProductRepository repository,
        IProductAccessStore access,
        ITenantProductChangeJobs jobs,
        IPrecondicionActivacion precondicion,
        ITenantProductProvisioningStore provisioning)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _access = access ?? throw new ArgumentNullException(nameof(access));
        _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
        _precondicion = precondicion ?? throw new ArgumentNullException(nameof(precondicion));
        _provisioning = provisioning ?? throw new ArgumentNullException(nameof(provisioning));
    }

    public async Task<TenantProductChange> HandleAsync(
        SetTenantProductEnabledCommand command,
        Guid? changedBy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var code = command.ProductCode.Trim().ToLowerInvariant();
        if (code == ProductCodes.Plataforma)
        {
            throw new TenantProductException(
                TenantProductException.ProductAlwaysOn,
                "La plataforma es el hub: está encendida para todas las empresas y no se habilita por empresa.");
        }

        var product = await _catalog.FindAsync(code, cancellationToken).ConfigureAwait(false)
            ?? throw new TenantProductException(
                TenantProductException.ProductNotFound,
                $"El producto '{code}' no existe.");

        // Apagar un producto inactivo sí se permite: es la forma de retirarlo de las empresas.
        if (command.Enabled && !product.IsActive)
        {
            throw new TenantProductException(
                TenantProductException.ProductInactive,
                $"El producto '{code}' está inactivo y no se puede encender.");
        }

        if (command.Enabled)
        {
            var ancestors = (await _access.GetTenantChainAsync(command.TenantId, cancellationToken).ConfigureAwait(false)).Skip(1).ToList();
            if (ancestors.Count > 0)
            {
                var enabled = await _access.GetTenantsWithProductEnabledAsync(ancestors, code, cancellationToken).ConfigureAwait(false);
                if (!ancestors.All(enabled.Contains))
                {
                    throw new TenantProductException(
                        TenantProductException.ProductNotEnabledForHead,
                        $"La cabeza de la empresa no tiene '{code}' encendido.");
                }
            }
        }

        var notes = string.IsNullOrWhiteSpace(command.Notes) ? null : command.Notes.Trim();
        if (notes is { Length: > NotesMaxLength })
        {
            throw new TenantProductException(
                TenantProductException.NotesTooLong,
                $"Las notas no pueden pasar de {NotesMaxLength} caracteres.");
        }

        // HU #14711 (AC-DC-03): encender diagnostico deja el alta de la credencial del impuesto en PENDIENTE en el mismo
        // guardado; la atiende el trabajador de core-identity. No pasa por la precondición ni por la outbox de aprovisionamiento.
        // N-3: solo al pasar de apagado (o sin fila) a encendido, o si aún no hay fila del alta; editar las notas no la reinicia.
        if (command.Enabled && string.Equals(code, TenantProductCredentialCodes.Diagnostico, StringComparison.Ordinal))
        {
            var previo = await _repository.GetAsync(command.TenantId, code, cancellationToken).ConfigureAwait(false);
            var estabaEncendido = previo is { Enabled: true };
            var preparacion = await _provisioning.PrepararPendienteAltaDiagnosticoAsync(command.TenantId, cancellationToken).ConfigureAwait(false);
            return await _repository
                .SetAsync(command.TenantId, code, command.Enabled, notes, changedBy, change =>
                {
                    if (change.Current.Enabled && (!estabaEncendido || !preparacion.FilaExiste))
                        preparacion.Marcar(change.Current.UpdatedAt);
                }, cancellationToken)
                .ConfigureAwait(false);
        }

        if (!ProductosConAprovisionamiento.Tiene(code))
        {
            return await _repository
                .SetAsync(command.TenantId, code, command.Enabled, notes, changedBy, antesDeGuardar: null, cancellationToken)
                .ConfigureAwait(false);
        }

        // HU #14694 (D-EMAIL): sin AdminCompany activo no hay a quién dar de alta en el proveedor. Apagar nunca la exige.
        if (command.Enabled
            && !await _precondicion.CumpleAsync(command.TenantId, code, cancellationToken).ConfigureAwait(false))
        {
            throw new TenantProductException(
                TenantProductException.ProvisioningPrerequisiteMissing,
                ProvisioningPrerequisiteMissingDetail);
        }

        // HU #14694 (plan §1.1, decisión E2): el trabajo viaja en el mismo guardado; sin cambio no se encola.
        return await _repository
            .SetAsync(command.TenantId, code, command.Enabled, notes, changedBy, change => _jobs.EncolarCambio(change.Current), cancellationToken)
            .ConfigureAwait(false);
    }
}

/// <summary>Orden de encender o apagar <paramref name="ProductCode"/> para <paramref name="TenantId"/>.</summary>
public sealed record SetTenantProductEnabledCommand(Guid TenantId, string ProductCode, bool Enabled, string? Notes);
