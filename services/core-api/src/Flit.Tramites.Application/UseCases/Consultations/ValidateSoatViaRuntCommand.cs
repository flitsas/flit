using System.Text.Json;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.Services;

namespace Flit.Tramites.Application.UseCases.Consultations;

/// <summary>
/// Resultado de validar el SOAT en línea (HU #10611, Feature #10587): la compañía, con el trámite en
/// <c>asignado</c>, re-consulta el RUNT del vehículo. Si el RUNT reporta el SOAT vigente se registra
/// <c>soat_estado=vigente</c> (desbloquea la aprobación del OT); si está vencido se registra
/// <c>vencido</c>; si el RUNT no lo reporta queda <c>unknown</c> y la compañía debe cargar el PDF.
/// </summary>
public sealed record ValidateSoatResult(
    bool Vigente,
    string SoatEstado,
    string? Vencimiento,
    string? Aseguradora,
    string Message);

/// <summary>
/// Registra el estado del SOAT re-consultando el RUNT del vehículo (template <c>RUNT_VEHICLE</c>) sin
/// salir del estado <c>asignado</c>. A diferencia de <see cref="RunConsultationHandler"/>, NO hidrata
/// todos los campos del vehículo (el trigger de inmutabilidad solo permite escribir <c>soat_estado</c>
/// fuera de borrador): únicamente deriva y persiste <c>soat_estado</c> a partir del check <c>soat</c>.
/// </summary>
public sealed class ValidateSoatViaRuntHandler(
    IProcedureInstanceRepository instanceRepo,
    ICatalogRepository catalogRepo,
    IConsultationProviderRegistry registry,
    Certifications.ICertificationIngestionService? certificationIngestion = null)
{
    private const string TemplateCode = "RUNT_VEHICLE";
    private const string SoatCheckKey = "soat";
    private const string ConsultationSource = "consultation";

    public async Task<(ValidateSoatResult? Result, string? Error)> HandleAsync(
        Guid instanceId,
        Guid tenantId,
        CancellationToken ct = default)
    {
        var instance = await instanceRepo.GetByIdWithDetailsAsync(instanceId, tenantId, ct);
        if (instance is null)
            return (null, "instance_not_found");

        // La validación de SOAT es exclusiva del paso post-asignación de placa: el trámite está
        // 'entregado' con el sub-estado interno de placa en 'asignado' (HU #10785).
        if (!string.Equals(instance.Status, TramiteEstado.Entregado, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(instance.PlateFlowStatus, PlateFlowStatus.Asignado, StringComparison.OrdinalIgnoreCase))
            return (null, "invalid_state");

        var template = await catalogRepo.GetConsultationTemplateByCodeAsync(TemplateCode, ct);
        if (template is null)
            return (null, "template_not_found");

        var providerKey = ResolveProviderKey(template.ExternalRefs);
        if (string.IsNullOrWhiteSpace(providerKey))
            return (null, "provider_not_resolved");

        var provider = registry.Resolve(providerKey);
        if (provider is null)
            return (null, "provider_not_found");

        var fieldValues = instance.FieldValues
            .ToDictionary(f => f.FieldKey, f => f.ValueText, StringComparer.OrdinalIgnoreCase);

        var result = await provider.ConsultAsync(
            new ConsultationContext(instance.Id, instance.TenantId, TemplateCode, fieldValues), ct);

        var soatCheck = result.Checks.FirstOrDefault(c =>
            string.Equals(c.Key, SoatCheckKey, StringComparison.OrdinalIgnoreCase));
        var soatEstado = MapSoatEstado(soatCheck?.Status);

        UpsertSoatEstado(instance, tenantId, instanceRepo, soatEstado);
        await instanceRepo.SaveChangesAsync(ct);

        // HU #11304 — esta consulta trae la póliza completa y hasta ahora se tiraba entera salvo el
        // estado: fuera de borrador el trigger de inmutabilidad de field_values solo deja escribir
        // soat_estado. El almacén canónico no tiene esa restricción (el congelamiento es explícito,
        // por frozen_at), así que aquí se recupera un dato ya pagado que se venía descartando.
        await IngestCertificationsAsync(instanceId, tenantId, result, ct);

        var vencimiento = FindHydrated(result.HydratedFields, "soat_vencimiento");
        var aseguradora = FindHydrated(result.HydratedFields, "soat_aseguradora");

        var message = soatEstado switch
        {
            SoatGate.Vigente =>
                "SOAT vigente según el RUNT. El trámite queda listo para la recepción y aprobación del OT.",
            SoatGate.Vencido =>
                "El RUNT reporta el SOAT vencido: el OT no podrá aprobar hasta que esté vigente (gate no subsanable).",
            _ =>
                "El RUNT no reporta un SOAT vigente para el vehículo. Carga el PDF del SOAT para continuar.",
        };

        return (new ValidateSoatResult(
            Vigente: soatEstado == SoatGate.Vigente,
            SoatEstado: soatEstado,
            Vencimiento: vencimiento,
            Aseguradora: aseguradora,
            Message: message), null);
    }

    // El check SOAT de los mappers de vehículo: ok=vigente, fail=vencido, resto=unknown.
    private static string MapSoatEstado(string? checkStatus) => checkStatus?.ToLowerInvariant() switch
    {
        "ok" => SoatGate.Vigente,
        "fail" => SoatGate.Vencido,
        _ => SoatGate.Unknown,
    };

    /// <summary>
    /// Entrega al almacén canónico lo que certificó esta re-consulta (HU #11304). Best-effort: el
    /// estado del SOAT —que es lo que desbloquea la aprobación del OT— ya quedó persistido.
    /// </summary>
    private async Task IngestCertificationsAsync(
        Guid instanceId, Guid tenantId, ConsultationResult result, CancellationToken ct)
    {
        if (certificationIngestion is null || result.Certifications is null)
            return;

        var provenance = new Domain.Certifications.CertificationProvenance(
            Domain.Certifications.CertificationSourceKind.Consultation,
            result.Provider,
            result.QueriedAt ?? DateTimeOffset.UtcNow);

        try
        {
            await certificationIngestion.IngestAsync(
                instanceId, tenantId, result.Certifications, provenance, result.RawPayload, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Silencio acotado: ver RunConsultationHandler.IngestCertificationsAsync.
        }
    }

    private static void UpsertSoatEstado(
        ProcedureInstance instance,
        Guid tenantId,
        IProcedureInstanceRepository repo,
        string soatEstado)
    {
        var now = DateTimeOffset.UtcNow;
        var existing = instance.FieldValues.FirstOrDefault(f => f.FieldKey == SoatGate.FieldKey);
        if (existing is not null)
        {
            existing.ValueText = soatEstado;
            existing.Source = ConsultationSource;
            existing.UpdatedAt = now;
            return;
        }

        var fieldValue = new ProcedureInstanceFieldValue
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureInstanceId = instance.Id,
            FormFieldId = null,
            FieldKey = SoatGate.FieldKey,
            ValueText = soatEstado,
            Source = ConsultationSource,
            CreatedAt = now,
        };
        instance.FieldValues.Add(fieldValue);
        // PK store-generated (uuidv7) con Id ya seteado: Added explícito para forzar INSERT.
        repo.Add(fieldValue);
    }

    private static string? FindHydrated(IReadOnlyList<HydratedField> fields, string key) =>
        fields.FirstOrDefault(f => string.Equals(f.FieldKey, key, StringComparison.OrdinalIgnoreCase))?.ValueText;

    private static string? ResolveProviderKey(string externalRefsJson)
    {
        if (string.IsNullOrWhiteSpace(externalRefsJson))
            return null;
        try
        {
            using var doc = JsonDocument.Parse(externalRefsJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("provider", out var providerEl) &&
                providerEl.ValueKind == JsonValueKind.String)
            {
                return providerEl.GetString();
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }
}
