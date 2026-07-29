using Flit.Admin.Application.Identity;
using Flit.Admin.Domain.Companies.MandateSigners;
using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Admin.Domain.Identity;

namespace Flit.Admin.Application.Companies.MandateSigners.CreateMandateSigner;

/// <summary>
/// Alta de un mandatario (RF22, ampliado por ADR-0036): valida OT operable, RF33 (compañías
/// activas/no bloqueadas), autogenera la huella de integridad y persiste con auditoría atómica
/// (RF28). Tras persistir, si trae correo, inicia la validación de identidad del mandatario por
/// correo (HU #10911, best-effort: un fallo del proveedor NO revierte el alta).
/// </summary>
public sealed class CreateMandateSignerHandler
{
    private readonly ITransitOfficeOperationalStatusReader _otStatus;
    private readonly IMandateSignerReader _reader;
    private readonly IMandateSignerRepository _repository;
    private readonly IAdminIdentityValidationService? _identityService;

    public CreateMandateSignerHandler(
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

    public async Task<CreateMandateSignerResult> HandleAsync(
        CreateMandateSignerCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

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

            MandateSignerValidation.ValidateCompanies(errors, companyIds, otCompanies, resolutions, null);
        }

        if (errors.Count > 0)
        {
            return CreateMandateSignerResult.Invalid(errors);
        }

        var registeredAt = DateTimeOffset.UtcNow;
        var fullName = command.FullName.Trim();
        var documentNumber = command.DocumentNumber.Trim();
        var documentType = string.IsNullOrWhiteSpace(command.DocumentType) ? "CC" : command.DocumentType.Trim();
        var email = string.IsNullOrWhiteSpace(command.Email) ? null : command.Email.Trim();
        var integrityHash = MandateSignerIntegrityHash.Compute(fullName, documentNumber, registeredAt);

        var signerId = await _repository.CreateAsync(
            new CreateMandateSignerData(
                command.TransitOfficeId,
                otTenantId!.Value,
                fullName,
                documentNumber,
                integrityHash,
                registeredAt,
                [.. companyIds.Distinct()],
                command.CreatedBy,
                command.CorrelationId,
                documentType,
                email,
                command.UserId),
            cancellationToken).ConfigureAwait(false);

        // HU #10911 — envío de validación de identidad al registrar (best-effort). Un fallo del
        // proveedor NO revierte el alta: el mandatario queda creado y el reenvío queda disponible.
        // HU #11000 — se APALANCA la identidad de la PERSONA en vez de arrancar siempre una validación
        // nueva: EnsureAsync reutiliza la aprobada y vigente del mismo documento (aunque se haya validado
        // como representante legal u otro mandatario), la ancla a este mandatario y solo envía el correo
        // cuando no hay ninguna vigente.
        var identity = MandateSignerIdentityOutcome.NotAttempted;
        if (_identityService is not null && email is not null)
        {
            try
            {
                var descriptor = new AdminIdentitySubjectDescriptor(
                    otTenantId.Value,
                    AdminIdentitySubjectTypes.MandateSigner,
                    signerId,
                    fullName,
                    documentType,
                    documentNumber,
                    email,
                    command.CreatedBy);
                var result = await _identityService
                    .EnsureAsync(descriptor, cancellationToken).ConfigureAwait(false);
                identity = result.Reused
                    ? MandateSignerIdentityOutcome.Reused
                    : MandateSignerIdentityOutcome.Sent;
            }
            catch (AdminIdentityProviderException)
            {
                // Best-effort: el alta ya está persistida; el reenvío manual queda disponible.
                identity = MandateSignerIdentityOutcome.Failed;
            }
        }

        return CreateMandateSignerResult.Success(signerId, integrityHash, identity);
    }
}
