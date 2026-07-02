namespace Flit.Admin.Application.Improntas.GenerarImpronta;

/// <summary>
/// Payload de generación de impronta (POST /api/v1/admin/improntas/generate). Todos los campos son
/// nullable para validar explícitamente en el handler (un campo ausente devuelve 422, no un 400 de
/// binding) — mismo criterio que <c>CreateTransitOfficeRequest</c>.
/// </summary>
/// <param name="Placa">Placa del vehículo. Requerida.</param>
/// <param name="Documento">
/// Documento de identidad del propietario del vehículo. Requerido por Kyverum RUNT para toda
/// consulta por placa (no documentado originalmente en CONTRATO-API.md — descubierto validando
/// contra el proveedor real, ver ADR-0022 / notas de HU #10465).
/// </param>
/// <param name="NumMotor">
/// Número de motor. Opcional — verificado contra el proveedor real: Kyverum resuelve los
/// identificadores del vehículo directamente desde el RUNT vía placa+documento cuando no se envían.
/// </param>
/// <param name="NumChasis">Número de chasis. Opcional, mismo hallazgo que NumMotor.</param>
/// <param name="NumSerie">Número de serie/VIN. Opcional, mismo hallazgo que NumMotor.</param>
/// <param name="Marca">Marca del vehículo. Opcional.</param>
/// <param name="Linea">Línea del vehículo. Opcional.</param>
/// <param name="Modelo">Modelo (año) del vehículo. Opcional.</param>
/// <param name="OrgNombre">Nombre de la organización solicitante. Requerido de facto (impreso en el certificado).</param>
/// <param name="OrgNit">
/// NIT de la organización solicitante. Opcional — verificado contra el proveedor real: Kyverum
/// genera la impronta sin este dato.
/// </param>
/// <param name="OrgCiudad">Ciudad de la organización solicitante. Opcional, mismo hallazgo que OrgNit.</param>
/// <param name="Operador">Operador que solicita la impronta. Requerido de facto.</param>
public sealed record GenerarImprontaRequest(
    string? Placa,
    string? Documento,
    string? NumMotor,
    string? NumChasis,
    string? NumSerie,
    string? Marca,
    string? Linea,
    string? Modelo,
    string? OrgNombre,
    string? OrgNit,
    string? OrgCiudad,
    string? Operador);
