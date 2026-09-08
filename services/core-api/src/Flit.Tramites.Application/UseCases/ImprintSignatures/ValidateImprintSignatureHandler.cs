using Flit.Tramites.Application.Documents;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.ImprintSignatures;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.ImprintSignatures;

/// <summary>
/// Valida criptográficamente la firma de una impronta manual y persiste el log append-only (HU #12148).
/// </summary>
public sealed class ValidateImprintSignatureHandler
{
    private readonly IVehicleSignatureImprintRepository _imprintRepository;
    private readonly IImprintSignatureValidationRepository _validationRepository;
    private readonly IProcedureInstanceRepository _instanceRepository;
    private readonly IImprontaManualSignatureVerifier _verifier;

    public ValidateImprintSignatureHandler(
        IVehicleSignatureImprintRepository imprintRepository,
        IImprintSignatureValidationRepository validationRepository,
        IProcedureInstanceRepository instanceRepository,
        IImprontaManualSignatureVerifier verifier)
    {
        _imprintRepository = imprintRepository ?? throw new ArgumentNullException(nameof(imprintRepository));
        _validationRepository = validationRepository ?? throw new ArgumentNullException(nameof(validationRepository));
        _instanceRepository = instanceRepository ?? throw new ArgumentNullException(nameof(instanceRepository));
        _verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
    }

    public async Task<ValidateImprintSignatureResult> HandleAsync(
        ValidateImprintSignatureCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validatedAt = DateTimeOffset.UtcNow;
        var imprint = await _imprintRepository
            .GetByIdAsync(command.VehicleSignatureImprintId, cancellationToken)
            .ConfigureAwait(false);

        if (imprint is null)
        {
            return new ValidateImprintSignatureResult
            {
                ValidationId = Guid.Empty,
                VehicleSignatureImprintId = command.VehicleSignatureImprintId,
                Result = ImprintSignatureValidationResults.NotFound,
                FailureReason = "Impronta firmada no encontrada.",
                ValidatedAt = validatedAt,
            };
        }

        var isValid = _verifier.Verify(imprint.PublicKey, imprint.DocumentHash, imprint.Signature);
        var result = isValid
            ? ImprintSignatureValidationResults.Valid
            : ImprintSignatureValidationResults.Invalid;
        var failureReason = isValid
            ? null
            : "La firma RSA no coincide con el hash del documento registrado.";

        // Placa del documento: el trámite suele vivir en tenant compañía, no en el OT validador.
        var instance = await _instanceRepository
            .GetByIdAsync(imprint.ProcedureInstanceId, imprint.TenantId, cancellationToken)
            .ConfigureAwait(false);
        var placa = NormalizePlacaSnapshot(instance?.Plate);

        var log = new ImprintSignatureValidation
        {
            Id = Guid.NewGuid(),
            // Tenant del validador (OT/sesión) para RLS de la bitácora; la firma sigue ligada al documento.
            TenantId = command.TenantId,
            VehicleSignatureImprintId = imprint.Id,
            ProcedureInstanceId = imprint.ProcedureInstanceId,
            Placa = placa,
            ValidatedBy = command.ValidatedBy,
            ValidatedAt = validatedAt,
            Result = result,
            FailureReason = failureReason,
        };

        _validationRepository.Add(log);
        await _validationRepository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new ValidateImprintSignatureResult
        {
            ValidationId = log.Id,
            VehicleSignatureImprintId = imprint.Id,
            Result = result,
            FailureReason = failureReason,
            ValidatedAt = validatedAt,
        };
    }

    private static string NormalizePlacaSnapshot(string? plate) =>
        string.IsNullOrWhiteSpace(plate) ? "-" : plate.Trim().ToUpperInvariant();
}
