using Flit.Admin.Domain.OtRequirements;

namespace Flit.Admin.Application.OtRequirements.UpdateOtRequirements;

/// <summary>
/// Configura los requisitos del OT del tenant (HU #10546 AC1/AC2). Hace merge con el estado actual
/// (los campos nulos se conservan) y persiste; la auditoría la asegura el trigger de BD.
/// </summary>
public sealed class UpdateOtRequirementsHandler
{
    private readonly IOtRequirementsProvider _provider;
    private readonly IOtRequirementsRepository _repository;

    public UpdateOtRequirementsHandler(
        IOtRequirementsProvider provider,
        IOtRequirementsRepository repository)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<UpdateOtRequirementsResult> HandleAsync(
        UpdateOtRequirementsCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Request);

        var current = await _provider
            .ResolveByTenantAsync(command.TenantId, cancellationToken)
            .ConfigureAwait(false);

        var request = command.Request;
        var saved = await _repository.SaveAsync(
            command.TenantId,
            request.RequiresRnmc ?? current.RequiresRnmc,
            request.AllowPlatePreassign ?? current.AllowPlatePreassign,
            request.IdentityValidationEnabled ?? current.IdentityValidationEnabled,
            command.ChangedBy,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return UpdateOtRequirementsResult.Success(OtRequirementsMapper.ToResponse(saved));
    }
}
