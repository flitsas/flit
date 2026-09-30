using Flit.Admin.Application.Companies.MandateSigners.CompanyMandateSigners;
using Flit.Admin.Domain.Companies.MandateSigners;
using Flit.Admin.Domain.Companies.SignatureVault;
using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Queries.Domain.Time;

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
    private readonly ISignatureVaultReader? _vaultReader;
    private readonly IMandateSignerBiometricApprovalReader? _biometricReader;

    /// <summary>Medio de firma resuelto en el alta desde el OT (solo el nombre, sin datos del baúl).</summary>
    public const string MeansVault = "baul";

    public const string MeansBiometric = "biometria";

    public const string SinMedioParaOtMessage =
        "El mandatario no está en condiciones de firmar: la persona necesita una firma vigente en el baúl "
        + "de su compañía o una validación biométrica aprobada y vigente.";

    public const string VariasCompaniasSinBaulMessage =
        "Sin indicar la firma del baúl, el mandatario solo puede registrarse para una compañía a la vez.";

    public CreateMandateSignerHandler(
        ITransitOfficeOperationalStatusReader otStatus,
        IMandateSignerReader reader,
        IMandateSignerRepository repository,
        ISignatureVaultReader? vaultReader = null,
        IMandateSignerBiometricApprovalReader? biometricReader = null)
    {
        _vaultReader = vaultReader;
        _biometricReader = biometricReader;
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
            await AddExclusiveSlotErrorsAsync(
                    _reader,
                    errors,
                    command.TransitOfficeId,
                    companyIds,
                    command.TransitOfficeIds,
                    command.OfficeCompanies,
                    currentSignerId: null,
                    command.CompanyVisibility,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        Guid? signatureVaultId = command.SignatureVaultId;
        string? signingMeans = null;
        if (errors.Count == 0 && command.ValidateSigningMeans)
        {
            var signing = await ResolveSigningMeansAsync(command, companyIds, cancellationToken)
                .ConfigureAwait(false);
            if (signing.Error is not null)
            {
                errors.Add(signing.Error);
            }
            else
            {
                signatureVaultId = signing.SignatureVaultId;
                signingMeans = signing.Means;
            }
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
                signatureVaultId,
                command.OfficeCompanies),
            cancellationToken).ConfigureAwait(false);

        // HU #11757 (ADR-0050) — el alta de un mandatario NO genera fila de validación ni correo,
        // tenga o no correo registrado: el módulo Identidad es la única fuente que puede originarla.
        // `email` se sigue capturando y persistiendo (dato de contacto del mandatario), solo se retiró
        // el disparo. El desenlace siempre es `NotAttempted` — se conserva el campo en la respuesta por
        // compatibilidad con el cliente, que ya lo tipa como uno de los cuatro valores del enum.
        return CreateMandateSignerResult.Success(
            signerId, integrityHash, MandateSignerIdentityOutcome.NotAttempted, signingMeans);
    }


    private sealed record SigningResolution(
        MandateSignerValidationError? Error, Guid? SignatureVaultId, string? Means);

    /// <summary>
    /// HU #13123 — mismas reglas que el alta de la compañía. Con <c>SignatureVaultId</c> en el request se
    /// valida esa firma contra el tenant de la compañía (existe, es de esa persona, activa y vigente).
    /// <para>
    /// <b>Ajuste:</b> el OT no ve el baúl ni lo envía, así que SIN <c>SignatureVaultId</c> el backend
    /// resuelve el medio de firma de la persona (tipo + número) dentro del tenant de la compañía destino:
    /// 1) firma vigente en el baúl de esa compañía (se vincula al mandatario) o 2) validación biométrica
    /// APROBADA y vigente en ese mismo tenant (criterio de HU #13121). En curso/vencida y el correo no
    /// cuentan; la firma física transitoria no aplica al alta del OT. Sin ninguno ⇒ 422. Con varias
    /// compañías y sin vault ⇒ 422 (la resolución es por una compañía). Solo se devuelve el nombre del
    /// medio, nunca datos del baúl.
    /// </para>
    /// </summary>
    private async Task<SigningResolution> ResolveSigningMeansAsync(
        CreateMandateSignerCommand command,
        IReadOnlyList<Guid> companyIds,
        CancellationToken cancellationToken)
    {
        var offices = command.TransitOfficeIds is { Count: > 0 }
            ? command.TransitOfficeIds.Distinct().ToList()
            : [command.TransitOfficeId];
        var distinctCompanies = companyIds.Distinct().ToList();

        if (command.SignatureVaultId is { } vaultId && vaultId != Guid.Empty)
        {
            if (distinctCompanies.Count != 1)
            {
                return new SigningResolution(
                    new MandateSignerValidationError(
                        "signatureVaultId",
                        "La firma del baúl solo puede indicarse cuando el mandatario se registra para una compañía.",
                        null),
                    null,
                    null);
            }

            var firmaError = await CreateCompanyMandateSignerHandler.ValidarFirmaAsync(
                _vaultReader,
                distinctCompanies[0],
                new CompanyMandateSignerRequest(
                    command.FullName,
                    command.DocumentNumber,
                    offices,
                    command.DocumentType,
                    command.Email,
                    SignatureVaultId: vaultId),
                cancellationToken).ConfigureAwait(false);
            if (firmaError is not null)
            {
                return new SigningResolution(firmaError, null, null);
            }

            var explicitError = MandateSignerSigningCapability.Validate(
                offices, command.PhysicalSignatureOfficeIds, vaultId);
            return new SigningResolution(explicitError, vaultId, explicitError is null ? MeansVault : null);
        }

        if (distinctCompanies.Count != 1)
        {
            return new SigningResolution(
                new MandateSignerValidationError("companyTenantIds", VariasCompaniasSinBaulMessage, null),
                null,
                null);
        }

        var companyTenant = distinctCompanies[0];
        var documentType = string.IsNullOrWhiteSpace(command.DocumentType) ? "CC" : command.DocumentType.Trim();
        var documentNumber = command.DocumentNumber.Trim();

        if (_vaultReader is not null)
        {
            var firma = await _vaultReader
                .FindActiveByDocumentAsync(companyTenant, documentType, documentNumber, cancellationToken)
                .ConfigureAwait(false);
            var hoy = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(ColombiaTime.Offset).Date);
            if (firma is not null && firma.EstaVigente(hoy))
            {
                return new SigningResolution(null, firma.Id, MeansVault);
            }
        }

        if (_biometricReader is not null
            && await _biometricReader
                .HasApprovedValidAsync(companyTenant, documentType, documentNumber, cancellationToken)
                .ConfigureAwait(false))
        {
            return new SigningResolution(null, null, MeansBiometric);
        }

        return new SigningResolution(
            new MandateSignerValidationError(MandateSignerSigningCapability.Field, SinMedioParaOtMessage, null),
            null,
            null);
    }

    internal static async Task AddExclusiveSlotErrorsAsync(
        IMandateSignerReader reader,
        List<MandateSignerValidationError> errors,
        Guid primaryOfficeId,
        IReadOnlyList<Guid> companyIds,
        IReadOnlyList<Guid>? transitOfficeIds,
        IReadOnlyList<MandateSignerOfficeCompanies>? officeCompanies,
        Guid? currentSignerId,
        OtCompanyVisibility companyVisibility,
        CancellationToken cancellationToken)
    {
        var offices = new HashSet<Guid> { primaryOfficeId };
        if (transitOfficeIds is { Count: > 0 })
        {
            foreach (var id in transitOfficeIds)
                offices.Add(id);
        }

        foreach (var officeId in offices)
        {
            var otCompanies = await reader
                .ListOtCompaniesAsync(officeId, companyVisibility, cancellationToken).ConfigureAwait(false);
            var resolutions = await reader
                .ListActiveCompanyResolutionsAsync(officeId, cancellationToken)
                .ConfigureAwait(false);
            var companiesForOffice = MandateSignerValidation.CompaniesForOffice(
                officeCompanies, officeId, companyIds);
            MandateSignerValidation.ValidateCompanies(
                errors, companiesForOffice, otCompanies, resolutions, currentSignerId);
        }
    }
}
