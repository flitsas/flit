using System.Text.Json;
using System.Text.Json.Serialization;
using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.Identity.Events;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

// ── DTOs ─────────────────────────────────────────────────────────────────────

/// <summary>Resultado de iniciar una validación Kyverum: la validación + la URL de captura.</summary>
public sealed record IniciarKyverumVerifyResult(BiometricValidationDto Validation, string CaptureUrl);

/// <summary>
/// Entrada del webhook de Kyverum: id de NUESTRA validación (de la URL del callback, ya que el cuerpo
/// no lo repite), cuerpo CRUDO (para HMAC) y la firma del header <c>x-kv-signature</c>.
/// </summary>
public sealed record KyverumWebhookInput(Guid ValidationId, byte[] RawBody, string? Signature);

// ── Handler: iniciar validación Kyverum (autenticado) — AC1 ───────────────────

/// <summary>
/// Inicia una validación de identidad con Kyverum (HU #10233, AC1). Mismas precondiciones que el flujo
/// mock (instancia en draft, idempotencia por parte), pero delega la captura al proveedor: llama a
/// <see cref="IKyverumVerifyClient"/>, persiste provider/verification_id/capture_url + el secreto del
/// webhook CIFRADO (Data Protection) + el payload sanitizado, deja la validación en <c>en_proceso</c>
/// y emite el evento <see cref="IdentityValidationRequested"/> (outbox). El token_hash se genera al azar
/// (no se usa magic-link) solo para satisfacer la columna NOT NULL/única. Errores del proveedor →
/// <c>proveedor_no_disponible</c> (transitorio, 503) / <c>proveedor_error</c> (definitivo, 502): AC7.
/// </summary>
public sealed class IniciarKyverumVerifyHandler(
    IProcedureInstanceRepository repo,
    IKyverumVerifyClient kyverum,
    IWebhookSecretProtector secretProtector,
    IIdentityValidationEventPublisher events)
{
    public async Task<(IniciarKyverumVerifyResult? Result, string? Error)> HandleAsync(
        Guid id,
        Guid tenantId,
        IniciarBiometriaInput input,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input.Nombre)
            || string.IsNullOrWhiteSpace(input.TipoDoc)
            || string.IsNullOrWhiteSpace(input.Documento)
            || string.IsNullOrWhiteSpace(input.Email))
            return (null, "datos_incompletos");

        var parte = NormalizeParte(input.Parte);
        if (parte is "invalid")
            return (null, "parte_invalida");

        var instance = await repo.GetByIdWithBiometricsAsync(id, tenantId, ct);
        if (instance is null)
            return (null, "not_found");
        if (instance.Status != ProcedureInstanceStatus.Draft)
            return (null, "not_draft");

        var existing = instance.BiometricValidations.FirstOrDefault(v =>
            string.Equals(v.Parte, parte, StringComparison.OrdinalIgnoreCase)
            && v.Estado is BiometricEstados.Enviado or BiometricEstados.EnProceso or BiometricEstados.Aprobado);
        if (existing is not null)
            return (null, "biometria_activa");

        // Id de NUESTRA validación: se genera ANTES de llamar al proveedor para incrustarlo en la
        // webhookUrl (el webhook de Kyverum no repite el id en el cuerpo → correlación por URL).
        var validationId = Guid.NewGuid();

        // Llamada al proveedor. NUNCA propaga la API key ni el secreto en el mensaje de error (AC7).
        KyverumVerifyStartResult provider;
        try
        {
            provider = await kyverum.StartVerificationAsync(
                new KyverumVerifyStartRequest(id, validationId, parte, input.Nombre.Trim(), input.TipoDoc.Trim(), input.Documento.Trim()),
                ct);
        }
        catch (KyverumVerifyException ex)
        {
            return (null, ex.Transient ? "proveedor_no_disponible" : "proveedor_error");
        }

        var now = DateTimeOffset.UtcNow;
        var validation = new ProcedureInstanceBiometricValidation
        {
            Id = validationId,
            TenantId = tenantId,
            ProcedureInstanceId = id,
            Parte = parte,
            Nombre = input.Nombre.Trim(),
            TipoDoc = input.TipoDoc.Trim(),
            Documento = input.Documento.Trim(),
            Email = input.Email.Trim(),
            Estado = BiometricEstados.EnProceso,
            // Sin magic-link en Kyverum: token_hash aleatorio para cumplir NOT NULL/único.
            TokenHash = BiometricToken.Hash(BiometricToken.Generate()),
            ExpiresAt = now.AddHours(BiometricRules.TokenTtlHoras),
            Intentos = 0,
            MaxIntentos = BiometricRules.MaxIntentos,
            CreatedAt = now,
            Provider = BiometricProviders.Kyverum,
            KyverumVerificationId = provider.VerificationId,
            CaptureUrl = provider.CaptureUrl,
            // Secreto del webhook (por-tenant, dashboard). Si no está configurado se deja null y el
            // webhook fallará cerrado (401) hasta configurarlo.
            WebhookSecretEncrypted = string.IsNullOrEmpty(provider.WebhookSecret)
                ? null
                : secretProtector.Protect(provider.WebhookSecret),
            ProviderStatus = provider.ProviderStatus,
            ProviderPayload = provider.RawPayloadSanitized,
        };

        instance.BiometricValidations.Add(validation);
        repo.Add(validation);

        await events.PublishAsync(new IdentityValidationRequested
        {
            TenantId = tenantId,
            ProcedureInstanceId = id,
            ValidationId = validation.Id,
            Provider = BiometricProviders.Kyverum,
            Parte = parte,
            ProviderVerificationId = provider.VerificationId,
        }, ct);

        await repo.SaveChangesAsync(ct);

        var dto = IniciarBiometriaHandler.ToDto(validation, now);
        return (new IniciarKyverumVerifyResult(dto, provider.CaptureUrl), null);
    }

    private static string? NormalizeParte(string? parte)
    {
        if (string.IsNullOrWhiteSpace(parte))
            return null;
        var p = parte.Trim().ToLowerInvariant();
        return p is BiometricRules.ParteComprador or BiometricRules.ParteVendedor ? p : "invalid";
    }
}

// ── Handler: webhook Kyverum (público) — AC2/AC3 ──────────────────────────────

/// <summary>
/// Procesa el webhook de Kyverum (HU #10233, AC2/AC3). Resuelve la validación por NUESTRO id (que viaja
/// en la URL del callback — el cuerpo no lo repite), descifra el secreto de ESA validación y verifica la
/// firma HMAC-SHA256 (<c>x-kv-signature: sha256=&lt;hex&gt;</c>) sobre el cuerpo CRUDO: firma inválida ⇒
/// <c>firma_invalida</c> (401) SIN tocar la BD. Idempotente: si la validación ya está en estado terminal
/// (aprobado|rechazado) devuelve <c>ok</c> sin re-aplicar ni re-emitir evento. Mapea <c>data.aprobado</c>
/// a aprobado|rechazado, persiste provider_status/payload SANITIZADO (sin OCR/PII) y emite
/// <see cref="IdentityValidationCompleted"/> (outbox).
/// </summary>
public sealed class KyverumWebhookHandler(
    IProcedureInstanceRepository repo,
    IWebhookSecretProtector secretProtector,
    IIdentityValidationEventPublisher events)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<(string? Result, string? Error)> HandleAsync(KyverumWebhookInput input, CancellationToken ct = default)
    {
        if (input.RawBody is null || input.RawBody.Length == 0)
            return (null, "cuerpo_invalido");

        // Correlación por NUESTRO id (de la URL). No se confía en el cuerpo para localizar la validación.
        var v = await repo.GetBiometricByIdAsync(input.ValidationId, ct);
        if (v is null)
            return (null, "not_found");

        // Verificación de firma ANTES de confiar en el cuerpo. Firma inválida o secreto ausente ⇒ 401
        // sin cambios en BD (AC3).
        if (string.IsNullOrWhiteSpace(v.WebhookSecretEncrypted))
            return (null, "firma_invalida");

        var secret = secretProtector.Unprotect(v.WebhookSecretEncrypted);
        if (!KyverumWebhookVerifier.IsValid(input.RawBody, input.Signature, secret))
            return (null, "firma_invalida");

        KyverumWebhookPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<KyverumWebhookPayload>(input.RawBody, JsonOptions);
        }
        catch (JsonException)
        {
            return (null, "cuerpo_invalido");
        }

        if (payload?.Data is null)
            return (null, "cuerpo_invalido");

        // Idempotencia: estados terminales no se re-procesan (AC2).
        if (v.Estado is BiometricEstados.Aprobado or BiometricEstados.Rechazado)
            return ("ok", null);

        // El sujeto que corresponde a la parte de esta validación (o el primero).
        var subject = SelectSubject(payload.Data.Subjects, v.Parte);
        var estado = payload.Data.Aprobado ? BiometricEstados.Aprobado : BiometricEstados.Rechazado;

        var now = DateTimeOffset.UtcNow;
        v.Estado = estado;
        v.ProviderStatus = payload.Evento;
        v.ProviderPayload = Sanitize(payload, subject);
        v.Score = subject?.Score;
        v.UpdatedAt = now;
        if (estado == BiometricEstados.Aprobado)
            v.ValidadoAt = now;

        await events.PublishAsync(new IdentityValidationCompleted
        {
            TenantId = v.TenantId,
            ProcedureInstanceId = v.ProcedureInstanceId,
            ValidationId = v.Id,
            Provider = BiometricProviders.Kyverum,
            Parte = v.Parte,
            Estado = estado,
            ProviderStatus = payload.Evento,
            Score = subject?.Score,
        }, ct);

        await repo.SaveChangesAsync(ct);
        return ("ok", null);
    }

    /// <summary>Sujeto de la parte (match por rol, case-insensitive); si no, el primero del arreglo.</summary>
    private static KyverumWebhookSubject? SelectSubject(IReadOnlyList<KyverumWebhookSubject>? subjects, string? parte)
    {
        if (subjects is not { Count: > 0 })
            return null;
        if (!string.IsNullOrWhiteSpace(parte))
        {
            foreach (var s in subjects)
                if (string.Equals(s.Rol, parte, StringComparison.OrdinalIgnoreCase))
                    return s;
        }
        return subjects[0];
    }

    /// <summary>
    /// Payload sanitizado para trazabilidad: ids del evento, veredicto, score, sello de firma y
    /// coincidencias (booleans). NUNCA incluye <c>datosExtraidos</c> (OCR: nombres/fecha/sexo = PII cruda).
    /// </summary>
    private static string Sanitize(KyverumWebhookPayload payload, KyverumWebhookSubject? subject) =>
        JsonSerializer.Serialize(new
        {
            evento = payload.Evento,
            request_id = payload.RequestId,
            delivery_id = payload.DeliveryId,
            aprobado = payload.Data?.Aprobado,
            status = subject?.Status,
            score = subject?.Score,
            firma_serie = subject?.FirmaSerie,
            coincidencias = subject?.Coincidencias,
            proveedor = BiometricProviders.Kyverum,
        });
}

// ── Contrato del webhook Kyverum (CONTRATO-API.md). datosExtraidos se omite a propósito (PII OCR). ──
public sealed record KyverumWebhookPayload(
    [property: JsonPropertyName("evento")] string? Evento,
    [property: JsonPropertyName("requestId")] string? RequestId,
    [property: JsonPropertyName("data")] KyverumWebhookData? Data,
    [property: JsonPropertyName("deliveryId")] string? DeliveryId,
    [property: JsonPropertyName("ts")] string? Ts);

public sealed record KyverumWebhookData(
    [property: JsonPropertyName("aprobado")] bool Aprobado,
    [property: JsonPropertyName("closedAt")] string? ClosedAt,
    [property: JsonPropertyName("subjects")] IReadOnlyList<KyverumWebhookSubject>? Subjects);

public sealed record KyverumWebhookSubject(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("rol")] string? Rol,
    [property: JsonPropertyName("documento")] string? Documento,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("score")] int? Score,
    [property: JsonPropertyName("firmaSerie")] string? FirmaSerie,
    [property: JsonPropertyName("coincidencias")] KyverumCoincidencias? Coincidencias);

public sealed record KyverumCoincidencias(
    [property: JsonPropertyName("documento")] bool Documento,
    [property: JsonPropertyName("nombre")] bool Nombre);
