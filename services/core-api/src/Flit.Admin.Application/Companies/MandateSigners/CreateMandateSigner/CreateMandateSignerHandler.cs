using Flit.Admin.Domain.Companies.MandateSigners;
using Flit.Admin.Domain.Companies.TransitOffices;

namespace Flit.Admin.Application.Companies.MandateSigners.CreateMandateSigner;

/// <summary>
/// Alta de un mandatario (RF22, ampliado por ADR-0036): valida OT operable, RF33 (compañías
/// activas/no bloqueadas), autogenera la huella de integridad y persiste con auditoría atómica
/// (RF28).
///
/// HU #11757 (ADR-0050) — el alta YA NO dispara la validación de identidad, tenga o no correo:
/// el módulo Identidad es la única fuente que puede originar una fila de validación (y el único
/// disparador de ese correo). El disparo que existía aquí desde la HU #10911/#11000
/// (<c>IAdminIdentityValidationService.EnsureAsync</c>, best-effort) se retira; el resultado del
/// alta siempre reporta <see cref="MandateSignerIdentityOutcome.NotAttempted"/>.
/// </summary>
public sealed class CreateMandateSignerHandler
{
    private readonly ITransitOfficeOperationalStatusReader _otStatus;
    private readonly IMandateSignerReader _reader;
    private readonly IMandateSignerRepository _repository;

    public CreateMandateSignerHandler(
        ITransitOfficeOperationalStatusReader otStatus,
        IMandateSignerReader reader,
        IMandateSignerRepository repository)
    {
        _otStatus = otStatus ?? throw new ArgumentNullException(nameof(otStatus));
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
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
                command.UserId,
                command.TransitOfficeIds,
                command.PhysicalSignatureOfficeIds,
                command.SignatureVaultId,
                command.OfficeCompanies),
            cancellationToken).ConfigureAwait(false);

        // HU #11757 (ADR-0050) — el alta de un mandatario NO genera fila de validación ni correo,
        // tenga o no correo registrado: el módulo Identidad es la única fuente que puede originarla.
        // `email` se sigue capturando y persistiendo (dato de contacto del mandatario), solo se retiró
        // el disparo. El desenlace siempre es `NotAttempted` — se conserva el campo en la respuesta por
        // compatibilidad con el cliente, que ya lo tipa como uno de los cuatro valores del enum.
        return CreateMandateSignerResult.Success(signerId, integrityHash);
    }
}
