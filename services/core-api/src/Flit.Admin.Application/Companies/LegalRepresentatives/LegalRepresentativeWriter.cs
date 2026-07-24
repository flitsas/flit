using Flit.Admin.Domain.Companies.LegalRepresentatives;
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
/// </summary>
public sealed class LegalRepresentativeWriter
{
    // Hora de Colombia (UTC-5, sin DST): la vigencia de firma/identidad se cuenta por día calendario
    // local (ADR-0025 §3 / ADR-0033 §5.1), coherente con el resolutor.
    private static readonly TimeSpan ColombiaUtcOffset = TimeSpan.FromHours(-5);

    private readonly IProcedureTypeCatalog _procedureTypeCatalog;
    private readonly ILegalRepresentativeSignatureResolver _signatureResolver;
    private readonly ILegalRepresentativeRepository _repository;
    private readonly ILegalRepresentativeReader _reader;
    private readonly TimeProvider _timeProvider;

    public LegalRepresentativeWriter(
        IProcedureTypeCatalog procedureTypeCatalog,
        ILegalRepresentativeSignatureResolver signatureResolver,
        ILegalRepresentativeRepository repository,
        ILegalRepresentativeReader reader,
        TimeProvider timeProvider)
    {
        _procedureTypeCatalog = procedureTypeCatalog ?? throw new ArgumentNullException(nameof(procedureTypeCatalog));
        _signatureResolver = signatureResolver ?? throw new ArgumentNullException(nameof(signatureResolver));
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

        // Edición: el representante debe existir en el tenant (404 en vez de crear uno nuevo).
        if (input.Id is { } editId)
        {
            var existing = await _reader
                .GetByIdAsync(input.TenantId, editId, cancellationToken).ConfigureAwait(false);
            if (existing is null)
            {
                return LegalRepresentativeWriteResult.NotFoundResult();
            }
        }

        var companyNit = input.CompanyNit!.Trim();
        var documentType = input.DocumentType!.Trim();
        var documentNumber = input.DocumentNumber!.Trim();

        // Upsert de la compañía representada por (tenant, NIT): reutiliza la dimensión si ya existe.
        var representedCompanyId = await _repository
            .UpsertRepresentedCompanyAsync(
                new UpsertRepresentedCompanyData(
                    input.TenantId,
                    companyNit,
                    input.CompanyName!.Trim(),
                    Normalize(input.CompanyEmail),
                    Normalize(input.CompanyAddress),
                    Normalize(input.CompanyCity),
                    Normalize(input.CompanyPhone),
                    input.ActorBy),
                cancellationToken)
            .ConfigureAwait(false);

        // Resolución de firma/identidad vigente al guardar (precedencia baúl > identidad).
        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().ToOffset(ColombiaUtcOffset).DateTime);
        var resolution = await _signatureResolver
            .ResolveAsync(input.TenantId, companyNit, documentType, documentNumber, today, cancellationToken)
            .ConfigureAwait(false);

        var distinctProcedureTypeIds = input.ProcedureTypeIds.Distinct().ToArray();

        var id = await _repository.SaveAsync(
            new SaveLegalRepresentativeData(
                input.TenantId,
                input.Id,
                representedCompanyId,
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
                distinctProcedureTypeIds,
                input.ActorBy),
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

        Require(errors, "companyNit", input.CompanyNit);
        Require(errors, "companyName", input.CompanyName);
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

        return errors;
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
}
