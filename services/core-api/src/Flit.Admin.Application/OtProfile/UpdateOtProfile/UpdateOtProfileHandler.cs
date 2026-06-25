using Flit.Admin.Domain.OtProfile;

namespace Flit.Admin.Application.OtProfile.UpdateOtProfile;

/// <summary>
/// Actualiza el perfil OT del tenant autenticado (HU #10215 AC2/AC5).
/// Al cambiar a modo <c>quipux</c> activa <c>quipux_read_only = true</c> automáticamente.
/// </summary>
public sealed class UpdateOtProfileHandler
{
    private readonly IOtProfileRepository _profileRepository;

    public UpdateOtProfileHandler(IOtProfileRepository profileRepository)
    {
        _profileRepository = profileRepository ?? throw new ArgumentNullException(nameof(profileRepository));
    }

    public async Task<UpdateOtProfileResult> HandleAsync(
        UpdateOtProfileCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Request);

        var request = command.Request;
        var errors = new List<OtProfileValidationError>();

        if (request.OperationMode is not null && !OtOperationModes.IsValid(request.OperationMode))
        {
            errors.Add(new OtProfileValidationError(
                "operation_mode",
                $"Valor inválido. Valores permitidos: {OtOperationModes.Dashboard}, {OtOperationModes.Quipux}."));
        }

        if (errors.Count > 0)
        {
            return UpdateOtProfileResult.Invalid(errors);
        }

        var current = await _profileRepository
            .GetByTenantAsync(command.TenantId, cancellationToken)
            .ConfigureAwait(false);

        var operationMode = request.OperationMode ?? current?.OperationMode ?? OtOperationModes.Dashboard;
        var quipuxReadOnly = ResolveQuipuxReadOnly(operationMode, request.QuipuxReadOnly, current?.QuipuxReadOnly ?? false);

        var saved = await _profileRepository.SaveAsync(
            command.TenantId,
            operationMode,
            quipuxReadOnly,
            command.ChangedBy,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return UpdateOtProfileResult.Success(OtProfileMapper.ToResponse(saved));
    }

    internal static bool ResolveQuipuxReadOnly(
        string operationMode,
        bool? requestedReadOnly,
        bool currentReadOnly)
    {
        if (operationMode == OtOperationModes.Quipux)
        {
            return true;
        }

        if (operationMode == OtOperationModes.Dashboard)
        {
            return requestedReadOnly ?? false;
        }

        return requestedReadOnly ?? currentReadOnly;
    }
}
