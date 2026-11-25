using Flit.Modules.Platform.Domain.TenantProducts;

namespace Flit.Modules.Platform.Application.TenantProducts;

/// <summary>
/// HU #14694 (plan §1.2 decisión R1; platform.v1 1.1.0 <c>POST …/aprovisionamiento/reintentar</c>) — encola el trabajo
/// <c>&lt;producto&gt;.aprovisionamiento.reintentar</c> con el id de quien lo pidió. No mira el estado de aprovisionamiento:
/// el servicio del producto ignora el trabajo fuera de ERROR, INCIERTO, REQUIERE_REVISION_NIT o LISTO_SIN_WEBHOOK
/// (impuestos.v1 1.2.1).
/// </summary>
public sealed class RetryProvisioningHandler
{
    private readonly ITenantProductRepository _repository;
    private readonly ITenantProductChangeJobs _jobs;

    public RetryProvisioningHandler(ITenantProductRepository repository, ITenantProductChangeJobs jobs)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
    }

    /// <exception cref="TenantProductException">
    /// <see cref="TenantProductException.ProductWithoutProvisioning"/> (404) o <see cref="TenantProductException.ProductNotEnabled"/> (409).
    /// </exception>
    public async Task HandleAsync(RetryProvisioningCommand command, Guid solicitadoPor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var code = (command.ProductCode ?? string.Empty).Trim().ToLowerInvariant();
        if (!ProductosConAprovisionamiento.Tiene(code))
        {
            throw new TenantProductException(
                TenantProductException.ProductWithoutProvisioning,
                $"El producto '{code}' no tiene aprovisionamiento.");
        }

        var row = await _repository.GetAsync(command.TenantId, code, cancellationToken).ConfigureAwait(false);
        if (row is not { Enabled: true })
        {
            throw new TenantProductException(
                TenantProductException.ProductNotEnabled,
                $"El producto '{code}' está apagado para la compañía.");
        }

        await _jobs.EncolarReintentoAsync(command.TenantId, code, solicitadoPor, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Orden de reintentar el aprovisionamiento de <paramref name="ProductCode"/> para <paramref name="TenantId"/>.</summary>
public sealed record RetryProvisioningCommand(Guid TenantId, string ProductCode);
