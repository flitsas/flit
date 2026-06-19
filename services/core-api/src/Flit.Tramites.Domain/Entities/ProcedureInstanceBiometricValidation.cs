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
