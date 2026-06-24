namespace Flit.Tramites.Domain.Entities;

/// <summary>
/// Validación biométrica remota de una parte de un trámite (selfie + cédula frontal/reverso).
/// Slice 6 — biométrica (mock). El scoring es determinista y mockeado; la integración real con
/// Anthropic se diferirá (ver IBiometricScorer en Application).
/// </summary>
public sealed class ProcedureInstanceBiometricValidation
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProcedureInstanceId { get; set; }

    /// <summary>'comprador' | 'vendedor'. Null en matrícula inicial (única parte = comprador).</summary>
    public string? Parte { get; set; }

    public string Nombre { get; set; } = string.Empty;
    public string TipoDoc { get; set; } = string.Empty;
    public string Documento { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;

    /// <summary>enviado | en_proceso | aprobado | rechazado | expirado.</summary>
    public string Estado { get; set; } = BiometricEstados.Enviado;

    /// <summary>SHA-256 (hex) del token enviado por magic-link. El token crudo nunca se persiste.</summary>
    public string TokenHash { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }

    // ── Proveedor de validación de identidad (HU #10233 — Kyverum Verify) ────────

    /// <summary>'mock' | 'kyverum'. Default 'mock' (flujo determinista de 3 fotos). 'kyverum' = validación
    /// remota delegada al proveedor externo Kyverum Verify (captura + webhook firmado).</summary>
    public string Provider { get; set; } = BiometricProviders.Mock;

    /// <summary>Id de la verificación en Kyverum (correlación con el webhook). Null cuando provider='mock'.</summary>
    public string? KyverumVerificationId { get; set; }

    /// <summary>URL de captura que abre el participante para completar la validación en Kyverum.</summary>
    public string? CaptureUrl { get; set; }

    /// <summary>Secreto HMAC del webhook CIFRADO con Data Protection API. NUNCA se persiste en claro
    /// ni se expone en DTOs/logs. Se descifra solo para verificar la firma del webhook entrante.</summary>
    public string? WebhookSecretEncrypted { get; set; }

    /// <summary>Estado crudo reportado por Kyverum (p.ej. 'approved'|'rejected'|'pending'). Trazabilidad.</summary>
    public string? ProviderStatus { get; set; }

    /// <summary>Payload del proveedor SANITIZADO (sin PII cruda ni secretos), en jsonb. Trazabilidad.</summary>
    public string? ProviderPayload { get; set; }

    public int Intentos { get; set; }
    public int MaxIntentos { get; set; } = BiometricRules.MaxIntentos;

    public int? Score { get; set; }
    public string? Detalle { get; set; }

    public string? FotoRostroPath { get; set; }
    public string? FotoCedulaFrontalPath { get; set; }
    public string? FotoCedulaReversoPath { get; set; }

    public DateTimeOffset? ValidadoAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }

    public ProcedureInstance? ProcedureInstance { get; set; }
}

/// <summary>Proveedores de validación de identidad (HU #10233).</summary>
public static class BiometricProviders
{
    /// <summary>Scorer determinista local (3 fotos). Default — preserva el flujo Slice 6.</summary>
    public const string Mock = "mock";

    /// <summary>Kyverum Verify: captura remota + webhook firmado (HMAC-SHA256).</summary>
    public const string Kyverum = "kyverum";
}

/// <summary>Estados de la máquina de biométrica.</summary>
public static class BiometricEstados
{
    public const string Enviado = "enviado";
    public const string EnProceso = "en_proceso";
    public const string Aprobado = "aprobado";
    public const string Rechazado = "rechazado";
    public const string Expirado = "expirado";
}

/// <summary>Reglas de negocio de la biométrica (compartidas Application/Domain).</summary>
public static class BiometricRules
{
    public const int MaxIntentos = 5;
    public const int ThresholdAprobacion = 60;
    public const int TokenTtlHoras = 24;

    public const string ParteComprador = "comprador";
    public const string ParteVendedor = "vendedor";
}
