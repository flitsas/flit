using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.Services;
using Microsoft.Extensions.Logging;

namespace Flit.Tramites.Application.UseCases.Consultations;

/// <summary>
/// Lookup dedicado de persona en RUNT (CONDUCTOR) para autopoblar el comprador de la
/// matrícula. NO persiste: valida que la instancia exista para el tenant, arma un
/// <see cref="ConsultationContext"/> EN MEMORIA con document_type/document_number (no lee ni
/// escribe los field_values de la instancia) y delega en el provider verifik_conductor.
/// El comprador se sigue guardando luego vía PUT actors.
/// </summary>
/// <remarks>
/// <para><b>HU #10955 (AC1, revierte parcialmente HU #10878/ADR-0031):</b> la IDENTIDAD de un actor
/// se consulta SIEMPRE en vivo contra el RUNT. Este handler YA NO llama a
/// <see cref="ExternalQueryCacheService.TryReusePersonAsync"/> antes de resolver el proveedor —
/// nunca sirve un HIT cacheado, exista o no <c>PersonDataConsent</c> en <c>granted</c> para esa
/// persona (el gate de consentimiento del ADR-0031 queda sin efecto práctico para este flujo). Sí se
/// conserva la llamada a <see cref="ExternalQueryCacheService.SavePersonResultAsync"/> al final: la
/// caché se sigue escribiendo con el resultado fresco (fail-open, no cambia el shape de la respuesta)
/// para no tocar otros consumidores del mismo mecanismo cache-aside (p. ej. <c>RunConsultationHandler</c>
/// con templates <c>entity_scope=actor</c>), y por si una futura HU necesita reintroducir el reúso de
/// forma explícita. <c>PersonDataConsent</c> NO se borra ni se toca: se conserva para auditoría.</para>
/// <para>El DETALLE de comparendos se sigue consultando best-effort (SIMIT) cuando el RUNT marca el
/// flag de multas pendientes.</para>
/// </remarks>
public sealed class RuntPersonLookupHandler(
    IProcedureInstanceRepository repo,
    IConsultationProviderChainResolver chainResolver,
    IConsultationTenantOverrideProvider overrideProvider,
    ExternalQueryCacheService cacheService,
    IConsultationProviderRegistry? finesRegistry = null,
    ILogger<RuntPersonLookupHandler>? logger = null)
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

        // HU #10955 (AC1) — la identidad SIEMPRE se consulta en vivo: ya no se intenta un HIT de
        // ExternalQueryCacheService.TryReusePersonAsync antes de resolver el proveedor (ver remarks
        // de la clase). El resultado fresco SÍ se sigue cacheando al final (SavePersonResultAsync).
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

        // Evidencia de que ESTE documento se consultó de verdad en el RUNT dentro de este trámite.
        // El gate de actores la exige cuando el tipo de trámite marca el actor con requiresRunt: sin
        // ella, el wizard daba la consulta por hecha con solo tener el documento digitado.
        if (dto.Found)
        {
            await repo.AddEventAsync(
                new ProcedureInstanceEvent
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    ProcedureInstanceId = instanceId,
                    Tipo = RuntPersonaConsultada.Tipo,
                    Payload = RuntPersonaConsultada.Payload(documentType, documentNumber),
                    CreatedAt = now,
                },
                ct);
        }

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
        {
            if (logger is not null)
                RuntPersonLookupLog.ProveedorMultasNoRegistrado(logger, providerKey, documentType, documentNumber);
            return null;
        }

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

            if (logger is not null && multas?.Details is null or { Count: 0 })
                RuntPersonLookupLog.SinDetalleDeComparendos(logger, provider.Key, documentType, documentNumber);

            return multas?.Details;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Best-effort: no rompe el lookup del actor, pero SÍ deja traza — sin esto, un fallo del
            // proveedor de multas era indistinguible de "esta persona no tiene comparendos".
            if (logger is not null)
                RuntPersonLookupLog.ConsultaDeComparendosFallo(logger, ex, provider.Key, documentType, documentNumber);
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
/// Trazas del detalle de comparendos. Es una sub-consulta best-effort: nunca rompe el lookup del
/// actor, pero su fallo debe quedar registrado — de lo contrario "el proveedor falló" y "esta persona
/// no tiene comparendos" se ven exactamente igual en la UI (alerta sin lista).
/// </summary>
internal static partial class RuntPersonLookupLog
{
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Detalle de comparendos omitido para {DocumentType} {DocumentNumber}: el proveedor '{ProviderKey}' no está registrado.")]
    public static partial void ProveedorMultasNoRegistrado(
        ILogger logger, string providerKey, string documentType, string documentNumber);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "El proveedor '{ProviderKey}' no devolvió detalle de comparendos para {DocumentType} {DocumentNumber} aunque el RUNT reporta multas pendientes: la ficha del actor mostrará la alerta sin la lista.")]
    public static partial void SinDetalleDeComparendos(
        ILogger logger, string providerKey, string documentType, string documentNumber);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Falló la consulta de detalle de comparendos ('{ProviderKey}') para {DocumentType} {DocumentNumber}.")]
    public static partial void ConsultaDeComparendosFallo(
        ILogger logger, Exception ex, string providerKey, string documentType, string documentNumber);
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
