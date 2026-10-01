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
/// HU #11757 (ADR-0050) retiró el disparo antiguo (<c>IAdminIdentityValidationService.EnsureAsync</c>). HU #13246
/// (Feature #13245) lo restablece de forma acotada y por el flujo del trámite: un mandatario Persona natural con forma de
/// firma biometría recibe SU validación de identidad (party_role mandatario + referencia a la ficha) al crearse, desde
/// la compañía y desde el hub OT; el resultado reporta <see cref="MandateSignerIdentityOutcome"/>.
///
/// HU #13129 (ADR-0061) — el alta valida el modelo del mandatario (natural, jurídica, formato en blanco),
/// la forma de firma (baúl o biometría) y la vigencia propia (fija o por rango) con 422 por campo.
/// </summary>
public sealed class CreateMandateSignerHandler
{
    private readonly ITransitOfficeOperationalStatusReader _otStatus;
    private readonly IMandateSignerReader _reader;
    private readonly IMandateSignerRepository _repository;
    private readonly ISignatureVaultReader? _vaultReader;
    private readonly IMandateSignerIdentityLauncher? _identityLauncher;
    private readonly IMandatarioAssociableCompanies? _associable;

    /// <summary>Medio de firma resuelto en el alta desde el OT (solo el nombre, sin datos del baúl).</summary>
    public const string MeansVault = "baul";

    public const string MeansBiometric = "biometria";

    public const string SinMedioParaOtMessage =
        "El mandatario no está en condiciones de firmar: elija la forma de firma validación de identidad "
        + "o cargue su firma vigente en el baúl de su compañía.";

    public const string SinBaulParaOtMessage =
        "El mandatario no tiene una firma vigente en el baúl de su compañía. Elija la forma de firma "
        + "biometría o cargue su firma en el baúl.";

    public const string VariasCompaniasSinBaulMessage =
        "Sin indicar la firma del baúl, el mandatario solo puede registrarse para una compañía a la vez.";

    public CreateMandateSignerHandler(
        ITransitOfficeOperationalStatusReader otStatus,
        IMandateSignerReader reader,
        IMandateSignerRepository repository,
        ISignatureVaultReader? vaultReader = null,
        IMandateSignerIdentityLauncher? identityLauncher = null,
        IMandatarioAssociableCompanies? associable = null)
    {
        _associable = associable;
        _vaultReader = vaultReader;
        _identityLauncher = identityLauncher;
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

        // HU #13179 — compañías asociadas: 403 si el Admin de Compañía envía una que no es su hija (nada se
        // guarda); 422 por elemento para propia, inactiva o inexistente; RF33 para el OT.
        var mandatarioOffices = command.TransitOfficeIds is { Count: > 0 }
            ? command.TransitOfficeIds.ToHashSet()
            : [command.TransitOfficeId];
        var associationErrors = await MandateSignerAssociationRules.ValidateAsync(
                _associable, _reader, command.OfficeCompanies, companyIds, mandatarioOffices,
                command.ConfiguredByScope, cancellationToken)
            .ConfigureAwait(false);

        var otStatus = await _otStatus
            .GetByIdAsync(command.TransitOfficeId, cancellationToken).ConfigureAwait(false);

        // Alta del OT sin forma de firma explícita (natural): se conserva la resolución en servidor de
        // HU #13123 (baúl vigente; desde la HU #13246 la biometría debe elegirse explícita: ninguna aprobación previa del documento
        // la habilita) y se fija la forma con el medio hallado.
        var method = command.SignatureMethod;
        SigningResolution? inferred = null;
        var modelRaw = command.SignerModel?.Trim().ToLowerInvariant();
        if (command.ValidateSigningMeans
            && string.IsNullOrWhiteSpace(method)
            && (string.IsNullOrEmpty(modelRaw) || modelRaw == MandateSignerModels.Natural)
            && !string.IsNullOrWhiteSpace(command.DocumentNumber)
            && companyIds.Count > 0)
        {
            var dummy = new MandateSignerProfile(
                MandateSignerModels.Natural, null, MandateValidityKinds.Fixed, null, null,
                command.FullName, string.IsNullOrWhiteSpace(command.DocumentType) ? "CC" : command.DocumentType.Trim(),
                command.DocumentNumber.Trim());
            inferred = await ResolveSigningMeansAsync(command, dummy, companyIds, cancellationToken)
                .ConfigureAwait(false);
            if (inferred.Error is null)
            {
                method = inferred.Means;
            }
        }

        // HU #13129 — modelo, forma de firma y vigencia (422 por campo).
        var (profile, profileErrors) = MandateSignerModelRules.Evaluate(new MandateSignerProfileInput(
            command.SignerModel,
            method,
            command.ValidityKind,
            command.ValidFrom,
            command.ValidTo,
            command.FullName,
            command.DocumentType,
            command.DocumentNumber,
            command.Email,
            command.SignatureVaultId));

        var (otTenantId, errors) = MandateSignerValidation.ValidateBase(
            otStatus,
            profile.FullName,
            profile.DocumentNumber,
            companyIds,
            documentRequired: profile.Model != MandateSignerModels.FormatoBlanco);
        if (inferred?.Error is not null)
        {
            errors.Add(inferred.Error);
        }
        else
        {
            errors.AddRange(profileErrors);
        }

        errors.AddRange(associationErrors);

        if (otTenantId is not null && companyIds.Count > 0)
        {
            await AddExclusiveSlotErrorsAsync(
                    _reader,
                    errors,
                    command.TransitOfficeId,
                    companyIds,
                    command.TransitOfficeIds,
                    currentSignerId: null,
                    command.CompanyVisibility,
                    cancellationToken,
                    associable: _associable,
                    configuredByScope: command.ConfiguredByScope)
                .ConfigureAwait(false);
        }

        // Solo la Persona natural con baúl conserva el vínculo a la firma del baúl.
        Guid? signatureVaultId = profile.SignatureMethod == MandateSignatureMethods.Baul
            ? command.SignatureVaultId
            : null;
        string? signingMeans = profile.SignatureMethod;
        if (errors.Count == 0 && command.ValidateSigningMeans && profile.IsNatural)
        {
            var signing = inferred is { Error: null }
                ? inferred
                : await ResolveSigningMeansAsync(command, profile, companyIds, cancellationToken)
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
        var fullName = profile.FullName;
        var documentNumber = profile.DocumentNumber;
        var documentType = profile.DocumentType;
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
                null, // HU #13131: la firma física ya no se persiste como exención.
                signatureVaultId,
                command.OfficeCompanies,
                profile.Model,
                profile.SignatureMethod,
                profile.ValidityKind,
                profile.ValidFrom,
                profile.ValidTo,
                command.ConfiguredByScope),
            cancellationToken).ConfigureAwait(false);

        // HU #13246 (decisión 2 del Líder Técnico) — Persona natural con forma de firma biometría: se lanza SU validación de
        // identidad por el flujo del trámite, en el tenant de la COMPAÑÍA del mandatario (también cuando lo crea el OT; sin
        // compañías vinculadas, el del organismo como respaldo). Si el proveedor falla de forma transitoria el mandatario ya
        // está guardado y la validación queda encolada; nunca se revierte el alta.
        var identity = MandateSignerIdentityOutcome.NotAttempted;
        if (MandateSignerIdentityLaunch.RequiresValidation(profile.Model, profile.SignatureMethod ?? signingMeans))
        {
            var launchTenant = companyIds.Count > 0 ? companyIds[0] : otTenantId!.Value;
            identity = await MandateSignerIdentityLaunch
                .TryLaunchAsync(
                    _identityLauncher, signerId, launchTenant, documentType, documentNumber, fullName, email,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return CreateMandateSignerResult.Success(signerId, integrityHash, identity, signingMeans);
    }


    private sealed record SigningResolution(
        MandateSignerValidationError? Error, Guid? SignatureVaultId, string? Means);

    /// <summary>
    /// HU #13123 + HU #13129 — mismas reglas que el alta de la compañía, ahora con forma de firma explícita
    /// (solo Persona natural).
    /// <para>
    /// <b>Biometría:</b> se guarda sin exigir una validación aprobada; la origina y vigila el módulo
    /// Identidad (HU #13130; una aprobación basta, sin ventana de 30 días, HU #13130b). <b>Baúl:</b> con <c>SignatureVaultId</c> se valida esa firma contra el
    /// tenant de la compañía (existe, es de esa persona, activa y vigente); sin él, el backend resuelve la
    /// firma vigente de la persona (tipo + número) en el baúl de la compañía destino (el OT no ve el baúl).
    /// Sin firma ⇒ 422 <c>signatureVaultId</c>. Con varias compañías y sin vault ⇒ 422. La firma física
    /// transitoria no aplica al alta del OT. Solo se devuelve el nombre del medio.
    /// </para>
    /// </summary>
    private async Task<SigningResolution> ResolveSigningMeansAsync(
        CreateMandateSignerCommand command,
        MandateSignerProfile profile,
        IReadOnlyList<Guid> companyIds,
        CancellationToken cancellationToken)
    {
        var offices = command.TransitOfficeIds is { Count: > 0 }
            ? command.TransitOfficeIds.Distinct().ToList()
            : [command.TransitOfficeId];
        var distinctCompanies = companyIds.Distinct().ToList();
        var method = profile.SignatureMethod;

        if (method != MandateSignatureMethods.Biometria
            && command.SignatureVaultId is { } vaultId && vaultId != Guid.Empty)
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
            return firmaError is not null
                ? new SigningResolution(firmaError, null, null)
                : new SigningResolution(null, vaultId, MeansVault);
        }

        // HU #13246 — con biometría no se exige nada previo: la validación propia se lanza tras guardar y la
        // aprobación de un comprador, un vendedor o una prevalidación con el mismo documento NO cuenta.
        if (method == MandateSignatureMethods.Biometria)
        {
            return new SigningResolution(null, null, MeansBiometric);
        }

        if (distinctCompanies.Count != 1)
        {
            return new SigningResolution(
                new MandateSignerValidationError("companyTenantIds", VariasCompaniasSinBaulMessage, null),
                null,
                null);
        }

        var companyTenant = distinctCompanies[0];
        var documentNumber = profile.DocumentNumber ?? string.Empty;

        if (method != MandateSignatureMethods.Biometria && _vaultReader is not null)
        {
            var firma = await _vaultReader
                .FindActiveByDocumentAsync(companyTenant, profile.DocumentType, documentNumber, cancellationToken)
                .ConfigureAwait(false);
            var hoy = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(ColombiaTime.Offset).Date);
            if (firma is not null && firma.EstaVigente(hoy))
            {
                return new SigningResolution(null, firma.Id, MeansVault);
            }
        }

        return method == MandateSignatureMethods.Baul
            ? new SigningResolution(
                new MandateSignerValidationError(
                    MandateSignerSigningCapability.FieldVault, SinBaulParaOtMessage, null), null, null)
            : new SigningResolution(
                new MandateSignerValidationError(
                    MandateSignerSigningCapability.Field, SinMedioParaOtMessage, null), null, null);
    }

    internal static async Task AddExclusiveSlotErrorsAsync(
        IMandateSignerReader reader,
        List<MandateSignerValidationError> errors,
        Guid primaryOfficeId,
        IReadOnlyList<Guid> companyIds,
        IReadOnlyList<Guid>? transitOfficeIds,
        Guid? currentSignerId,
        OtCompanyVisibility companyVisibility,
        CancellationToken cancellationToken,
        IMandatarioAssociableCompanies? associable = null,
        string configuredByScope = "organismo")
    {
        // HU #13182b (D3, P7 del PO) — el OT / Super Admin registra el mandatario de CUALQUIER compañía gestora
        // activa aunque no esté habilitada en el organismo; la inexistente o inactiva se sigue rechazando. El Admin
        // de Compañía (origen «compania») conserva RF33 con el grant del organismo.
        HashSet<Guid>? validas = null;
        if (associable is not null
            && !string.Equals(configuredByScope, "compania", StringComparison.Ordinal))
        {
            var rechazos = await associable
                .RejectionsAsync(null, [], companyIds.Distinct().ToList(), cancellationToken)
                .ConfigureAwait(false);
            validas = [.. companyIds.Where(id => !rechazos.ContainsKey(id))];
        }

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
            // HU #13179 — solo las compañías PROPIETARIAS entran en la exclusividad y RF33; las asociadas se
            // validan aparte (MandateSignerAssociationRules) y no ocupan el cupo de nadie.
            MandateSignerValidation.ValidateCompanies(
                errors, companyIds, otCompanies, resolutions, currentSignerId, configuredByScope, validas);
        }
    }
}
