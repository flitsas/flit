using Flit.Consultas.Grpc.V1;
using Flit.Ict.Domain.Abstractions;
using Flit.Ict.Domain.Validation;
using Google.Protobuf;
using Grpc.Core;

namespace Flit.Ict.Infrastructure.ExternalClients;

/// <summary>
/// Consulta de fuentes externas por core-consultas (Epic #13316, HU #13346; ADR-0065): ICT llama directo a Consultas
/// con su cliente <c>svc-ict</c>, así el consumo queda medido como producto <c>ict</c>. Traduce el resultado normalizado
/// a los 5 hechos que aplican los validadores, con la misma regla que tenía <c>IctConsultationService</c> de core-api
/// (retirado en HU #13348). Si Consultas no responde, el error se propaga y el orquestador reintenta (fail-closed).
/// </summary>
public sealed class ConsultasConsultationClient(ConsultasService.ConsultasServiceClient consultas) : IConsultationClient
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
            throw new InvalidOperationException($"Consulta de fuentes falló: unknown_query_type ({queryType}).");

        var metadata = new Metadata { { "x-flit-tenant-id", tenantId.ToString() } };
        return tipo switch
        {
            "VIN" or "VEHICLE" => Vehiculo(tipo, (await consultas.ConsultarVehiculoAsync(new ConsultarVehiculoRequest
            {
                // Bug #13304 (D6): con placa Y documento no viaja el VIN (paridad con el paso 1 del wizard de traspaso: con
                // VIN el proveedor consultaría por VIN y no traería el gravamen del titular). Sin documento se conserva.
                Vin = !string.IsNullOrWhiteSpace(vin) && !PlacaConDocumento(tipo, plate, documentNumber) ? new Flit.Platform.Grpc.V1.Vin { Valor = vin } : null,
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

    internal static bool PlacaConDocumento(string tipo, string? plate, string? documentNumber) =>
        tipo == "VEHICLE" && !string.IsNullOrWhiteSpace(plate) && !string.IsNullOrWhiteSpace(documentNumber);

    internal static ConsultationResult Vehiculo(string tipo, ResultadoConsulta r) => new(
        SoatStatus: Vigencia(EstadoDe(r, CheckSoat)),
        RtmStatus: Vigencia(EstadoDe(r, CheckRtm)),
        VehicleModelYear: int.TryParse(CampoDe(r, FieldVehicleYear), out var year) && year > 0 ? year : null,
        HasActiveSanctions: false,
        PazYSalvo: null,
        TransitOfficeName: string.IsNullOrWhiteSpace(CampoDe(r, FieldTransitOffice)) ? null : CampoDe(r, FieldTransitOffice),
        Vehicle: Snapshot(tipo, r));

    /// <summary>
    /// Bug #13304 + HU #13348 — resultado COMPLETO de la consulta de vehículo para que el borrador lo reutilice sin
    /// volver a consultar el RUNT. Es el <see cref="ResultadoConsulta"/> de Consultas en JSON (sin respuesta cruda: no se
    /// pide), opaco para core-ict; core-api lo convierte al materializar con su lista blanca de claves. Si la cadena no
    /// respondió (sin campos y todos los chequeos desconocidos o en error) no hay nada reutilizable (null), igual que
    /// cuando la consulta pasaba por core-api.
    /// </summary>
    internal static VehicleConsultationSnapshot? Snapshot(string tipo, ResultadoConsulta r)
    {
        var respondio = r.Campos.Count > 0
            || r.Chequeos.Any(c => c.Estado is not (EstadoChequeo.Unknown or EstadoChequeo.Error or EstadoChequeo.Unspecified));
        if (!respondio)
            return null;

        return new VehicleConsultationSnapshot(
            JsonFormatter.Default.Format(r),
            DateTimeOffset.UtcNow,
            r.Proveedor ?? string.Empty,
            tipo == "VIN" ? VehicleConsultationSnapshot.KindVin : VehicleConsultationSnapshot.KindPlate);
    }

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
