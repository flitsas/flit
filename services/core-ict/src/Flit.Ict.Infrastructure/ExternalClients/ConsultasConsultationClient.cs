using Flit.Consultas.Grpc.V1;
using Flit.Ict.Domain.Abstractions;
using Flit.Ict.Domain.Validation;
using Grpc.Core;
using Microsoft.Extensions.Logging;

namespace Flit.Ict.Infrastructure.ExternalClients;

/// <summary>
/// Consulta de fuentes externas por core-consultas (Epic #13316, HU #13346; ADR-0065): ICT llama directo a Consultas
/// con su cliente <c>svc-ict</c>, así el consumo queda medido como producto <c>ict</c>. Traduce el resultado normalizado
/// a los 5 hechos que aplican los validadores, con la MISMA regla que <c>IctConsultationService</c> de core-api. Si
/// Consultas no responde, cae al camino de siempre (core-api) cuando <c>CoreConsultas:Respaldo</c> está encendida (por
/// defecto); si no, el error se propaga y el orquestador reintenta (fail-closed, como hoy).
/// </summary>
public sealed class ConsultasConsultationClient(
    ConsultasService.ConsultasServiceClient consultas,
    IctGrpcConsultationClient respaldo,
    bool usarRespaldo,
    ILogger<ConsultasConsultationClient> logger) : IConsultationClient
{
    // Claves normalizadas del módulo de consultas (ConsultationCheck.Key / HydratedField.FieldKey).
    private const string CheckSoat = "soat";
    private const string CheckRtm = "tecnomecanica";
    private const string CheckRnmc = "medidas_correctivas";
    private const string FieldVehicleYear = "vehicle_year";
    private const string FieldTransitOffice = "transit_office_name";
    private const string FieldPendingFines = "person_has_pending_fines";

    public async Task<ConsultationResult> QueryAsync(
        Guid tenantId, string queryType, string plate, string vin, string documentType, string documentNumber, CancellationToken ct = default)
    {
        var tipo = (queryType ?? string.Empty).ToUpperInvariant();
        if (tipo is not ("VIN" or "VEHICLE" or "RNMC" or "DRIVER"))
            return await respaldo.QueryAsync(tenantId, queryType ?? string.Empty, plate, vin, documentType, documentNumber, ct).ConfigureAwait(false);

        try
        {
            var metadata = new Metadata { { "x-flit-tenant-id", tenantId.ToString() } };
            return tipo switch
            {
                "VIN" or "VEHICLE" => Vehiculo((await consultas.ConsultarVehiculoAsync(new ConsultarVehiculoRequest
                {
                    Vin = tipo == "VIN" && !string.IsNullOrWhiteSpace(vin) ? new Flit.Platform.Grpc.V1.Vin { Valor = vin } : null,
                    Placa = !string.IsNullOrWhiteSpace(plate) ? new Flit.Platform.Grpc.V1.Placa { Valor = plate } : null,
                    Propietario = Documento(documentType, documentNumber),
                }, metadata, cancellationToken: ct).ConfigureAwait(false)).Resultado),
                "RNMC" => Rnmc((await consultas.ConsultarRnmcAsync(new ConsultarRnmcRequest
                {
                    Documento = Documento(documentType, documentNumber),
                }, metadata, cancellationToken: ct).ConfigureAwait(false)).Resultado),
                _ => Conductor((await consultas.ConsultarConductorAsync(new ConsultarConductorRequest
                {
                    Documento = Documento(documentType, documentNumber),
                }, metadata, cancellationToken: ct).ConfigureAwait(false)).Resultado),
            };
        }
        catch (Exception ex) when (ex is RpcException or InvalidOperationException && usarRespaldo && !ct.IsCancellationRequested)
        {
            ConsultasIctLog.Respaldo(logger, tipo, tenantId, ex);
            return await respaldo.QueryAsync(tenantId, queryType ?? string.Empty, plate, vin, documentType, documentNumber, ct).ConfigureAwait(false);
        }
    }

    internal static ConsultationResult Vehiculo(ResultadoConsulta r) => new(
        SoatStatus: Vigencia(EstadoDe(r, CheckSoat)),
        RtmStatus: Vigencia(EstadoDe(r, CheckRtm)),
        VehicleModelYear: int.TryParse(CampoDe(r, FieldVehicleYear), out var year) && year > 0 ? year : null,
        HasActiveSanctions: false,
        PazYSalvo: null,
        TransitOfficeName: string.IsNullOrWhiteSpace(CampoDe(r, FieldTransitOffice)) ? null : CampoDe(r, FieldTransitOffice));

    // RNMC bloquea si hay medidas correctivas activas (warn/fail en la respuesta normalizada).
    internal static ConsultationResult Rnmc(ResultadoConsulta r) => new(
        null, null, null, HasActiveSanctions: EstadoDe(r, CheckRnmc) is EstadoChequeo.Warn or EstadoChequeo.Fail, PazYSalvo: null, TransitOfficeName: null);

    // Paz y salvo ≈ sin comparendos pendientes. Si no hay dato, queda desconocido (no bloquea).
    internal static ConsultationResult Conductor(ResultadoConsulta r)
    {
        var pendientes = CampoDe(r, FieldPendingFines);
        return new(null, null, null, false,
            PazYSalvo: pendientes is null ? null : !string.Equals(pendientes, "true", StringComparison.OrdinalIgnoreCase),
            TransitOfficeName: null);
    }

    private static EstadoChequeo? EstadoDe(ResultadoConsulta r, string clave) =>
        r.Chequeos.FirstOrDefault(c => string.Equals(c.Clave, clave, StringComparison.OrdinalIgnoreCase))?.Estado;

    private static string? CampoDe(ResultadoConsulta r, string clave) =>
        r.Campos.FirstOrDefault(c => string.Equals(c.Clave, clave, StringComparison.OrdinalIgnoreCase)) is { HasValorTexto: true } c ? c.ValorTexto : null;

    private static string? Vigencia(EstadoChequeo? estado) => estado switch
    {
        EstadoChequeo.Ok => "VIGENTE",
        EstadoChequeo.Fail => "NO_VIGENTE",
        _ => null,
    };

    private static Flit.Platform.Grpc.V1.DocumentoIdentidad? Documento(string tipo, string numero) =>
        string.IsNullOrWhiteSpace(numero) ? null : new Flit.Platform.Grpc.V1.DocumentoIdentidad { Tipo = tipo ?? string.Empty, Numero = numero };
}

internal static partial class ConsultasIctLog
{
    [LoggerMessage(EventId = 7421, Level = LogLevel.Warning,
        Message = "core-consultas no respondió la consulta {Tipo} de {TenantId}: se usó el respaldo (core-api)")]
    public static partial void Respaldo(ILogger logger, string tipo, Guid tenantId, Exception ex);
}
