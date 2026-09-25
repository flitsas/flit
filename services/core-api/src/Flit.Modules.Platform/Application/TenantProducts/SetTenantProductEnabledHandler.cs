using Flit.Modules.Platform.Domain.Products;
using Flit.Modules.Platform.Domain.TenantProducts;
using Flit.Modules.Security.Application.Products;

namespace Flit.Modules.Platform.Application.TenantProducts;

/// <summary>
/// Enciende o apaga un producto para una empresa (ADR-0063). Idempotente: repetir la misma orden no
/// escribe ni audita. La auditoría la escribe el repositorio en el mismo guardado.
/// </summary>
/// <remarks>
/// Fuera de B-03, a propósito: la regla de jerarquía (una hija solo tiene lo que su cabeza tiene
/// encendido) la aplica el resolutor de acceso en B-05, y el evento
/// <c>platform.tenant_product.changed</c> se publica cuando exista el outbox del frente C.
/// </remarks>
public sealed class SetTenantProductEnabledHandler
{
    /// <summary>Largo máximo de las notas (<c>varchar(500)</c> en el DDL 119).</summary>
    public const int NotesMaxLength = 500;

    private readonly IProductCatalog _catalog;
    private readonly ITenantProductRepository _repository;

    public SetTenantProductEnabledHandler(IProductCatalog catalog, ITenantProductRepository repository)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
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
