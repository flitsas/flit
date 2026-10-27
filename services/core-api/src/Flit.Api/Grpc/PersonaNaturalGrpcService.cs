using Flit.Api.Authorization;
using Flit.Infrastructure.Persistence;
using Flit.Platform.Sdk;
using Flit.Platform.Sdk.Grpc;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.Services;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using Flit.Tramites.Grpc.V1;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;

namespace Flit.Api.Grpc;

/// <summary>
/// Fachada gRPC del portal de persona natural (HU #14034, Feature #14022, Épica #14021, ADR-0075). Adaptador delgado:
/// reutiliza los casos de uso de Trámites y no reimplementa ninguna regla. NO tiene ningún método que cree o modifique un
/// trámite (las escrituras van por la cola).
/// <para>
/// Cada método empieza por <see cref="Verificar"/>: el llamante tiene que ser <c>svc-personanatural</c> y la empresa de la
/// llamada, la de Persona Natural (<see cref="PersonaNaturalServicioAuthorization.VerificarEmpresa"/>). Los métodos cuya
/// implementación llega con otra HU responden <c>UNIMPLEMENTED</c> con <c>NO_IMPLEMENTADO</c>, también tras la verificación,
/// para que un llamante ajeno no distinga qué existe.
/// </para>
/// </summary>
public sealed class PersonaNaturalGrpcService(FlitDbContext db, RunPreflightPreviewHandler preview)
    : PersonaNaturalService.PersonaNaturalServiceBase
{
    /// <summary>Razón estable de un método que todavía no tiene implementación.</summary>
    public const string NoImplementado = "NO_IMPLEMENTADO";

    // ── Implementados en esta HU ────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Estado del trámite que nació de una solicitud del portal: se busca por <c>external_ref</c> dentro de la empresa de
    /// Persona Natural. Sin trámite todavía (la cola no lo ha creado) responde <c>encontrado = false</c>, no un error.
    /// </summary>
    public override async Task<ObtenerEstadoTramiteResponse> ObtenerEstadoTramite(ObtenerEstadoTramiteRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        Verificar(context);
        var solicitud = request.SolicitudId?.Trim();
        if (string.IsNullOrEmpty(solicitud))
            throw Invalido("solicitud_id es obligatorio.");

        var tramite = await db.Set<ProcedureInstance>()
            .AsNoTracking()
            .Where(p => p.TenantId == PlatformTenants.PersonaNatural && p.ExternalRef == solicitud && p.DeletedAt == null)
            .Select(p => new { p.Id, p.ReferenceNumber, p.Status, p.SubsanacionActiva })
            .FirstOrDefaultAsync(context.CancellationToken).ConfigureAwait(false);

        return tramite is null
            ? new ObtenerEstadoTramiteResponse { Encontrado = false }
            : new ObtenerEstadoTramiteResponse
            {
                Encontrado = true,
                ProcedureInstanceId = tramite.Id.ToString(),
                ReferenceNumber = tramite.ReferenceNumber,
                Estado = tramite.Status,
                SubsanacionActiva = tramite.SubsanacionActiva,
            };
    }

    /// <summary>
    /// Requisitos del paso 1 sin crear el trámite: la misma consulta de <c>POST /preflight-preview</c> del wizard de FLIT 2
    /// (<see cref="RunPreflightPreviewHandler"/>), a nombre de la empresa de Persona Natural. Devuelve solo etiqueta y estado
    /// de cada chequeo: nunca datos del vehículo ni de personas.
    /// </summary>
    public override async Task<EvaluarRequisitosResponse> EvaluarRequisitos(EvaluarRequisitosRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        Verificar(context);
        var tipo = Texto(request.TipoTramiteCodigo);
        if (tipo is null)
            throw Invalido("tipo_tramite_codigo es obligatorio.");
        Guid? organismo = null;
        if (Texto(request.OrganismoId) is { } organismoTexto)
        {
            if (!Guid.TryParse(organismoTexto, out var parsed))
                throw Invalido("organismo_id no es un identificador válido.");
            organismo = parsed;
        }

        var (resultado, error, _, _) = await preview.HandleAsync(
            new PreflightPreviewRequest(
                PlatformTenants.PersonaNatural,
                Modalidad: string.Empty,
                Vin: Texto(request.Vin),
                Plate: Texto(request.Placa),
                OwnerDocumentType: Texto(request.TipoDocumentoPropietario),
                OwnerDocumentNumber: Texto(request.NumeroDocumentoPropietario),
                TransitOfficeId: organismo,
                ProcedureTypeCode: tipo),
            context.CancellationToken).ConfigureAwait(false);

        if (error is not null)
            return TraducirBloqueo(error);

        var respuesta = new EvaluarRequisitosResponse { PuedeContinuar = resultado!.Overall != "red" };
        respuesta.Requisitos.AddRange(resultado.Checks.Select(c => new Requisito
        {
            Codigo = c.Key,
            Descripcion = c.Label,
            Estado = EstadoRequisito(c.Status),
        }));
        return respuesta;
    }

    // ── Pendientes de su HU: contrato publicado, sin implementación ─────────────────────────────────────────────────

    public override Task<SolicitarValidacionIdentidadResponse> SolicitarValidacionIdentidad(SolicitarValidacionIdentidadRequest request, ServerCallContext context) =>
        Pendiente<SolicitarValidacionIdentidadResponse>(context, "HU C1");

    public override Task<ReenviarValidacionIdentidadResponse> ReenviarValidacionIdentidad(ReenviarValidacionIdentidadRequest request, ServerCallContext context) =>
        Pendiente<ReenviarValidacionIdentidadResponse>(context, "HU C2");

    public override Task<ObtenerEstadoValidacionIdentidadResponse> ObtenerEstadoValidacionIdentidad(ObtenerEstadoValidacionIdentidadRequest request, ServerCallContext context) =>
        Pendiente<ObtenerEstadoValidacionIdentidadResponse>(context, "HU C2");

    public override Task<ListarTiposDisponiblesResponse> ListarTiposDisponibles(ListarTiposDisponiblesRequest request, ServerCallContext context) =>
        Pendiente<ListarTiposDisponiblesResponse>(context, "HU K2");

    public override Task<ObtenerDefinicionTipoTramiteResponse> ObtenerDefinicionTipoTramite(ObtenerDefinicionTipoTramiteRequest request, ServerCallContext context) =>
        Pendiente<ObtenerDefinicionTipoTramiteResponse>(context, "HU K1");

    public override Task<CotizarResponse> Cotizar(CotizarRequest request, ServerCallContext context) =>
        Pendiente<CotizarResponse>(context, "HU B5");

    public override Task<ObtenerConfiguracionPortalResponse> ObtenerConfiguracionPortal(ObtenerConfiguracionPortalRequest request, ServerCallContext context) =>
        Pendiente<ObtenerConfiguracionPortalResponse>(context, "HU J3");

    // ── Soporte ─────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Candado de cliente y empresa con el llamante que dejó el interceptor del SDK.</summary>
    private static void Verificar(ServerCallContext context) =>
        PersonaNaturalServicioAuthorization.VerificarEmpresa(PlatformServiceCaller.From(context));

    private static Task<T> Pendiente<T>(ServerCallContext context, string hu)
    {
        Verificar(context);
        throw PlatformRpcErrors.ToRpcException(NoImplementado, $"Este método llega con la {hu} de la Épica #14021.", StatusCode.Unimplemented);
    }

    private static string? Texto(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();

    private static RpcException Invalido(string detalle) => new(new Status(StatusCode.InvalidArgument, detalle));

    /// <summary>Un chequeo en <c>ok</c>/<c>warn</c> se cumple (el aviso no bloquea); <c>fail</c>/<c>error</c> falta; el resto no aplica.</summary>
    internal static string EstadoRequisito(string status) => status switch
    {
        "ok" or "warn" => "cumple",
        "fail" or "error" => "falta",
        _ => "no_aplica",
    };

    /// <summary>
    /// Los bloqueos del paso 1 (duplicidad, vehículo ya matriculado, sin carrocería) son requisitos que faltan, no errores
    /// de llamada: se devuelven con el mismo texto que ve un gestor en FLIT 2, sin datos del vehículo ni ids internos. Los
    /// de organismo se traducen a <c>organismo_no_habilitado</c>, y lo demás son errores de entrada o de tipo.
    /// </summary>
    internal static EvaluarRequisitosResponse TraducirBloqueo(string error)
    {
        if (error is "procedure_type_not_found" or "modalidad_not_available")
            throw PlatformRpcErrors.ToRpcException("tipo_no_disponible", "El tipo de trámite no está disponible.", StatusCode.NotFound);
        if (error == "identificador_requerido")
            throw Invalido("Indique el identificador del vehículo que pide el tipo de trámite (placa o VIN).");
        if (error == TransitOfficeSelectionPolicy.RequiredErrorCode || error == TransitOfficeSelectionPolicy.UnavailableErrorCode)
            throw PlatformRpcErrors.ToRpcException("organismo_no_habilitado", "El organismo de tránsito no está habilitado para el portal.", StatusCode.FailedPrecondition);

        var descripcion = error switch
        {
            _ when error == InitialProcedureValidationGate.DuplicateActiveProcedure => "Ya existe un trámite en proceso para este vehículo.",
            _ when error == VehicleStatePolicy.ErrorCode => "El vehículo ya se encuentra matriculado: no es válido para este tipo de trámite.",
            _ when error == VehicleStatePolicy.OrganismoRuntNoHabilitadoErrorCode => "El organismo ante el que está el vehículo no está habilitado para el portal.",
            _ when error == VehicleBodyTypePolicy.ErrorCode => "El vehículo no tiene carrocería registrada: no es posible radicar este trámite.",
            _ => null,
        };
        if (descripcion is null)
            throw PlatformRpcErrors.ToRpcException("preflight_no_disponible", "No fue posible evaluar los requisitos.", StatusCode.Internal);

        var respuesta = new EvaluarRequisitosResponse { PuedeContinuar = false };
        respuesta.Requisitos.Add(new Requisito { Codigo = error, Descripcion = descripcion, Estado = "falta" });
        return respuesta;
    }
}
