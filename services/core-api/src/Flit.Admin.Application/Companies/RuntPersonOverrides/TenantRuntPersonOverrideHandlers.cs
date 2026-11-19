namespace Flit.Admin.Application.Companies.RuntPersonOverrides;

/// <summary>
/// Resultado de creación o actualización de un override de persona RUNT.
/// </summary>
public sealed record TenantRuntPersonOverrideResult(
    bool Success,
    TenantRuntPersonOverrideView? Override,
    IReadOnlyList<TenantRuntPersonOverrideValidationError> Errors);

/// <summary>
/// Caso de uso: listar overrides de persona RUNT de un tenant (HU #14642).
/// </summary>
public sealed class ListTenantRuntPersonOverridesHandler(ITenantRuntPersonOverrideRepository repository)
{
    public Task<IReadOnlyList<TenantRuntPersonOverrideView>> HandleAsync(
        Guid tenantId,
        string? documentTypeFilter,
        CancellationToken ct)
        => repository.ListByTenantAsync(tenantId, documentTypeFilter, ct);
}

/// <summary>
/// Caso de uso: crear un override de persona RUNT (HU #14642).
/// Valida antes de persistir; devuelve errores de validación sin lanzar excepción.
/// </summary>
public sealed class CreateTenantRuntPersonOverrideHandler(ITenantRuntPersonOverrideRepository repository)
{
    public async Task<TenantRuntPersonOverrideResult> HandleAsync(
        CreateTenantRuntPersonOverrideRequest request,
        CancellationToken ct)
    {
        var errors = TenantRuntPersonOverrideValidator.ValidateCreate(request);
        if (errors.Count > 0)
            return new TenantRuntPersonOverrideResult(false, null, errors);

        try
        {
            var created = await repository.CreateAsync(request, ct).ConfigureAwait(false);
            return new TenantRuntPersonOverrideResult(true, created, []);
        }
        catch (DuplicateRuntOverrideException ex)
        {
            return new TenantRuntPersonOverrideResult(
                false, null,
                [new TenantRuntPersonOverrideValidationError(
                    "documentNumber",
                    $"Ya existe un override activo para {ex.DocumentType}/{ex.DocumentNumber} en este tenant.")]);
        }
    }
}

/// <summary>
/// Caso de uso: actualizar o desactivar un override de persona RUNT (HU #14642).
/// </summary>
public sealed class UpdateTenantRuntPersonOverrideHandler(ITenantRuntPersonOverrideRepository repository)
{
    public async Task<TenantRuntPersonOverrideResult> HandleAsync(
        Guid id,
        UpdateTenantRuntPersonOverrideRequest request,
        CancellationToken ct)
    {
        var errors = TenantRuntPersonOverrideValidator.ValidateUpdate(request);
        if (errors.Count > 0)
            return new TenantRuntPersonOverrideResult(false, null, errors);

        var updated = await repository.UpdateAsync(id, request, ct).ConfigureAwait(false);

        return updated is null
            ? new TenantRuntPersonOverrideResult(false, null,
                [new TenantRuntPersonOverrideValidationError("id", "Override no encontrado.")])
            : new TenantRuntPersonOverrideResult(true, updated, []);
    }
}
