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
    private readonly IMandatarioAssociableCompanies? _associable;

    public UpdateMandateSignerHandler(
        ITransitOfficeOperationalStatusReader otStatus,
        IMandateSignerReader reader,
        IMandateSignerRepository repository,
        IMandatarioAssociableCompanies? associable = null)
    {
        _associable = associable;
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

        IReadOnlyList<Guid> companyIds = command.CompanyTenantIds ?? [];
        var companiesToValidate = companyIds;
        var transitOfficeIds = command.TransitOfficeIds;

        // Bug #12912 (2ª vuelta review PR #442) — la edición desde el organismo solo gestiona SU fila,
        // leyendo el estado persistido (no el cuerpo, que al OT le llega recortado):
        //  - las compañías que ya tenía y el organismo no puede ver se conservan (ni se quitan ni se
        //    exponen), y RF33 solo se valida sobre las que el organismo AGREGA;
        //  - los organismos no se tocan: los ajenos y su firma a mano se conservan tal cual.
        if (command.CompanyVisibility == OtCompanyVisibility.DirectOrWithReceivedProcedures)
        {
            var persistidas = signer.CompanyTenantIds.ToHashSet();
            var visibles = (await _reader
                    .ListOtCompaniesAsync(command.TransitOfficeId, command.CompanyVisibility, cancellationToken)
                    .ConfigureAwait(false))
                .Select(c => c.CompanyTenantId)
                .ToHashSet();

            companiesToValidate = [.. companyIds.Where(id => !persistidas.Contains(id)).Distinct()];
            companyIds = [.. companyIds.Union(persistidas.Where(id => !visibles.Contains(id)))];
            transitOfficeIds = null;
        }

        // HU #13179 — compañías asociadas (403 fuera de alcance; 422 por elemento; RF33 para el OT). Los
        // organismos del mandatario son los que quedan tras la edición, o los persistidos si no se tocan.
        var mandatarioOffices = (transitOfficeIds is { Count: > 0 }
                ? transitOfficeIds
                : [.. signer.TransitOfficeIds, signer.TransitOfficeId, command.TransitOfficeId])
            .ToHashSet();
        var associationErrors = await MandateSignerAssociationRules.ValidateAsync(
                _associable, _reader, command.OfficeCompanies, companyIds, mandatarioOffices,
                command.ConfiguredByScope, cancellationToken)
            .ConfigureAwait(false);

        var otStatus = await _otStatus
            .GetByIdAsync(command.TransitOfficeId, cancellationToken).ConfigureAwait(false);

        // HU #13129 — modelo, forma de firma y vigencia; lo que no se manda se conserva (solo natural).
        var (profile, profileErrors) = MandateSignerModelRules.Evaluate(
            new MandateSignerProfileInput(
                command.SignerModel,
                command.SignatureMethod,
                command.ValidityKind,
                command.ValidFrom,
                command.ValidTo,
                command.FullName,
                command.DocumentType,
                command.DocumentNumber,
                command.Email,
                command.ActualizaFirma ? command.SignatureVaultId : null),
            signer);

        var (otTenantId, errors) = MandateSignerValidation.ValidateBase(
            otStatus,
            profile.FullName,
            profile.DocumentNumber,
            companyIds,
            documentRequired: profile.Model != MandateSignerModels.FormatoBlanco);
        errors.AddRange(profileErrors);
        errors.AddRange(associationErrors);

        // Forma de firma baúl: la firma elegida (o la ya guardada, si el llamante no gestiona la firma).
        var efectiveVaultId = command.ActualizaFirma ? command.SignatureVaultId : signer.SignatureVaultId;
        if (errors.Count == 0
            && profile.SignatureMethod == MandateSignatureMethods.Baul)
        {
            var offices = transitOfficeIds is { Count: > 0 }
                ? transitOfficeIds
                : (signer.TransitOfficeIds.Count > 0 ? signer.TransitOfficeIds : [command.TransitOfficeId]);
            var baulError = MandateSignerSigningCapability.Validate(
                offices,
                efectiveVaultId,
                existente: null,
                MandateSignatureMethods.Baul);
            if (baulError is not null)
            {
                errors.Add(baulError);
            }
        }

        if (otTenantId is not null && companiesToValidate.Count > 0)
        {
            await CreateMandateSignerHandler.AddExclusiveSlotErrorsAsync(
                    _reader,
                    errors,
                    command.TransitOfficeId,
                    companiesToValidate,
                    transitOfficeIds,
                    command.MandateSignerId,
                    command.CompanyVisibility,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (errors.Count > 0)
        {
            return UpdateMandateSignerResult.Invalid(errors);
        }

        var fullName = profile.FullName;
        var documentNumber = profile.DocumentNumber;
        var documentType = profile.DocumentType;
        var email = string.IsNullOrWhiteSpace(command.Email) ? null : command.Email.Trim();

        // El vínculo al baúl solo existe con forma de firma baúl: al pasar a biometría o a un modelo sin
        // firma personal se desvincula (elección explícita, sin caída de un medio al otro, ADR-0061).
        var conservaBaul = profile.SignatureMethod == MandateSignatureMethods.Baul;
        var actualizaFirma = command.ActualizaFirma || !conservaBaul;
        var vaultId = conservaBaul ? command.SignatureVaultId : null;
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
                transitOfficeIds,
                null, // HU #13131: no se toca la marca histórica de firma física (se conserva tal cual).
                vaultId,
                command.OfficeCompanies,
                actualizaFirma,
                // Tras la edición, el organismo bajo el que se editó es el primario. Solo cambia algo
                // cuando la lista retira al primario anterior; en la edición desde el perfil del
                // organismo ambos coinciden y esto es un no-op.
                NuevoOrganismoPrimario: command.TransitOfficeId,
                SignerModel: profile.Model,
                SignatureMethod: profile.SignatureMethod,
                ValidityKind: profile.ValidityKind,
                ValidFrom: profile.ValidFrom,
                ValidTo: profile.ValidTo,
                ConfiguredByScope: command.ConfiguredByScope),
            cancellationToken).ConfigureAwait(false);

        return updated
            ? UpdateMandateSignerResult.Updated(integrityHash)
            : UpdateMandateSignerResult.NotFound();
    }
}
