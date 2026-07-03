using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.Consultations;

/// <summary>
/// Lookup dedicado de persona en RUNT (CONDUCTOR) para autopoblar el comprador de la
/// matrícula. NO persiste: valida que la instancia exista para el tenant, arma un
/// <see cref="ConsultationContext"/> EN MEMORIA con document_type/document_number (no lee ni
/// escribe los field_values de la instancia) y delega en el provider verifik_conductor.
/// El comprador se sigue guardando luego vía PUT actors.
/// </summary>
public sealed class RuntPersonLookupHandler(
    IProcedureInstanceRepository repo,
    IConsultationProviderChainResolver chainResolver,
    IConsultationTenantOverrideProvider overrideProvider)
{
    private const string KyverumConductorProvider = "kyverum_runt_conductor";

    public async Task<(RuntPersonDto? Result, string? Error)> HandleAsync(
        Guid instanceId,
        Guid tenantId,
        string? documentType,
        string? documentNumber,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(documentType) || string.IsNullOrWhiteSpace(documentNumber))
            return (null, "invalid_request");

        var instance = await repo.GetByIdAsync(instanceId, tenantId, ct);
        if (instance is null)
            return (null, "instance_not_found");

        var mappedDocType = MapDocumentType(documentType);
        if (mappedDocType is null)
            return (null, "unsupported_document_type");

        var fieldValues = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["document_type"] = mappedDocType,
            ["document_number"] = documentNumber,
        };

        // HU #10478: cadena Kyverum-first → Verifik (conductor) según config del tenant.
        var tenantOverride = await overrideProvider.GetAsync(tenantId, ct);
        var ctx = new ConsultationContext(instanceId, tenantId, instance.ReferenceNumber, fieldValues);
        var result = await chainResolver.ConsultAsync(ConsultationKind.Conductor, ctx, tenantOverride, ct);

        var fullName = GetHydrated(result, "person_full_name");
        var found = !string.IsNullOrWhiteSpace(fullName);

        var hasPendingFines = GetHydrated(result, "person_has_pending_fines") == "true";
        var citizenStatus = found ? GetHydrated(result, "person_citizen_status") : null;
        var nroPazYSalvo = found ? GetHydrated(result, "person_paz_y_salvo") : null;
        var hasActiveLicense = GetHydrated(result, "person_has_active_license") == "true";
        var licenseCategories = found ? GetHydrated(result, "person_license_categories") : null;

        var dto = new RuntPersonDto(
            Found: found,
            FullName: found ? fullName : null,
            FirstName: found ? GetHydrated(result, "person_first_name") : null,
            LastName: found ? GetHydrated(result, "person_last_name") : null,
            DocumentType: documentType,
            DocumentNumber: documentNumber,
            LicenseStatus: found ? GetHydrated(result, "person_license_status") : null,
            Mode: ResolveMode(result.Provider),
            CitizenStatus: citizenStatus,
            HasPendingFines: hasPendingFines,
            NroPazYSalvo: nroPazYSalvo,
            HasActiveLicense: hasActiveLicense,
            LicenseCategories: licenseCategories);

        return (dto, null);
    }

    private static string? GetHydrated(ConsultationResult result, string fieldKey)
    {
        foreach (var f in result.HydratedFields)
        {
            if (string.Equals(f.FieldKey, fieldKey, StringComparison.OrdinalIgnoreCase))
                return f.ValueText;
        }

        return null;
    }

    // Mapeo documentType FLIT → Verifik: CC→CC, CE→CE, PAS→PA, TI→PPT, NIT→null (no soportado)
    private static string? MapDocumentType(string documentType) =>
        documentType.ToUpperInvariant() switch
        {
            "CC" => "CC",
            "CE" => "CE",
            "PAS" => "PA",
            "TI" => "PPT",
            "NIT" => null,
            _ => documentType
        };

    // Modo real|mock que reporta el DTO (informativo para el wizard). Kyverum RUNT no tiene modo
    // mock: si respondió, es "real". Para Verifik conductor se replica la semántica de
    // ConsultationProviderModeOptions.IsMock (VERIFIK_CONDUCTOR_MODE, default "mock"), aquí porque
    // Application no referencia Infrastructure.
    private static string ResolveMode(string? answeringProvider)
    {
        if (string.Equals(answeringProvider, KyverumConductorProvider, StringComparison.OrdinalIgnoreCase))
            return "real";

        var mode = Environment.GetEnvironmentVariable("VERIFIK_CONDUCTOR_MODE") ?? "mock";
        return string.Equals(mode, "real", StringComparison.OrdinalIgnoreCase) ? "real" : "mock";
    }
}

/// <summary>
/// Persona resuelta en RUNT (sin persistir). <see cref="Found"/> = se hidrató un
/// person_full_name no vacío. Cuando Found=false, los campos de nombre van en null y el
/// frontend cae al ingreso manual.
/// </summary>
public sealed record RuntPersonDto(
    bool Found,
    string? FullName,
    string? FirstName,
    string? LastName,
    string DocumentType,
    string DocumentNumber,
    string? LicenseStatus,
    string Mode,
    string Source = "RUNT",
    string? CitizenStatus = null,
    bool HasPendingFines = false,
    string? NroPazYSalvo = null,
    bool HasActiveLicense = false,
    string? LicenseCategories = null);
