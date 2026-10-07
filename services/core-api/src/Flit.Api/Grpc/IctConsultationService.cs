using Flit.Ict.Grpc.Contracts;
using Flit.Tramites.Application.UseCases.Consultations;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace Flit.Api.Grpc;

/// <summary>
/// Servidor gRPC de consulta de fuentes externas para core-ict (ICT). Fachada delgada sobre el
/// subsistema de consultas de core-api (<see cref="IConsultationProviderChainResolver"/> para
/// vehículo/conductor, <see cref="IConsultationProviderRegistry"/> para RNMC), reutilizando los
/// proveedores RUNT/SOAT/RTM/RNMC (Verifik/Kyverum) con su modo mock|real y credenciales. Normaliza el
/// <c>ConsultationResult</c> de core-api a los 5 hechos que aplican los validadores de core-ict. El
/// tenant viaja explícito. TODO(ICT-GRPC-AUTH): exigir service-token dedicado.
/// </summary>
public sealed class IctConsultationService(
    IConsultationProviderChainResolver chainResolver,
    IConsultationProviderRegistry registry,
    IConsultationTenantOverrideProvider overrideProvider) : IctConsultation.IctConsultationBase
{
    // Claves normalizadas de core-api (ConsultationCheck.Key / HydratedField.FieldKey).
    private const string CheckSoat = "soat";
    private const string CheckRtm = "tecnomecanica";
    private const string CheckRnmc = "medidas_correctivas";
    private const string FieldVehicleYear = "vehicle_year";
    private const string FieldTransitOffice = "transit_office_name";
    private const string FieldPendingFines = "person_has_pending_fines";
    private const string RnmcProviderKey = "verifik_rnmc";

    public override async Task<ConsultationReply> Query(ConsultationRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        var reply = new ConsultationReply();

        if (!Guid.TryParse(request.TenantId, out var tenantId))
        {
            reply.ErrorCode = "invalid_tenant";
            return reply;
        }

        var ct = context.CancellationToken;
        var tenantOverride = await overrideProvider.GetAsync(tenantId, ct);

        switch ((request.QueryType ?? string.Empty).ToUpperInvariant())
        {
            case "VIN":
                await FillVehicleAsync(reply, ConsultationKind.VehicleVin, tenantId, tenantOverride, request, ct);
                break;
            case "VEHICLE":
                await FillVehicleAsync(reply, ConsultationKind.VehiclePlate, tenantId, tenantOverride, request, ct);
                break;
            case "RNMC":
                await FillRnmcAsync(reply, tenantId, request, ct);
                break;
            case "DRIVER":
                await FillConductorAsync(reply, tenantId, tenantOverride, request, ct);
                break;
            default:
                reply.ErrorCode = "unknown_query_type";
                break;
        }

        return reply;
    }

    private async Task FillVehicleAsync(
        ConsultationReply reply, ConsultationKind kind, Guid tenantId,
        ConsultationTenantOverride? tenantOverride, ConsultationRequest request, CancellationToken ct)
    {
        var fv = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        // Bug #13304 (D6) — consulta por placa con placa Y documento: paridad con el paso 1 del wizard de
        // traspaso, que nunca manda VIN. Si viajara, el proveedor (VIN prioritario) consultaría por VIN y
        // la respuesta no traería el gravamen asociado al titular. Sin documento se conserva el VIN.
        var placaConDocumento = kind == ConsultationKind.VehiclePlate
            && !string.IsNullOrWhiteSpace(request.Plate)
            && !string.IsNullOrWhiteSpace(request.DocumentNumber);
        if (!placaConDocumento && !string.IsNullOrWhiteSpace(request.Vin))
        {
            fv["vin"] = request.Vin;
        }

        if (!string.IsNullOrWhiteSpace(request.Plate))
        {
            fv["plate"] = request.Plate;
        }

        // La consulta por placa (traspaso) requiere el documento del propietario/vendedor.
        if (!string.IsNullOrWhiteSpace(request.DocumentNumber))
        {
            fv["owner_document_type"] = request.DocumentType;
            fv["owner_document_number"] = request.DocumentNumber;
        }

        var ctx = new ConsultationContext(Guid.Empty, tenantId, "vehiculo", fv);
        var result = await chainResolver.ConsultAsync(kind, ctx, tenantOverride, ct);

        // Bug #13304 — resultado COMPLETO para que el borrador ICT lo reutilice sin re-consultar. JSON
        // opaco para core-ict, con PII (titular, acreedor): nunca se loguea ni lleva RawPayload. Solo claves
        // de vehículo (M-1). Si la cadena no hidrató nada y todos los checks quedaron en unknown/error, NO se
        // llena (MENOR-3): core-ict no guarda un snapshot vacío y el pretrámite cae en «sin consulta RUNT».
        var snapshot = PreflightVehicleSnapshot.FromConsultation(result).SoloClavesDeVehiculo();
        if (CadenaRespondio(snapshot))
        {
            reply.VehicleSnapshotJson = PreflightVehicleSnapshotJson.Serialize(snapshot);
            reply.ConsultedAt = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow);
            reply.Provider = result.Provider ?? string.Empty;
            reply.ConsultationKind = kind.ToString();
        }

        reply.SoatStatus = MapVigencia(StatusOf(result, CheckSoat));
        reply.RtmStatus = MapVigencia(StatusOf(result, CheckRtm));
        if (int.TryParse(HydratedOf(result, FieldVehicleYear), out var year) && year > 0)
        {
            reply.VehicleModelYear = year;
        }

        // Organismo de matrícula del RUNT (nombre; el proveedor no entrega DIVIPOLA ni código). core-ict lo
        // usa en TRASPASO para fijar el OT del borrador: v1 lo derivaba de aquí, no del código del cliente.
        var transitOffice = HydratedOf(result, FieldTransitOffice);
        if (!string.IsNullOrWhiteSpace(transitOffice))
        {
            reply.TransitOfficeName = transitOffice;
        }
    }

    private async Task FillRnmcAsync(ConsultationReply reply, Guid tenantId, ConsultationRequest request, CancellationToken ct)
    {
        var provider = registry.Resolve(RnmcProviderKey);
        if (provider is null)
        {
            reply.ErrorCode = "rnmc_provider_unavailable";
            return;
        }

        var fv = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["owner_document_type"] = request.DocumentType,
            ["owner_document_number"] = request.DocumentNumber,
        };
        var ctx = new ConsultationContext(Guid.Empty, tenantId, provider.Key, fv);
        var result = await provider.ConsultAsync(ctx, ct);

        // RNMC bloquea si hay medidas correctivas activas (warn/fail en la respuesta normalizada).
        reply.HasActiveSanctions = StatusOf(result, CheckRnmc) is "warn" or "fail";
    }

    private async Task FillConductorAsync(
        ConsultationReply reply, Guid tenantId, ConsultationTenantOverride? tenantOverride,
        ConsultationRequest request, CancellationToken ct)
    {
        var fv = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["document_type"] = request.DocumentType,
            ["document_number"] = request.DocumentNumber,
        };
        var ctx = new ConsultationContext(Guid.Empty, tenantId, "conductor", fv);
        var result = await chainResolver.ConsultAsync(ConsultationKind.Conductor, ctx, tenantOverride, ct);

        // Paz y salvo ≈ sin comparendos pendientes. Si no hay dato, se deja desconocido (no bloquea).
        var pending = HydratedOf(result, FieldPendingFines);
        if (pending is not null)
        {
            reply.PazYSalvoKnown = true;
            reply.PazYSalvo = !string.Equals(pending, "true", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Bug #13304 (MENOR-3) — hay consulta reutilizable si la cadena hidrató algún campo o algún check trae
    /// un veredicto (ok/warn/fail). Solo unknown/error y sin campos ⇒ no respondió.
    /// </summary>
    private static bool CadenaRespondio(PreflightVehicleSnapshot snapshot) =>
        snapshot.HydratedFields.Count > 0
        || snapshot.Checks.Any(c => c.Status is not ("unknown" or "error"));

    private static string? StatusOf(ConsultationResult result, string checkKey)
    {
        foreach (var check in result.Checks)
        {
            if (string.Equals(check.Key, checkKey, StringComparison.OrdinalIgnoreCase))
            {
                return check.Status;
            }
        }

        return null;
    }

    private static string? HydratedOf(ConsultationResult result, string fieldKey)
    {
        foreach (var field in result.HydratedFields)
        {
            if (string.Equals(field.FieldKey, fieldKey, StringComparison.OrdinalIgnoreCase))
            {
                return field.ValueText;
            }
        }

        return null;
    }

    private static string MapVigencia(string? status) => status switch
    {
        "ok" => "VIGENTE",
        "fail" => "NO_VIGENTE",
        _ => string.Empty,
    };
}
