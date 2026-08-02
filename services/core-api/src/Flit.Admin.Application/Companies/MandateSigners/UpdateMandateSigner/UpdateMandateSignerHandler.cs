using Flit.Admin.Application.Identity;
using Flit.Admin.Domain.Companies.MandateSigners;
using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Admin.Domain.Identity;

namespace Flit.Admin.Application.Companies.MandateSigners.UpdateMandateSigner;

/// <summary>
/// Edición de un mandatario (RF23): valida OT operable + RF33 + exclusividad, regenera la
/// huella con la fecha de registro original y persiste con auditoría atómica (RF28). Los
/// mandatos ya emitidos conservan su huella previa (no se tocan). Si en la edición se AGREGA
/// un correo que antes no existía, apalanca la validación de identidad por correo (HU #10993,
/// best-effort: un fallo del proveedor NO revierte la edición).
/// </summary>
public sealed class UpdateMandateSignerHandler
{
    private readonly ITransitOfficeOperationalStatusReader _otStatus;
    private readonly IMandateSignerReader _reader;
    private readonly IMandateSignerRepository _repository;
    private readonly IAdminIdentityValidationService? _identityService;

    public UpdateMandateSignerHandler(
        ITransitOfficeOperationalStatusReader otStatus,
        IMandateSignerReader reader,
        IMandateSignerRepository repository,
        IAdminIdentityValidationService? identityService = null)
    {
        _otStatus = otStatus ?? throw new ArgumentNullException(nameof(otStatus));
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _identityService = identityService;
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
                // Tras la edición, el organismo bajo el que se editó es el primario. Solo cambia algo
                // cuando la lista retira al primario anterior; en la edición desde el perfil del
                // organismo ambos coinciden y esto es un no-op.
                NuevoOrganismoPrimario: command.TransitOfficeId),
            cancellationToken).ConfigureAwait(false);

        // HU #10993 — apalancar la validación de identidad también al EDITAR: si el correo se acaba de
        // agregar (antes no tenía) se dispara la validación por correo. Best-effort (un fallo del
        // proveedor NO revierte la edición) e idempotente: ResendAsync reutiliza una validación
        // aprobada y vigente y solo envía una nueva cuando no hay una activa.
        var emailNewlyAdded = string.IsNullOrWhiteSpace(signer.Email);
        if (updated && _identityService is not null && emailNewlyAdded && email is not null)
        {
            try
            {
                var descriptor = new AdminIdentitySubjectDescriptor(
                    otTenantId.Value,
                    AdminIdentitySubjectTypes.MandateSigner,
                    command.MandateSignerId,
                    fullName,
                    documentType,
                    documentNumber,
                    email,
                    command.UpdatedBy);
                await _identityService.ResendAsync(descriptor, cancellationToken).ConfigureAwait(false);
            }
            catch (AdminIdentityProviderException)
            {
                // Best-effort: la edición ya está persistida; el reenvío manual queda disponible.
            }
        }

        return updated
            ? UpdateMandateSignerResult.Updated(integrityHash)
            : UpdateMandateSignerResult.NotFound();
    }
}
