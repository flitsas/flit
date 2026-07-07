using Flit.Admin.Domain.Companies.MandateSigners;
using Flit.Admin.Domain.Companies.TransitOffices;

namespace Flit.Admin.Application.Companies.MandateSigners.UpdateMandateSigner;

/// <summary>
/// Edición de un mandatario (RF23): valida OT operable + RF33 + exclusividad, regenera la
/// huella con la fecha de registro original y persiste con auditoría atómica (RF28). Los
/// mandatos ya emitidos conservan su huella previa (no se tocan).
/// </summary>
public sealed class UpdateMandateSignerHandler
{
    private readonly ITransitOfficeOperationalStatusReader _otStatus;
    private readonly IMandateSignerReader _reader;
    private readonly IMandateSignerRepository _repository;

    public UpdateMandateSignerHandler(
        ITransitOfficeOperationalStatusReader otStatus,
        IMandateSignerReader reader,
        IMandateSignerRepository repository)
    {
        _otStatus = otStatus ?? throw new ArgumentNullException(nameof(otStatus));
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<UpdateMandateSignerResult> HandleAsync(
        UpdateMandateSignerCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var signer = await _reader
            .GetByIdAsync(command.MandateSignerId, cancellationToken).ConfigureAwait(false);

        // 404 si no existe, pertenece a otro OT o ya fue inactivado (baja lógica).
        if (signer is null || signer.TransitOfficeId != command.TransitOfficeId || !signer.IsActive)
        {
            return UpdateMandateSignerResult.NotFound();
        }

        var companyIds = command.CompanyTenantIds ?? [];

        var otStatus = await _otStatus
            .GetByIdAsync(command.TransitOfficeId, cancellationToken).ConfigureAwait(false);

        var (otTenantId, errors) = MandateSignerValidation.ValidateBase(
            otStatus, command.FullName, command.DocumentNumber, companyIds);

        if (otTenantId is not null && companyIds.Count > 0)
        {
            var otCompanies = await _reader
                .ListOtCompaniesAsync(command.TransitOfficeId, cancellationToken).ConfigureAwait(false);
            var resolutions = await _reader
                .ListActiveCompanyResolutionsAsync(command.TransitOfficeId, cancellationToken)
                .ConfigureAwait(false);

            MandateSignerValidation.ValidateCompanies(
                errors, companyIds, otCompanies, resolutions, command.MandateSignerId);
        }

        if (errors.Count > 0)
        {
            return UpdateMandateSignerResult.Invalid(errors);
        }

        var fullName = command.FullName.Trim();
        var documentNumber = command.DocumentNumber.Trim();
        // Regenera la huella con la MISMA fecha de registro original (RF: huella determinista).
        var integrityHash = MandateSignerIntegrityHash.Compute(fullName, documentNumber, signer.RegisteredAt);

        var updated = await _repository.UpdateAsync(
            new UpdateMandateSignerData(
                command.MandateSignerId,
                otTenantId!.Value,
                fullName,
                documentNumber,
                integrityHash,
                [.. companyIds.Distinct()],
                command.UpdatedBy,
                command.CorrelationId),
            cancellationToken).ConfigureAwait(false);

        return updated
            ? UpdateMandateSignerResult.Updated(integrityHash)
            : UpdateMandateSignerResult.NotFound();
    }
}
