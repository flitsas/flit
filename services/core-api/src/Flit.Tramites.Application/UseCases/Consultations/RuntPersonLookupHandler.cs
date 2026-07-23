using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.Consultations;

/// <summary>
/// Lookup dedicado de persona en RUNT (CONDUCTOR) para autopoblar el comprador de la
/// matrícula. NO persiste: valida que la instancia exista para el tenant, arma un
/// <see cref="ConsultationContext"/> EN MEMORIA con document_type/document_number (no lee ni
/// escribe los field_values de la instancia) y delega en el provider verifik_conductor.
/// El comprador se sigue guardando luego vía PUT actors.
/// </summary>
/// <remarks>
/// HU #10878 (Feature #10862, CF-04, ADR-0030/ADR-0031): ANTES de resolver la cadena de
/// proveedores, consulta <see cref="ExternalQueryCacheService.TryReusePersonAsync"/> (fuente
/// <c>RUNT</c>, llave = documento FLIT tal cual llega — CC/CE/PAS/TI, el mismo vocabulario que
/// <c>ActorInput.TipoDocumento</c>, para que el gate de consentimiento capturado en <c>PUT
/// actors</c> resuelva la misma llave). En HIT reconstruye el DTO desde el payload cacheado sin
/// llamar a ningún proveedor (AC1) — la consulta best-effort de multas SIMIT queda fuera de
/// alcance en el HIT (no se re-ejecuta). En MISS, el flujo original queda intacto y, al final,
/// cachea el resultado fresco del RUNT (AC2), sin incluir el detalle de multas (SIMIT es una
/// sub-consulta best-effort distinta, fuera de alcance de esta HU).
/// </remarks>
public sealed class RuntPersonLookupHandler(
    IProcedureInstanceRepository repo,
    IConsultationProviderChainResolver chainResolver,
    IConsultationTenantOverrideProvider overrideProvider,
    ExternalQueryCacheService cacheService,
    IConsultationProviderRegistry? finesRegistry = null)
{
    private const string KyverumConductorProvider = "kyverum_runt_conductor";
    private const string RuntSourceCode = "RUNT";

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

        var now = DateTimeOffset.UtcNow;

        // HU #10878 — cache-aside ANTES de resolver la cadena de proveedores (AC1).
        var cacheLookup = await cacheService.TryReusePersonAsync(tenantId, RuntSourceCode, documentType, documentNumber, now, ct);
        if (cacheLookup.Hit)
            return (BuildDtoFromFields(cacheLookup.Fields!, documentType, documentNumber, "cache"), null);

        var fieldValues = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["document_type"] = mappedDocType,
            ["document_number"] = documentNumber,
        };

        // HU #10478: cadena Kyverum-first → Verifik (conductor) según config del tenant.
        var tenantOverride = await overrideProvider.GetAsync(tenantId, ct);
        var ctx = new ConsultationContext(instanceId, tenantId, instance.ReferenceNumber, fieldValues);
        var result = await chainResolver.ConsultAsync(ConsultationKind.Conductor, ctx, tenantOverride, ct);

        var dto = BuildDtoFromFields(result.HydratedFields, documentType, documentNumber, ResolveMode(result.Provider));

        // El RUNT (conductor) solo trae el FLAG de multas, no el detalle. Cuando marca multas,
        // se consulta el SIMIT del mismo documento (best-effort) para traer el detalle de cada
        // comparendo y mostrarlo junto a la alerta en la ficha del actor. Sin registry (tests) o si
        // el proveedor falla, la alerta se conserva sin detalle: es informativo, no rompe el lookup.
        var fines = dto.Found && dto.HasPendingFines
            ? await TryConsultFinesDetailAsync(tenantOverride, documentType, documentNumber, instanceId, tenantId, ct)
            : null;
        if (fines is not null)
            dto = dto with { Fines = fines };

        // HU #10878 (AC2): cachea el resultado fresco del RUNT (sin el detalle de multas — sub-consulta
        // best-effort fuera de alcance) para reúsos futuros dentro del TTL de la fuente. Fail-open: si
        // la fuente no está catalogada o el TTL es 0, no cachea, sin afectar la respuesta ya calculada.
        await cacheService.SavePersonResultAsync(
            tenantId, RuntSourceCode, documentType, documentNumber, instanceId, result.HydratedFields, now, ct);

        return (dto, null);
    }

    /// <summary>
    /// Consulta el SIMIT del documento para traer el detalle de comparendos (best-effort). El
    /// conductor del RUNT es siempre persona natural (NIT retorna unsupported antes), así que el
    /// proveedor de multas es <c>verifik_simit</c> (fuente externa) o <c>flit_fines</c> (interna).
    /// Devuelve el detalle del check de multas, o <c>null</c> si no hay registry, el proveedor no está
    /// registrado o la consulta falla — nunca propaga el fallo al lookup del actor.
    /// </summary>
    private async Task<IReadOnlyList<FineDetail>?> TryConsultFinesDetailAsync(
        ConsultationTenantOverride? tenantOverride,
        string documentType,
        string documentNumber,
        Guid instanceId,
        Guid tenantId,
        CancellationToken ct)
    {
        if (finesRegistry is null)
            return null;

        var providerKey = FinesProviderResolver.Resolve(tenantOverride?.FinesQuerySource, isNaturalPerson: true);
        var provider = finesRegistry.Resolve(providerKey);
        if (provider is null)
            return null;

        try
        {
            var fv = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["owner_document_type"] = documentType,
                ["owner_document_number"] = documentNumber,
            };
            var ctx = new ConsultationContext(instanceId, tenantId, provider.Key, fv);
            var result = await provider.ConsultAsync(ctx, ct);
            var multas = result.Checks.FirstOrDefault(c =>
                string.Equals(c.Key, FinesCheckFactory.KeyMultas, StringComparison.Ordinal));
            return multas?.Details;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? GetHydrated(IReadOnlyList<HydratedField> fields, string fieldKey)
    {
        foreach (var f in fields)
        {
            if (string.Equals(f.FieldKey, fieldKey, StringComparison.OrdinalIgnoreCase))
                return f.ValueText;
        }

        return null;
    }

    /// <summary>Arma el DTO leyendo el shape común HydratedField[] — usado tanto en el HIT de caché como en el consult en vivo.</summary>
    private static RuntPersonDto BuildDtoFromFields(
        IReadOnlyList<HydratedField> fields, string documentType, string documentNumber, string mode)
    {
        var fullName = GetHydrated(fields, "person_full_name");
        var found = !string.IsNullOrWhiteSpace(fullName);
        var hasPendingFines = GetHydrated(fields, "person_has_pending_fines") == "true";

        return new RuntPersonDto(
            Found: found,
            FullName: found ? fullName : null,
            FirstName: found ? GetHydrated(fields, "person_first_name") : null,
            LastName: found ? GetHydrated(fields, "person_last_name") : null,
            DocumentType: documentType,
            DocumentNumber: documentNumber,
            LicenseStatus: found ? GetHydrated(fields, "person_license_status") : null,
            Mode: mode,
            CitizenStatus: found ? GetHydrated(fields, "person_citizen_status") : null,
            HasPendingFines: hasPendingFines,
            NroPazYSalvo: found ? GetHydrated(fields, "person_paz_y_salvo") : null,
            HasActiveLicense: GetHydrated(fields, "person_has_active_license") == "true",
            LicenseCategories: found ? GetHydrated(fields, "person_license_categories") : null,
            Fines: null);
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
    string? LicenseCategories = null,
    IReadOnlyList<FineDetail>? Fines = null);
