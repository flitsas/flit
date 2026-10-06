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
/// El evento <c>platform.tenant_product.changed</c> se publica cuando exista el outbox (C-01).
/// </remarks>
public sealed class SetTenantProductEnabledHandler
{
    /// <summary>Largo máximo de las notas (<c>varchar(500)</c> en el DDL 119).</summary>
    public const int NotesMaxLength = 500;

    private readonly IProductCatalog _catalog;
    private readonly ITenantProductRepository _repository;
    private readonly IProductAccessStore _access;

    public SetTenantProductEnabledHandler(IProductCatalog catalog, ITenantProductRepository repository, IProductAccessStore access)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _access = access ?? throw new ArgumentNullException(nameof(access));
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

        return await _repository
            .SetAsync(command.TenantId, code, command.Enabled, notes, changedBy, cancellationToken)
            .ConfigureAwait(false);
    }
}

/// <summary>Orden de encender o apagar <paramref name="ProductCode"/> para <paramref name="TenantId"/>.</summary>
public sealed record SetTenantProductEnabledCommand(Guid TenantId, string ProductCode, bool Enabled, string? Notes);
