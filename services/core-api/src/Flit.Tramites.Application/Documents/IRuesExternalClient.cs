namespace Flit.Tramites.Application.Documents;

/// <summary>
/// Datos para autogenerar el Certificado RUES de un actor jurídico (NIT). En v1.0 (producción) el
/// flujo llamaba a un servicio externo de generación documental (<c>document-generator/rues-certificate</c>)
/// pasando el tipo de documento (<c>NIT</c>) y el número; el certificado (PDF) lo produce el proveedor.
/// </summary>
/// <param name="Nit">Número de identificación tributaria del actor jurídico (sin dígito de verificación).</param>
/// <param name="RazonSocial">Razón social del actor, si se conoce (opcional; el proveedor la resuelve por NIT).</param>
public sealed record RuesExternalRequest(string Nit, string? RazonSocial = null);

/// <summary>
/// Resultado exitoso del proveedor RUES. <see cref="PdfDataUri"/> viene como Data URI
/// (<c>data:application/pdf;base64,...</c>) o base64 crudo — la capa de aplicación lo decodifica a bytes.
/// </summary>
/// <param name="PdfDataUri">Certificado RUES en PDF, embebido como Data URI/base64.</param>
/// <param name="Radicado">Radicado/consecutivo asignado por el proveedor, si lo entrega.</param>
public sealed record RuesExternalResult(string PdfDataUri, string? Radicado);

/// <summary>
/// Error del proveedor RUES. <see cref="IsTransient"/> distingue fallos reintentables (timeout, red,
/// upstream caído) de los definitivos (validación, no autorizado). El mensaje NUNCA contiene la API key.
/// </summary>
public sealed class RuesExternalException(string message, bool isTransient) : Exception(message)
{
    public bool IsTransient { get; } = isTransient;
}

/// <summary>
/// Contrato del cliente de autogeneración del Certificado RUES (RF36). La implementación productiva
/// (<c>RuesApiClient</c>, Typed HttpClient) vive en Infraestructura y se registra SOLO cuando la
/// integración está habilitada (<c>Rues:Enabled=true</c>). Cuando no está registrado, el handler cae al
/// respaldo de carga manual del documento <c>rues</c> (el tipo ya existe en el catálogo y su regla
/// condicional <c>nit_rues</c>), de modo que la funcionalidad es aditiva y no rompe ningún ambiente.
/// El contrato de cable concreto con el servicio de 2.0 está pendiente de confirmación del Líder Técnico.
/// </summary>
public interface IRuesExternalClient
{
    Task<RuesExternalResult> GenerarAsync(RuesExternalRequest request, CancellationToken ct = default);
}
