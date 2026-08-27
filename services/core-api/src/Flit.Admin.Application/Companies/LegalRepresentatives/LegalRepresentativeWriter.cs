using Flit.Admin.Domain.Companies.LegalRepresentatives;
using Flit.Admin.Domain.Companies.SignatureVault;
using Flit.Admin.Domain.DocumentRequirements;

namespace Flit.Admin.Application.Companies.LegalRepresentatives;

/// <summary>
/// Lógica compartida del guardado (alta/edición) de un representante legal (HU #10901, ADR-0033).
/// Encapsula, para Create y Update: validación de campos y tipos de trámite (422), upsert de la
/// compañía representada por NIT, resolución de firma/identidad vigente vía
/// <see cref="ILegalRepresentativeSignatureResolver"/> (precedencia baúl &gt; identidad) y la
/// persistencia con la marca de tipos de trámite (M:N). Cuando el resolutor no encuentra firma ni
/// identidad vigente, el guardado igual persiste y la respuesta emite la señal
/// <see cref="LegalRepresentativeSignals.SinFirmaNiIdentidad"/>. <c>DocumentNumber</c> es PII: no loguear.
///
/// HU #11175 (AC3/AC4/AC5): si <see cref="LegalRepresentativeWriteInput.SignatureVaultId"/> viene
/// informado, se valida (misma tenencia, documento coincidente, activa y vigente) y se persiste
/// directamente, saltándose el resolver (AC3). Una firma inválida retorna 422 (AC4). Si no viene,
/// se conserva el comportamiento anterior (AC5).
/// </summary>
public sealed class LegalRepresentativeWriter
{
    // Hora de Colombia (UTC-5, sin DST): la vigencia de firma/identidad se cuenta por día calendario
    // local (ADR-0025 §3 / ADR-0033 §5.1), coherente con el resolutor.
    private static readonly TimeSpan ColombiaUtcOffset = TimeSpan.FromHours(-5);

    private readonly IProcedureTypeCatalog _procedureTypeCatalog;
    private readonly ILegalRepresentativeSignatureResolver _signatureResolver;
    private readonly ISignatureVaultReader _signatureVaultReader;
    private readonly ILegalRepresentativeRepository _repository;
    private readonly ILegalRepresentativeReader _reader;
    private readonly TimeProvider _timeProvider;

    public LegalRepresentativeWriter(
        IProcedureTypeCatalog procedureTypeCatalog,
        ILegalRepresentativeSignatureResolver signatureResolver,
        ISignatureVaultReader signatureVaultReader,
        ILegalRepresentativeRepository repository,
        ILegalRepresentativeReader reader,
        TimeProvider timeProvider)
    {
        _procedureTypeCatalog = procedureTypeCatalog ?? throw new ArgumentNullException(nameof(procedureTypeCatalog));
        _signatureResolver = signatureResolver ?? throw new ArgumentNullException(nameof(signatureResolver));
        _signatureVaultReader = signatureVaultReader ?? throw new ArgumentNullException(nameof(signatureVaultReader));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<LegalRepresentativeWriteResult> WriteAsync(
        LegalRepresentativeWriteInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var errors = await ValidateAsync(input, cancellationToken).ConfigureAwait(false);
        if (errors.Count > 0)
        {
            return LegalRepresentativeWriteResult.Invalid(errors);
        }

        // Edición explícita: el representante debe existir en el tenant (404 en vez de crear uno nuevo).
        Guid? editId = input.Id;
        if (editId is { } explicitId)
        {
            var existing = await _reader
                .GetByIdAsync(input.TenantId, explicitId, cancellationToken).ConfigureAwait(false);
            if (existing is null)
            {
                return LegalRepresentativeWriteResult.NotFoundResult();
            }
        }

        var documentType = input.DocumentType!.Trim();
        var documentNumber = input.DocumentNumber!.Trim();

        // "Se crea una sola vez" (HU #10932): en alta, si ya existe la persona activa por documento en
        // el tenant, se le agregan compañías en vez de duplicarla.
        LegalRepresentativeItem? samePerson = null;
        if (editId is null)
        {
            samePerson = await _reader
                .FindActiveByDocumentAsync(input.TenantId, documentType, documentNumber, cancellationToken)
                .ConfigureAwait(false);
            if (samePerson is not null)
            {
                editId = samePerson.Id;
            }
        }

        var effectiveCompanies = EffectiveCompanies(input);
        var primaryNit = effectiveCompanies.Count > 0 ? effectiveCompanies[0].Nit!.Trim() : string.Empty;
        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().ToOffset(ColombiaUtcOffset).DateTime);

        LegalRepresentativeSignatureResolution resolution;
        if (input.SignatureVaultId is { } explicitSignatureId)
        {
            resolution = LegalRepresentativeSignatureResolution.FromSignature(explicitSignatureId);
        }
        else
        {
            resolution = await _signatureResolver
                .ResolveAsync(input.TenantId, primaryNit, documentType, documentNumber, today, cancellationToken)
                .ConfigureAwait(false);
        }

        var distinctProcedureTypeIds = input.ProcedureTypeIds.Distinct().ToArray();

        // La ficha de NIT es del representante: hay que persistir la persona antes del upsert.
        if (editId is null)
        {
            editId = await _repository.SaveAsync(
                BuildSave(
                    input, null, null, documentType, documentNumber, resolution, distinctProcedureTypeIds, []),
                cancellationToken).ConfigureAwait(false);
        }

        var companyIds = new List<Guid>();
        foreach (var company in effectiveCompanies)
        {
            var companyId = await _repository
                .UpsertRepresentedCompanyAsync(
                    new UpsertRepresentedCompanyData(
                        input.TenantId,
                        company.Nit!.Trim(),
                        company.Name!.Trim(),
                        Normalize(company.Email),
                        Normalize(company.Address),
                        Normalize(company.City),
                        Normalize(company.Phone),
                        input.ActorBy,
                        editId),
                    cancellationToken)
                .ConfigureAwait(false);
            if (!companyIds.Contains(companyId))
            {
                companyIds.Add(companyId);
            }
        }

        if (input.Id is null && samePerson is not null)
        {
            foreach (var existingCompanyId in samePerson.Companies.Select(c => c.Id))
            {
                if (!companyIds.Contains(existingCompanyId))
                {
                    companyIds.Add(existingCompanyId);
                }
            }
        }

        var primaryCompanyId = companyIds.Count > 0 ? companyIds[0] : (Guid?)null;
        var id = await _repository.SaveAsync(
            BuildSave(
                input, editId, primaryCompanyId, documentType, documentNumber, resolution, distinctProcedureTypeIds, companyIds),
            cancellationToken).ConfigureAwait(false);

        // Señal (no bloqueante) cuando no hubo firma ni identidad vigente al guardar.
        var signals = resolution.HasSignatureOrIdentity
            ? Array.Empty<string>()
            : [LegalRepresentativeSignals.SinFirmaNiIdentidad];

        return LegalRepresentativeWriteResult.Success(id, signals);
    }

    private async Task<List<LegalRepresentativeValidationError>> ValidateAsync(
        LegalRepresentativeWriteInput input,
        CancellationToken cancellationToken)
    {
        var errors = new List<LegalRepresentativeValidationError>();

        // Compañías (HU #10932): opcionales. Si hay lista, cada fila exige NIT+nombre.
        // Si la lista viene vacía/null y tampoco hay Company* planos → persona sin NITs (válido).
        if (input.Companies is { Count: > 0 } companies)
        {
            foreach (var company in companies)
            {
                Require(errors, "companies", company.Nit);
                Require(errors, "companies", company.Name);
            }
        }
        else if (!string.IsNullOrWhiteSpace(input.CompanyNit) || !string.IsNullOrWhiteSpace(input.CompanyName))
        {
            Require(errors, "companyNit", input.CompanyNit);
            Require(errors, "companyName", input.CompanyName);
        }

        Require(errors, "documentType", input.DocumentType);
        Require(errors, "documentNumber", input.DocumentNumber);
        Require(errors, "firstLastName", input.FirstLastName);
        Require(errors, "name", input.Name);

        // Cada tipo de trámite marcado debe existir en el catálogo (tramites.procedure_types).
        foreach (var procedureTypeId in input.ProcedureTypeIds.Distinct())
        {
            if (procedureTypeId == Guid.Empty)
            {
                errors.Add(new LegalRepresentativeValidationError(
                    "procedureTypeIds", "tipo_tramite_invalido",
                    "El identificador de un tipo de trámite marcado es inválido."));
                continue;
            }

            var exists = await _procedureTypeCatalog
                .ExistsAsync(procedureTypeId, cancellationToken).ConfigureAwait(false);
            if (!exists)
            {
                errors.Add(new LegalRepresentativeValidationError(
                    "procedureTypeIds", "tipo_tramite_inexistente",
                    "Uno de los tipos de trámite marcados no existe en el catálogo."));
            }
        }

        // AC4 — Validar la firma elegida explícitamente: misma tenencia, documento coincidente,
        // activa y vigente. PII (documentNumber) no se incluye en mensajes de error.
        if (input.SignatureVaultId is { } signatureVaultId && errors.Count == 0)
        {
            var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().ToOffset(ColombiaUtcOffset).DateTime);
            await ValidateExplicitSignatureAsync(
                input.TenantId,
                signatureVaultId,
                input.DocumentType!.Trim(),
                input.DocumentNumber!.Trim(),
                today,
                errors,
                cancellationToken).ConfigureAwait(false);
        }

        return errors;
    }

    /// <summary>
    /// AC4: valida que la firma elegida (a) exista en el tenant, (b) su documento coincida con el
    /// del representante, (c) esté activa y (d) esté vigente en la fecha de Colombia. PII nunca
    /// se escribe en logs ni en mensajes de error.
    /// </summary>
    private async Task ValidateExplicitSignatureAsync(
        Guid tenantId,
        Guid signatureVaultId,
        string documentType,
        string documentNumber,
        DateOnly today,
        List<LegalRepresentativeValidationError> errors,
        CancellationToken cancellationToken)
    {
        var firma = await _signatureVaultReader
            .GetByIdAsync(tenantId, signatureVaultId, cancellationToken)
            .ConfigureAwait(false);

        if (firma is null)
        {
            errors.Add(new LegalRepresentativeValidationError(
                "signatureVaultId", "firma_no_encontrada",
                "La firma indicada no existe en el baúl de esta compañía."));
            return;
        }

        // Documento del titular de la firma debe coincidir con el del representante.
        if (!string.Equals(firma.DocumentType, documentType, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(firma.DocumentNumber, documentNumber, StringComparison.Ordinal))
        {
            errors.Add(new LegalRepresentativeValidationError(
                "signatureVaultId", "firma_documento_no_coincide",
                "La firma indicada no pertenece al representante."));
            return;
        }

        // Firma debe estar activa y vigente.
        if (firma.Estado != SignatureVaultEstado.Activa ||
            today < firma.VigenciaDesde ||
            today > firma.VigenciaHasta)
        {
            errors.Add(new LegalRepresentativeValidationError(
                "signatureVaultId", "firma_no_vigente",
                "La firma indicada no está activa o su vigencia ha expirado."));
        }
    }

    private static void Require(List<LegalRepresentativeValidationError> errors, string field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(new LegalRepresentativeValidationError(field, "requerido", $"El campo {field} es obligatorio."));
        }
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// Compañías efectivas del representante (HU #10932): la lista anidada <c>Companies</c> si viene;
    /// si no, la compañía única de <c>Company*</c> cuando hay NIT/nombre; si no hay ninguna → lista vacía
    /// (persona sin empresas asociadas todavía).
    /// </summary>
    private static IReadOnlyList<LegalRepresentativeCompanyInput> EffectiveCompanies(
        LegalRepresentativeWriteInput input)
    {
        if (input.Companies is { Count: > 0 } companies)
        {
            return companies;
        }

        if (!string.IsNullOrWhiteSpace(input.CompanyNit) || !string.IsNullOrWhiteSpace(input.CompanyName))
        {
            return
            [
                new LegalRepresentativeCompanyInput(
                    input.CompanyNit, input.CompanyName, input.CompanyEmail,
                    input.CompanyAddress, input.CompanyCity, input.CompanyPhone)
            ];
        }

        return [];
    }

    private static SaveLegalRepresentativeData BuildSave(
        LegalRepresentativeWriteInput input,
        Guid? id,
        Guid? primaryCompanyId,
        string documentType,
        string documentNumber,
        LegalRepresentativeSignatureResolution resolution,
        IReadOnlyList<Guid> procedureTypeIds,
        IReadOnlyList<Guid> companyIds) =>
        new(
            input.TenantId,
            id,
            primaryCompanyId,
            documentType,
            documentNumber,
            input.FirstLastName!.Trim(),
            Normalize(input.SecondLastName),
            input.Name!.Trim(),
            Normalize(input.Email),
            Normalize(input.Address),
            Normalize(input.City),
            Normalize(input.Phone),
            resolution.SignatureVaultId,
            resolution.IdentityValidationRef,
            procedureTypeIds,
            input.ActorBy,
            companyIds);
}
