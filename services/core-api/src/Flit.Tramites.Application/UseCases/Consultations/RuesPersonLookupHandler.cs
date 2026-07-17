using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.Consultations;

/// <summary>
/// Lookup dedicado de persona JURÍDICA en RUES por NIT, para autopoblar la razón social del
/// actor jurídico en el wizard (bifurcación del "Consultar RUNT" cuando personType=juridical).
/// NO persiste: valida que la instancia exista para el tenant, arma un
/// <see cref="ConsultationContext"/> EN MEMORIA con el NIT (no lee ni escribe los field_values) y
/// delega en el provider <c>verifik_rues</c>. El actor se guarda luego vía PUT actors.
/// Es el análogo jurídico de <see cref="RuntPersonLookupHandler"/> (conductor / persona natural).
/// </summary>
public sealed class RuesPersonLookupHandler(
    IProcedureInstanceRepository repo,
    IConsultationProviderRegistry registry)
{
    private const string RuesProviderKey = "verifik_rues";

    public async Task<(RuesPersonDto? Result, string? Error)> HandleAsync(
        Guid instanceId,
        Guid tenantId,
        string? documentNumber,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(documentNumber))
            return (null, "invalid_request");

        var instance = await repo.GetByIdAsync(instanceId, tenantId, ct);
        if (instance is null)
            return (null, "instance_not_found");

        var provider = registry.Resolve(RuesProviderKey);
        if (provider is null)
            return (null, "provider_not_found");

        var nit = documentNumber.Trim();
        var fieldValues = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["nit"] = nit,
            ["documentNumber"] = nit,
        };

        var ctx = new ConsultationContext(instanceId, tenantId, "RUES_ACTOR_JURIDICAL", fieldValues);
        var result = await provider.ConsultAsync(ctx, ct);

        var razonSocial = GetHydrated(result, "rues_razon_social");
        var estado = GetHydrated(result, "rues_estado");
        var found = !string.IsNullOrWhiteSpace(razonSocial);

        var dto = new RuesPersonDto(
            Found: found,
            RazonSocial: found ? razonSocial : null,
            Estado: found ? estado : null,
            DocumentNumber: nit,
            MatriculaMercantil: found ? GetHydrated(result, "rues_matricula_mercantil") : null,
            CamaraComercio: found ? GetHydrated(result, "rues_camara_comercio") : null,
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

    // Modo real|mock informativo para el wizard. Replica la semántica de
    // ConsultationProviderModeOptions.IsMock (VERIFIK_RUES_MODE, default "mock") sin que Application
    // referencie Infrastructure.
    private static string ResolveMode()
    {
        var mode = Environment.GetEnvironmentVariable("VERIFIK_RUES_MODE") ?? "mock";
        return string.Equals(mode, "real", StringComparison.OrdinalIgnoreCase) ? "real" : "mock";
    }
}

/// <summary>
/// Persona jurídica resuelta en RUES (sin persistir). <see cref="Found"/> = se hidrató una
/// razón social no vacía. Cuando Found=false, los campos van en null y el frontend cae al ingreso
/// manual (registro sin resultado de consulta).
/// </summary>
public sealed record RuesPersonDto(
    bool Found,
    string? RazonSocial,
    string? Estado,
    string DocumentNumber,
    string? MatriculaMercantil,
    string? CamaraComercio,
    string Mode,
    string DocumentType = "NIT",
    string Source = "RUES");
