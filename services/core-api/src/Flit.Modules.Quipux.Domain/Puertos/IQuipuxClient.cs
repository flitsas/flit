namespace Flit.Modules.Quipux.Domain.Puertos;

/// <summary>
/// Puerto hacia la API externa de Quipux. La implementación vive en Infrastructure
/// (<c>QuipuxApiClient</c>, typed HttpClient) y es la que gestiona login y caché de token; el
/// dominio y la Application nunca ven <c>HttpClient</c> ni el token.
/// </summary>
/// <remarks>
/// En FLIT 1.0 esto era un microservicio aparte (BackCrudQuipux). Se colapsa a un adapter porque
/// aquel gateway no tenía lógica de negocio: era login + subir a S3 + reenviar el POST.
/// </remarks>
public interface IQuipuxClient
{
    /// <summary>
    /// Radica el documento. El PDF ya debe estar en el bucket: <paramref name="request"/> lleva la
    /// key S3 en <c>ContenidoDocumento</c>, no el binario.
    /// </summary>
    Task<QuipuxRegisterResult> RegisterDocumentAsync(
        QuipuxRegisterRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Consulta el estado de un documento ya radicado.</summary>
    Task<QuipuxValidateStatusResult> ValidateStatusAsync(
        QuipuxValidateStatusRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>Payload de radicación. Espeja el contrato de Quipux (nombres en español, tal cual los espera).</summary>
public sealed class QuipuxRegisterRequest
{
    public required string Placa { get; init; }

    public required string Vin { get; init; }

    public required string CodigoDivipo { get; init; }

    public required string Consumidor { get; init; }

    public required int TipoTramite { get; init; }

    public required int TipoRequisito { get; init; }

    /// <summary>Nombre del documento en Quipux. Es la llave de correlación para consultar el estado después.</summary>
    public required string Documento { get; init; }

    public required int TipoDocumentoPropietario { get; init; }

    public required string NroDocumentoPropietario { get; init; }

    public required int TipoDocumentoFuncionario { get; init; }

    public required string NroDocumentoFuncionario { get; init; }

    /// <summary>Key del objeto en el bucket de Quipux (<c>FLIT/{documento}.pdf</c>), no el PDF.</summary>
    public required string ContenidoDocumento { get; init; }
}

/// <summary>Consulta de estado. Quipux correlaciona por nombre de documento + organismo.</summary>
public sealed class QuipuxValidateStatusRequest
{
    public required string Documento { get; init; }

    public required string CodigoDivipo { get; init; }

    public required string Consumidor { get; init; }
}

/// <summary>Respuesta de radicación. <c>Codigo == 81</c> es el único éxito.</summary>
public sealed record QuipuxRegisterResult(int Codigo, string? Descripcion);

/// <summary>
/// Respuesta de consulta. <paramref name="EstadoTramiteCodigo"/> es null cuando Quipux aún no
/// resolvió: en 1.0 ese null se colapsaba a '0' y se perdía la distinción entre "sin resolver" y
/// "respuesta sin estado".
/// </summary>
public sealed record QuipuxValidateStatusResult(
    int Codigo,
    string? Descripcion,
    int? EstadoTramiteCodigo,
    string? EstadoTramiteDescripcion);

/// <summary>
/// Fallo al hablar con Quipux. <see cref="IsTransient"/> distingue "reintentable" (timeout, red,
/// 5xx) de definitivo (payload inválido, respuesta ilegible), siguiendo la convención del repo
/// (<c>RuesApiClient</c>, <c>KyverumVerifyClient</c>).
/// </summary>
public sealed class QuipuxException : Exception
{
    public QuipuxException(string message, bool isTransient)
        : base(message) => IsTransient = isTransient;

    public QuipuxException(string message, bool isTransient, Exception innerException)
        : base(message, innerException) => IsTransient = isTransient;

    /// <summary>Si es true, el worker deja la submission reclamable para el próximo ciclo.</summary>
    public bool IsTransient { get; }
}
