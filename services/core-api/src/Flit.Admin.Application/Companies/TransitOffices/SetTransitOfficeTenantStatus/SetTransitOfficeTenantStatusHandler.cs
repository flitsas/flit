using Flit.Admin.Domain.Companies.TransitOffices;

namespace Flit.Admin.Application.Companies.TransitOffices.SetTransitOfficeTenantStatus;

/// <summary>
/// Caso de uso para activar/desactivar un tenant OT (HU #10518, RF02/RF03). A diferencia
/// del genérico <c>SetCompanyStatusHandler</c>, delega en el repositorio OT que además
/// registra la auditoría de gobernanza en <c>admin.tenant_config_audit_logs</c> de forma
/// atómica e idempotente. No revoca grants (decisión producto v1).
/// </summary>
public sealed class SetTransitOfficeTenantStatusHandler
{
    private readonly ITransitOfficeTenantWriteRepository _repository;

    public SetTransitOfficeTenantStatusHandler(ITransitOfficeTenantWriteRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public Task<SetTransitOfficeTenantStatusResult> HandleAsync(
        SetTransitOfficeTenantStatusCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        return _repository.SetStatusAsync(
            command.TenantId,
            command.EstadoActivo,
            command.ChangedBy,
            command.CorrelationId,
            cancellationToken);
    }
}
