using Flit.Admin.Application.Companies.MandateSigners.CreateMandateSigner;
using Flit.Admin.Domain.Companies.MandateSigners;
using Flit.Admin.Domain.Companies.TransitOffices;

namespace Flit.Admin.Application.Companies.MandateSigners.UpdateMandateSigner;

/// <summary>
/// Edición de un mandatario (RF23): valida OT operable + RF33 + exclusividad, regenera la
/// huella con la fecha de registro original y persiste con auditoría atómica (RF28). Los
/// mandatos ya emitidos conservan su huella previa (no se tocan).
///
/// HU #11764 (ADR-0050) — la edición YA NO dispara la validación de identidad, se agregue o no el
/// correo por primera vez: el módulo Identidad es la única fuente que puede originar una fila de
/// validación (y el único disparador de ese correo). El disparo que existía aquí desde la HU
/// #10993 (<c>IAdminIdentityValidationService.ResendAsync</c>, best-effort) se retira; el correo
/// se sigue capturando y persistiendo como dato de contacto.
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

        // 404 si no existe, pertenece a otro OT o ya fue inactivado (baja lógica). La identidad se
        // comprueba contra el organismo primario que el mandatario tiene HOY, que no siempre es el
        // organismo bajo el que se edita: desde el configurador de la compañía se manda la lista
        // completa de organismos y el primero de ella no tiene por qué ser el primario guardado.
        var primarioActual = command.OrganismoPrimarioActual ?? command.TransitOfficeId;
        if (signer is null || signer.TransitOfficeId != primarioActual || !signer.IsActive)
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
            await CreateMandateSignerHandler.AddExclusiveSlotErrorsAsync(
                    _reader,
                    errors,
                    command.TransitOfficeId,
                    companyIds,
                    command.TransitOfficeIds,
                    command.OfficeCompanies,
                    command.MandateSignerId,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (errors.Count > 0)
        {
            return UpdateMandateSignerResult.Invalid(errors);
        }

        var fullName = command.FullName.Trim();
        var documentNumber = command.DocumentNumber.Trim();
        var documentType = string.IsNullOrWhiteSpace(command.DocumentType) ? "CC" : command.DocumentType.Trim();
        var email = string.IsNullOrWhiteSpace(command.Email) ? null : command.Email.Trim();
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
                command.CorrelationId,
                documentType,
                email,
                command.UserId,
                command.TransitOfficeIds,
                command.PhysicalSignatureOfficeIds,
                command.SignatureVaultId,
                command.OfficeCompanies,
                command.ActualizaFirma,
                // Tras la edición, el organismo bajo el que se editó es el primario. Solo cambia algo
                // cuando la lista retira al primario anterior; en la edición desde el perfil del
                // organismo ambos coinciden y esto es un no-op.
                NuevoOrganismoPrimario: command.TransitOfficeId),
            cancellationToken).ConfigureAwait(false);

        return updated
            ? UpdateMandateSignerResult.Updated(integrityHash)
            : UpdateMandateSignerResult.NotFound();
    }
}
