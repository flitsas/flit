using Flit.Admin.Domain.OtProfile;

namespace Flit.Admin.Application.OtProfile.UpdateOtProfile;

/// <summary>
/// Actualiza el perfil OT del tenant autenticado (HU #10215 AC2/AC5).
/// Al cambiar a modo <c>quipux</c> activa <c>quipux_read_only = true</c> automáticamente.
/// </summary>
public sealed class UpdateOtProfileHandler
{
    /// <summary>
    /// Código de error estable (RF05, ADR-0024) al intentar modificar campos oficiales RUNT.
    /// También lo usa la auditoría de fallo como <c>error_code</c>.
    /// </summary>
    public const string OfficialFieldsImmutableCode = "campos_oficiales_no_editables";

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

        // RF05: los campos oficiales RUNT son inmutables. Si el payload los trae —aunque sea
        // por un DTO ampliado a futuro— se rechaza sin escribir nada (guardia explícita).
        if (HasOfficialFieldChange(request))
        {
            return UpdateOtProfileResult.Invalid([
                new OtProfileValidationError(
                    OfficialFieldsImmutableCode,
                    "Los campos oficiales RUNT (razón social, NIT, código) no son editables."),
            ]);
        }

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

    /// <summary>
    /// True si el payload intenta fijar cualquier campo oficial RUNT (razón social, NIT, código).
    /// Un string vacío/espacios también cuenta como intento explícito de escritura.
    /// </summary>
    private static bool HasOfficialFieldChange(UpdateOtProfileRequest request) =>
        request.LegalName is not null || request.TaxId is not null || request.Code is not null;

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
