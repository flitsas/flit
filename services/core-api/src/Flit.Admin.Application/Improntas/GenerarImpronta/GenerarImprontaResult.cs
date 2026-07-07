using Flit.Admin.Application.Companies.CreateCompany;

namespace Flit.Admin.Application.Improntas.GenerarImpronta;

/// <summary>Resultado del caso de uso de generación de impronta, con el status HTTP a devolver.</summary>
public enum GenerarImprontaStatus
{
    /// <summary>200 — PDF generado y persistido.</summary>
    Success,

    /// <summary>422 — validación local (placa/identificador de vehículo/org/operador faltante).</summary>
    ValidationFailed,

    /// <summary>422/400 — Kyverum RUNT respondió <c>VALIDATION_ERROR</c> (AC3).</summary>
    ProviderValidationFailed,

    /// <summary>401 — Kyverum RUNT rechazó la autenticación (key inválida o scope insuficiente).</summary>
    ProviderUnauthorized,

    /// <summary>502 — Kyverum RUNT no disponible temporalmente (<c>UPSTREAM_UNAVAILABLE</c>, timeout, red) (AC3).</summary>
    ProviderUnavailable,
}

/// <summary>
/// Resultado de <see cref="GenerarImprontaHandler"/>. En éxito trae los bytes del PDF ya decodificados
/// (Data URI → <c>byte[]</c>) más la metadata de trazabilidad para que el endpoint sirva el archivo con
/// <c>Results.File</c> y nombre derivado del radicado. En fallo, <see cref="Status"/> indica el status
/// HTTP a devolver; <see cref="Errors"/> se usa solo para <see cref="GenerarImprontaStatus.ValidationFailed"/>
/// (shape <c>{ field, message }</c>, reutiliza <see cref="CompanyValidationError"/>); <see cref="ProviderErrorCode"/>/
/// <see cref="ProviderMessage"/> se usan para los demás fallos (errores del proveedor Kyverum RUNT).
/// </summary>
public sealed class GenerarImprontaResult
{
    private GenerarImprontaResult(
        GenerarImprontaStatus status,
        byte[]? pdfBytes,
        string? radicado,
        string? hash,
        string? fechaImpresa,
        IReadOnlyList<CompanyValidationError> errors,
        string? providerErrorCode,
        string? providerMessage)
    {
        Status = status;
        PdfBytes = pdfBytes;
        Radicado = radicado;
        Hash = hash;
        FechaImpresa = fechaImpresa;
        Errors = errors;
        ProviderErrorCode = providerErrorCode;
        ProviderMessage = providerMessage;
    }

    public GenerarImprontaStatus Status { get; }

    public byte[]? PdfBytes { get; }

    public string? Radicado { get; }

    public string? Hash { get; }

    public string? FechaImpresa { get; }

    public IReadOnlyList<CompanyValidationError> Errors { get; }

    public string? ProviderErrorCode { get; }

    public string? ProviderMessage { get; }

    public static GenerarImprontaResult Success(byte[] pdfBytes, string radicado, string hash, string fechaImpresa) =>
        new(GenerarImprontaStatus.Success, pdfBytes, radicado, hash, fechaImpresa, [], null, null);

    public static GenerarImprontaResult Invalid(IReadOnlyList<CompanyValidationError> errors) =>
        new(GenerarImprontaStatus.ValidationFailed, null, null, null, null, errors, null, null);

    public static GenerarImprontaResult ProviderValidation(string message) =>
        new(GenerarImprontaStatus.ProviderValidationFailed, null, null, null, null, [], "validation_error", message);

    public static GenerarImprontaResult ProviderUnauthorized(string message) =>
        new(GenerarImprontaStatus.ProviderUnauthorized, null, null, null, null, [], "unauthorized", message);

    public static GenerarImprontaResult ProviderUnavailable(string message) =>
        new(GenerarImprontaStatus.ProviderUnavailable, null, null, null, null, [], "upstream_unavailable", message);
}
