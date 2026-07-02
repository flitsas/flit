namespace Flit.Admin.Application.Improntas.GenerarImpronta;

/// <summary>
/// Payload de generación de impronta (POST /api/v1/admin/improntas/generate). Todos los campos son
/// nullable para validar explícitamente en el handler (un campo ausente devuelve 422, no un 400 de
/// binding) — mismo criterio que <c>CreateTransitOfficeRequest</c>.
/// </summary>
/// <param name="Placa">Placa del vehículo. Requerida.</param>
/// <param name="NumMotor">Número de motor. Al menos uno de NumMotor/NumChasis/NumSerie es requerido.</param>
/// <param name="NumChasis">Número de chasis. Al menos uno de NumMotor/NumChasis/NumSerie es requerido.</param>
/// <param name="NumSerie">Número de serie/VIN. Al menos uno de NumMotor/NumChasis/NumSerie es requerido.</param>
/// <param name="Marca">Marca del vehículo. Opcional.</param>
/// <param name="Linea">Línea del vehículo. Opcional.</param>
/// <param name="Modelo">Modelo (año) del vehículo. Opcional.</param>
/// <param name="OrgNombre">Nombre de la organización solicitante. Requerido de facto (impreso en el certificado).</param>
/// <param name="OrgNit">NIT de la organización solicitante. Requerido de facto.</param>
/// <param name="OrgCiudad">Ciudad de la organización solicitante. Requerido de facto.</param>
/// <param name="Operador">Operador que solicita la impronta. Requerido de facto.</param>
public sealed record GenerarImprontaRequest(
    string? Placa,
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
