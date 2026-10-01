using System.Globalization;
using System.Text.Json;
using Flit.Queries.Domain.Time;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

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
///
/// <para>Bug #13194 — un RUNT que NO reporta SOAT (<c>unknown</c>) no degrada un <c>soat_estado</c>
/// vigente que vino de un soporte manual (PDF leído por OCR o captura del usuario) cuyo vencimiento no
/// pasó: el RUNT no dijo que no hubiera SOAT, solo que no lo reporta. Un vencido explícito del RUNT
/// (<c>fail</c>) sí manda: es la fuente oficial afirmando lo contrario del documento.</para>
///
/// <para>Bug #13194 (review, SEC) — el soporte manual solo cuenta si el trámite tiene un ADJUNTO de SOAT
/// vigente (no histórico) y una fecha de vencimiento legible que no haya pasado en el día de Colombia.
/// Sin adjunto, sin fecha o con fecha ilegible NO hay soporte (fail-closed): el origen <c>user</c>/<c>ocr</c>
/// del campo lo escribe el cliente y por sí solo no prueba nada.</para>
///
/// <para>Bug #13194 — si el proveedor lanza, o devuelve el check <c>provider</c> en <c>error</c> (5xx del
/// gateway, timeout, red), se loguea y se devuelve <see cref="ProviderError"/> («la consulta no respondió»):
/// «Enviar al OT» sigue, como cuando falta la plantilla.</para>
/// </summary>
public sealed class ValidateSoatViaRuntHandler(
    IProcedureInstanceRepository instanceRepo,
    ICatalogRepository catalogRepo,
    IConsultationProviderRegistry registry,
    Certifications.ICertificationIngestionService? certificationIngestion = null,
    ILogger<ValidateSoatViaRuntHandler>? logger = null,
    TimeProvider? clock = null)
{
    /// <summary>El proveedor RUNT lanzó: la consulta no respondió (no es un SOAT no vigente).</summary>
    public const string ProviderError = "provider_error";

    private const string TemplateCode = "RUNT_VEHICLE";
    private const string SoatCheckKey = "soat";
    private const string ProviderCheckKey = "provider";
    private const string ErrorStatus = "error";
    private const string ConsultationSource = "consultation";
    private const string SoatVencimientoKey = "soat_vencimiento";

    /// <summary>Orígenes de un soporte manual del SOAT: OCR del PDF cargado o captura del usuario.</summary>
    private static readonly string[] ManualSources = ["ocr", "user"];

    private static readonly string[] FormatosFecha = ["yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy", "yyyy/MM/dd", "dd-MM-yyyy"];

    private readonly ILogger<ValidateSoatViaRuntHandler> _logger =
        logger ?? NullLogger<ValidateSoatViaRuntHandler>.Instance;

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public async Task<(ValidateSoatResult? Result, string? Error)> HandleAsync(
        Guid instanceId,
        Guid tenantId,
        CancellationToken ct = default)
    {
        var instance = await instanceRepo.GetByIdWithDetailsAsync(instanceId, tenantId, ct);
        if (instance is null)
            return (null, "instance_not_found");

        // La validación de SOAT es exclusiva del paso post-asignación de placa: el trámite está en
        // 'asignado' (ADR-0059), gestionando SOAT e impuestos antes de «Enviar al OT».
        if (!string.Equals(instance.Status, TramiteEstado.Asignado, StringComparison.OrdinalIgnoreCase))
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

        ConsultationResult result;
        try
        {
            result = await provider.ConsultAsync(
                new ConsultationContext(instance.Id, instance.TenantId, TemplateCode, fieldValues), ct);
        }
        // Solo la cancelación DEL LLAMADOR se propaga: un OperationCanceledException interno (p. ej. el
        // timeout de HttpClient, que lanza TaskCanceledException) es un proveedor que no respondió.
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            ValidateSoatLog.ProveedorFallo(_logger, instanceId, providerKey, ex.GetType().Name);
            return (null, ProviderError);
        }

        // Bug #13194 (P3-09) — los proveedores no lanzan ante un 5xx del gateway, un timeout o un error de
        // red: devuelven un resultado con el check «provider» en «error» y SIN check de SOAT. Eso no es «el
        // RUNT respondió sin SOAT» (unknown) sino «el RUNT no respondió»: mismo trato que la excepción, sin
        // escribir soat_estado (no se degrada un soporte manual vigente).
        if (ProveedorNoRespondio(result))
        {
            ValidateSoatLog.ProveedorSinRespuesta(_logger, instanceId, providerKey);
            return (null, ProviderError);
        }

        var soatCheck = result.Checks.FirstOrDefault(c =>
            string.Equals(c.Key, SoatCheckKey, StringComparison.OrdinalIgnoreCase));
        var soatEstado = MapSoatEstado(soatCheck?.Status);

        // Bug #13194 — «el RUNT no lo reporta» no es «no hay SOAT»: se conserva el soporte manual vigente.
        var conservaSoporteManual = soatEstado == SoatGate.Unknown
            && await TieneSoporteManualVigenteAsync(instance, tenantId, ct);
        if (conservaSoporteManual)
        {
            soatEstado = SoatGate.Vigente;
        }
        else
        {
            UpsertSoatEstado(instance, tenantId, instanceRepo, soatEstado);
            await instanceRepo.SaveChangesAsync(ct);
        }

        // HU #11304 — esta consulta trae la póliza completa y hasta ahora se tiraba entera salvo el
        // estado: fuera de borrador el trigger de inmutabilidad de field_values solo deja escribir
        // soat_estado. El almacén canónico no tiene esa restricción (el congelamiento es explícito,
        // por frozen_at), así que aquí se recupera un dato ya pagado que se venía descartando.
        await IngestCertificationsAsync(instanceId, tenantId, result, ct);

        var vencimiento = FindHydrated(result.HydratedFields, "soat_vencimiento");
        var aseguradora = FindHydrated(result.HydratedFields, "soat_aseguradora");

        var message = soatEstado switch
        {
            SoatGate.Vigente when conservaSoporteManual =>
                "El RUNT no reporta el SOAT, pero el soporte cargado está vigente: se conserva.",
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

    /// <summary>
    /// Convención de los proveedores de consulta: el check <c>provider</c> con estado <c>error</c> marca
    /// que no hubo respuesta utilizable (no-2xx, timeout, red, JSON ilegible).
    /// </summary>
    internal static bool ProveedorNoRespondio(ConsultationResult result) =>
        result.Checks.Any(c =>
            string.Equals(c.Key, ProviderCheckKey, StringComparison.OrdinalIgnoreCase)
            && string.Equals(c.Status, ErrorStatus, StringComparison.OrdinalIgnoreCase));

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

    /// <summary>
    /// ¿El trámite ya tiene el SOAT vigente por un soporte manual? Exige las tres cosas (fail-closed):
    /// <list type="number">
    /// <item><c>soat_estado=vigente</c> escrito por el OCR del PDF o por el usuario (no por una consulta);</item>
    /// <item>una fecha <c>soat_vencimiento</c> legible que no haya pasado en el día de Colombia;</item>
    /// <item>un adjunto de SOAT (<see cref="AttachmentRules.SoatEvidenceTipos"/>) no histórico en el trámite.</item>
    /// </list>
    /// El adjunto se consulta al final y solo si lo demás ya se cumple: es la única lectura extra.
    /// </summary>
    private async Task<bool> TieneSoporteManualVigenteAsync(
        ProcedureInstance instance, Guid tenantId, CancellationToken ct)
    {
        var estado = instance.FieldValues.FirstOrDefault(f =>
            string.Equals(f.FieldKey, SoatGate.FieldKey, StringComparison.OrdinalIgnoreCase));
        if (estado is null
            || !SoatGate.IsSatisfied(estado.ValueText)
            || !ManualSources.Contains(estado.Source, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        var vencimiento = instance.FieldValues.FirstOrDefault(f =>
            string.Equals(f.FieldKey, SoatVencimientoKey, StringComparison.OrdinalIgnoreCase))?.ValueText;
        var fecha = ParseFecha(vencimiento);
        if (fecha is null || fecha.Value < HoyEnColombia())
            return false;

        var conAdjuntos = await instanceRepo.GetByIdWithAttachmentsAsync(instance.Id, tenantId, ct);
        return conAdjuntos?.Attachments.Any(a =>
            !a.IsHistorico && AttachmentRules.IsSoatEvidenceTipo(a.Tipo)) == true;
    }

    /// <summary>Día calendario de hoy en Colombia (UTC−05:00 fijo, sin horario de verano).</summary>
    private DateOnly HoyEnColombia() =>
        DateOnly.FromDateTime(_clock.GetUtcNow().ToOffset(ColombiaTime.Offset).DateTime);

    /// <summary>Fecha de vencimiento como día calendario; <c>null</c> si falta o no se puede leer.</summary>
    private static DateOnly? ParseFecha(string? valor)
    {
        var texto = valor?.Trim();
        if (string.IsNullOrEmpty(texto))
            return null;

        if (DateOnly.TryParseExact(
                texto, FormatosFecha, CultureInfo.InvariantCulture, DateTimeStyles.None, out var exacta))
        {
            return exacta;
        }

        // Con hora: se toma la fecha CALENDARIO escrita, sin convertir de huso (RN-08, Épica #12552).
        return DateTimeOffset.TryParse(texto, CultureInfo.InvariantCulture, DateTimeStyles.None, out var conHora)
            ? DateOnly.FromDateTime(conHora.DateTime)
            : null;
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

/// <summary>Logging source-generado (CA1848) de la validación del SOAT. NUNCA incluye PII.</summary>
internal static partial class ValidateSoatLog
{
    // Error, no Warning: la validación del gate SOAT quedó sin respuesta y debe alertar. Solo el TIPO de
    // la excepción: el mensaje y el stack del proveedor pueden arrastrar la placa o la respuesta del RUNT.
    [LoggerMessage(
        EventId = 13194,
        Level = LogLevel.Error,
        Message = "El proveedor RUNT {ProviderKey} falló ({ExceptionType}) al validar el SOAT del trámite {InstanceId}; se trata como consulta sin respuesta.")]
    public static partial void ProveedorFallo(ILogger logger, Guid instanceId, string providerKey, string exceptionType);

    // Mismo nivel que la excepción: el RUNT no respondió (5xx del gateway, timeout, red) y el gate quedó sin dato.
    [LoggerMessage(
        EventId = 13195,
        Level = LogLevel.Error,
        Message = "El proveedor RUNT {ProviderKey} no respondió al validar el SOAT del trámite {InstanceId}; se trata como consulta sin respuesta.")]
    public static partial void ProveedorSinRespuesta(ILogger logger, Guid instanceId, string providerKey);
}
