using Flit.Ict.Grpc.Contracts;
using Flit.Tramites.Application.UseCases.Consultations;
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
        if (!string.IsNullOrWhiteSpace(request.Vin))
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

        reply.SoatStatus = MapVigencia(StatusOf(result, CheckSoat));
        reply.RtmStatus = MapVigencia(StatusOf(result, CheckRtm));
        if (int.TryParse(HydratedOf(result, FieldVehicleYear), out var year) && year > 0)
        {
            reply.VehicleModelYear = year;
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
