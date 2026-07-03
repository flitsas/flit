namespace Flit.Admin.Domain.Improntas;

/// <summary>
/// Datos del vehículo/organización necesarios para generar el Certificado de Improntas Digitales
/// (Res. 17145/2023, Mintransporte) vía Kyverum RUNT (HU #10465). Al menos uno de
/// <see cref="NumMotor"/>/<see cref="NumChasis"/>/<see cref="NumSerie"/> es obligatorio según el
/// contrato del proveedor; esa validación se hace en la capa de aplicación (HU #10467), no aquí.
/// </summary>
/// <param name="Placa">Placa del vehículo.</param>
/// <param name="Documento">
/// Documento de identidad del propietario del vehículo. Requerido por Kyverum RUNT para toda
/// consulta por placa (no documentado originalmente en CONTRATO-API.md — descubierto validando
/// contra el proveedor real: <c>"Documento del propietario requerido para consulta por placa"</c>).
/// </param>
/// <param name="NumMotor">
/// Número de motor. Opcional — verificado contra el proveedor real: Kyverum resuelve los
/// identificadores del vehículo directamente desde el RUNT vía placa+documento cuando no se envían.
/// </param>
/// <param name="NumChasis">Número de chasis. Opcional, mismo hallazgo que <see cref="NumMotor"/>.</param>
/// <param name="NumSerie">Número de serie/VIN. Opcional, mismo hallazgo que <see cref="NumMotor"/>.</param>
/// <param name="Marca">Marca del vehículo. Opcional.</param>
/// <param name="Linea">Línea del vehículo. Opcional.</param>
/// <param name="Modelo">Modelo (año) del vehículo. Opcional.</param>
/// <param name="OrgNombre">Nombre de la organización solicitante (impreso en el certificado).</param>
/// <param name="OrgNit">
/// NIT de la organización solicitante (impreso en el certificado). Opcional — verificado contra el
/// proveedor real: Kyverum genera la impronta sin este dato.
/// </param>
/// <param name="OrgCiudad">
/// Ciudad de la organización solicitante (impresa en el certificado). Opcional — mismo hallazgo que
/// <see cref="OrgNit"/>.
/// </param>
/// <param name="Operador">Operador que solicita la impronta (impreso en el certificado).</param>
public sealed record ImprontaExternalRequest(
    string Placa,
    string Documento,
    string? NumMotor,
    string? NumChasis,
    string? NumSerie,
    string? Marca,
    string? Linea,
    string? Modelo,
    string OrgNombre,
    string? OrgNit,
    string? OrgCiudad,
    string Operador);

/// <summary>
/// Resultado exitoso de Kyverum RUNT al generar una impronta. <see cref="PdfDataUri"/> viene tal cual
/// lo devuelve el proveedor (<c>data:application/pdf;base64,...</c>) — decodificarlo a bytes es
/// responsabilidad de la capa de aplicación (HU #10467), no de este cliente.
/// </summary>
/// <param name="PdfDataUri">Certificado en PDF, embebido como Data URI base64.</param>
/// <param name="Hash">Hash SHA-256 reproducible del certificado.</param>
/// <param name="Radicado">Radicado interno asignado por Kyverum RUNT (formato <c>IMPR-XXXXXXXX</c>).</param>
/// <param name="FechaImpresa">Fecha/hora exacta impresa en el certificado (sin milisegundos).</param>
public sealed record ImprontaExternalResult(
    string PdfDataUri,
    string Hash,
    string Radicado,
    string FechaImpresa);

/// <summary>
/// Error del proveedor Kyverum RUNT al generar una impronta. <see cref="IsTransient"/> distingue
/// fallos transitorios/reintentables (<c>UPSTREAM_UNAVAILABLE</c>, timeout, red) de los definitivos
/// (<c>VALIDATION_ERROR</c>, <c>UNAUTHORIZED</c>). El mensaje NUNCA contiene la API key del proveedor.
/// </summary>
public sealed class ImprontaRuntException(string message, bool isTransient) : Exception(message)
{
    public bool IsTransient { get; } = isTransient;
}

/// <summary>
/// Contrato del cliente HTTP de Kyverum RUNT (HU #10465), dominio <c>runt.kyverum.com</c> — distinto
/// del proveedor de biometría Kyverum Verify (<c>verify.kyverum.com</c>, homólogo
/// <c>IKyverumVerifyClient</c> en <c>Flit.Tramites.Application.Identity</c>). La implementación vive
/// en Infraestructura (<c>ImprontaRuntClient</c>, Typed HttpClient). Lanza
/// <see cref="ImprontaRuntException"/> ante respuestas de error o fallos de comunicación con el
/// proveedor.
/// </summary>
public interface IImprontaExternalClient
{
    Task<ImprontaExternalResult> GenerarAsync(ImprontaExternalRequest request, CancellationToken ct = default);
}
