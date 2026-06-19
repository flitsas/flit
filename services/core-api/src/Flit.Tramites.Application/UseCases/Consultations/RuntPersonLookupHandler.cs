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
    IConsultationProviderRegistry registry)
{
    private const string ProviderKey = "verifik_conductor";

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

        var provider = registry.Resolve(ProviderKey);
        if (provider is null)
            return (null, "provider_not_found");

        var fieldValues = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["document_type"] = documentType,
            ["document_number"] = documentNumber,
        };

        var ctx = new ConsultationContext(instanceId, tenantId, instance.ReferenceNumber, fieldValues);
        var result = await provider.ConsultAsync(ctx, ct);

        var fullName = GetHydrated(result, "person_full_name");
        var found = !string.IsNullOrWhiteSpace(fullName);

        var dto = new RuntPersonDto(
            Found: found,
            FullName: found ? fullName : null,
            FirstName: found ? GetHydrated(result, "person_first_name") : null,
            LastName: found ? GetHydrated(result, "person_last_name") : null,
            DocumentType: documentType,
            DocumentNumber: documentNumber,
            LicenseStatus: found ? GetHydrated(result, "person_license_status") : null,
            Mode: ResolveMode());

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

    // El modo real|mock del provider verifik_conductor se controla con VERIFIK_CONDUCTOR_MODE
    // (default "mock"; cualquier valor distinto de "real" se trata como mock — misma semántica
    // que ConsultationProviderModeOptions.IsMock en Infrastructure, replicada aquí porque
    // Application no referencia Infrastructure).
    private static string ResolveMode()
    {
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
    string Source = "RUNT");
