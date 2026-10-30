using Flit.Admin.Domain.DocumentOrderOverrides;
using Flit.Admin.Domain.Plataforma.PersonaNatural;
using Flit.Api.Authorization;
using Flit.Infrastructure.PersonaNatural;
using Flit.Infrastructure.Persistence;
using Flit.Platform.Sdk;
using Flit.Platform.Sdk.Grpc;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Application.UseCases.ProcedureTypes;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.PersonaNatural;
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
public sealed class PersonaNaturalGrpcService(
    FlitDbContext db, RunPreflightPreviewHandler preview, PersonaNaturalTiposReader tiposReader, PersonaNaturalPortalReader portal,
    GetProcedureStepsHandler pasosHandler, IResolvedDocumentMatrixResolver matriz,
    PersonaNaturalConfiguracionPublicaReader configuracionPublica, Flit.Admin.Application.Banners.Ports.IBannerImageStorage imagenesBanner,
    IPersonaNaturalConfigRepository configuracionPn)
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
    /// <summary>
    /// Organismos ofrecidos a Persona Natural: los que tienen grant habilitado para su empresa. Sin ninguno devuelve lista
    /// vacía, no un error. El enlace de tarifas del organismo no existe todavía como dato (HU J): viaja vacío.
    /// </summary>
    public override async Task<ListarOrganismosResponse> ListarOrganismos(ListarOrganismosRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        Verificar(context);
        var organismos = await portal.ListarOrganismosAsync(context.CancellationToken).ConfigureAwait(false);
        var respuesta = new ListarOrganismosResponse();
        respuesta.Organismos.AddRange(organismos.Select(o => new OrganismoPortal
        {
            Id = o.Id.ToString(),
            Codigo = o.Codigo,
            Nombre = o.Nombre,
            Municipio = o.Municipio,
            Departamento = o.Departamento,
            EnlaceTarifas = string.Empty,
        }));
        return respuesta;
    }

    /// <summary>
    /// HU #14216 — código público del organismo para el canal del punto. No crea trámites ni toca el grant del portal.
    /// </summary>
    public override async Task<ResolverCanalPuntoResponse> ResolverCanalPunto(ResolverCanalPuntoRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        Verificar(context);
        var codigo = Texto(request.Codigo);
        if (codigo is null)
            throw Invalido("codigo es obligatorio.");

        var r = await configuracionPn.ResolverCanalPuntoAsync(codigo, context.CancellationToken).ConfigureAwait(false);
        return new ResolverCanalPuntoResponse
        {
            Estado = r.Estado,
            OrganismoId = r.OrganismoId == Guid.Empty ? string.Empty : r.OrganismoId.ToString(),
            Codigo = r.Codigo,
            Nombre = r.Nombre,
        };
    }

    /// <summary>
    /// Tipos del portal con su elegibilidad (<see cref="ElegibilidadPersonaNatural"/>, del perfil del tipo). Sin organismo,
    /// todos con su motivo («Próximamente»). Con organismo, solo los que se ofrecen ahí: elegibles, habilitados por el
    /// superadministrador (lista blanca, HU #14072) y con tarifa vigente.
    /// </summary>
    public override async Task<ListarTiposDisponiblesResponse> ListarTiposDisponibles(ListarTiposDisponiblesRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        Verificar(context);
        Guid? organismo = null;
        if (Texto(request.OrganismoId) is { } organismoTexto)
        {
            if (!Guid.TryParse(organismoTexto, out var parsed))
                throw Invalido("organismo_id no es un identificador válido.");
            if (!await portal.OrganismoHabilitadoAsync(parsed, context.CancellationToken).ConfigureAwait(false))
                throw PlatformRpcErrors.ToRpcException("organismo_no_habilitado", "El organismo de tránsito no está habilitado para el portal.", StatusCode.FailedPrecondition);
            organismo = parsed;
        }

        // «Habilitado = elegible + habilitado por el superadministrador (HU #14072) + tarifa vigente».
        var tipos = PersonaNaturalPortalReader.AplicarListaBlanca(
            await tiposReader.ListarAsync(context.CancellationToken).ConfigureAwait(false),
            await portal.TiposHabilitadosAsync(context.CancellationToken).ConfigureAwait(false));
        var respuesta = new ListarTiposDisponiblesResponse();
        if (organismo is { } org)
        {
            var tarifas = await portal.TarifasVigentesAsync(portal.HoyColombia(), context.CancellationToken).ConfigureAwait(false);
            tipos = tipos.Where(t => t.Elegibilidad.Elegible && TieneTarifaBase(t.Id, org, tarifas)).ToList();
        }

        respuesta.Tipos.AddRange(tipos.Select(t => new TipoTramiteResumen
        {
            Codigo = t.Codigo,
            Nombre = t.Nombre,
            Familia = t.Familia,
            Version = t.Version,
            DisponiblePn = t.Elegibilidad.Elegible,
            MotivoNoDisponible = t.Elegibilidad.Motivo,
        }));
        return respuesta;
    }

    /// <summary>
    /// Cotización de 2 líneas (Servicio de tránsito y Servicio FLIT) más los adicionales que el tipo permite. ES el único
    /// cálculo: la landing y el paso de pago llaman a este mismo RPC, así que el total no puede diferir
    /// (<see cref="CotizadorPersonaNatural"/>). Solo lee tarifas: nunca inventa un valor. Errores estables:
    /// <c>tipo_no_disponible</c>, <c>organismo_no_habilitado</c>, <c>tipo_no_habilitado</c> y <c>tarifa_no_vigente</c>.
    /// </summary>
    public override async Task<CotizarResponse> Cotizar(CotizarRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        Verificar(context);
        var codigo = Texto(request.TipoTramiteCodigo) ?? throw Invalido("tipo_tramite_codigo es obligatorio.");
        if (!Guid.TryParse(Texto(request.OrganismoId), out var organismo))
            throw Invalido("organismo_id es obligatorio y debe ser un identificador válido.");
        if (request.Adicionales.Count > ConceptosTarifa.Adicionales.Count)
            throw Invalido("Demasiados conceptos adicionales.");

        var ct = context.CancellationToken;
        var tipos = await tiposReader.ListarAsync(ct, codigo).ConfigureAwait(false);
        if (tipos.Count == 0)
            throw PlatformRpcErrors.ToRpcException("tipo_no_disponible", "El tipo de trámite no está disponible.", StatusCode.NotFound);
        // La lista blanca del superadministrador (HU #14072) manda sobre la elegibilidad calculada.
        var tipo = PersonaNaturalPortalReader.AplicarListaBlanca(tipos, await portal.TiposHabilitadosAsync(ct).ConfigureAwait(false))[0];
        if (!await portal.OrganismoHabilitadoAsync(organismo, ct).ConfigureAwait(false))
            throw PlatformRpcErrors.ToRpcException("organismo_no_habilitado", "El organismo de tránsito no está habilitado para el portal.", StatusCode.FailedPrecondition);
        if (!tipo.Elegibilidad.Elegible)
            throw PlatformRpcErrors.ToRpcException("tipo_no_habilitado", "El tipo de trámite no está habilitado para Persona Natural en este organismo.", StatusCode.FailedPrecondition);

        var tarifas = await portal.TarifasVigentesAsync(portal.HoyColombia(), ct).ConfigureAwait(false);
        var resultado = CotizadorPersonaNatural.Calcular(
            tipo.Id, organismo, tipo.Perfil.ComplementaryTransformationsAllowed(tipo.Familia), request.Adicionales, tarifas);
        if (resultado.Cotizacion is not { } cotizacion)
            throw PlatformRpcErrors.ToRpcException(CotizadorPersonaNatural.TarifaNoVigente, "No hay tarifa vigente para este organismo y tipo de trámite.", StatusCode.FailedPrecondition);

        var respuesta = new CotizarResponse { TotalCop = cotizacion.TotalCop, VersionTarifa = cotizacion.VersionTarifa };
        respuesta.Lineas.AddRange(cotizacion.Lineas.Select(l => new LineaCobro { Codigo = l.Codigo, Nombre = l.Nombre, Subtitulo = l.Subtitulo, ValorCop = l.ValorCop }));
        respuesta.AdicionalesNoAplicados.AddRange(cotizacion.AdicionalesNoAplicados);
        return respuesta;
    }

    /// <summary>
    /// HU #14078 — estructura del wizard de un tipo (pasos, campos, actores, documentos y capacidades). Compone lo que FLIT 2
    /// ya resuelve y no evalúa reglas (<see cref="PersonaNaturalDefinicionMapper"/>). Errores estables, sin datos parciales:
    /// <c>tipo_no_disponible</c> (no existe, no está publicado o su wizard está apagado) y <c>organismo_no_habilitado</c>.
    /// Un tipo con wizard encendido pero no elegible para el portal SÍ se devuelve, con <c>disponible_pn = false</c> y su
    /// motivo: así el BFF puede mostrarlo como «Próximamente».
    /// </summary>
    public override async Task<ObtenerDefinicionTipoTramiteResponse> ObtenerDefinicionTipoTramite(ObtenerDefinicionTipoTramiteRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        Verificar(context);
        var codigo = Texto(request.Codigo) ?? throw Invalido("codigo es obligatorio.");
        Guid? organismo = null;
        if (Texto(request.OrganismoId) is { } organismoTexto)
        {
            if (!Guid.TryParse(organismoTexto, out var parsed))
                throw Invalido("organismo_id no es un identificador válido.");
            organismo = parsed;
        }

        var ct = context.CancellationToken;
        var tipos = await tiposReader.ListarAsync(ct, codigo).ConfigureAwait(false);
        var tipo = tipos.FirstOrDefault(t => string.Equals(t.Codigo, codigo, StringComparison.OrdinalIgnoreCase));
        if (tipo is null || !tipo.WizardHabilitado)
            throw PlatformRpcErrors.ToRpcException("tipo_no_disponible", "El tipo de trámite no está disponible.", StatusCode.NotFound);
        if (organismo is { } org && !await portal.OrganismoHabilitadoAsync(org, ct).ConfigureAwait(false))
            throw PlatformRpcErrors.ToRpcException("organismo_no_habilitado", "El organismo de tránsito no está habilitado para el portal.", StatusCode.FailedPrecondition);

        var (pasos, error) = await pasosHandler.HandleAsync(tipo.Id, ct).ConfigureAwait(false);
        if (pasos is null || error is not null)
            throw PlatformRpcErrors.ToRpcException("tipo_no_disponible", "El tipo de trámite no está disponible.", StatusCode.NotFound);
        var documentos = await matriz.ResolveAsync(tipo.Id, organismo, ct).ConfigureAwait(false);

        return new ObtenerDefinicionTipoTramiteResponse { Definicion = PersonaNaturalDefinicionMapper.Armar(tipo, pasos, documentos) };
    }

    /// <summary>Un tipo se ofrece en un organismo solo si los dos renglones base tienen tarifa vigente.</summary>
    private static bool TieneTarifaBase(Guid tipoId, Guid organismoId, IReadOnlyCollection<TarifaVigente> tarifas) =>
        CotizadorPersonaNatural.Resolver(ConceptosTarifa.DerechosTransito, tipoId, organismoId, tarifas) is not null
        && CotizadorPersonaNatural.Resolver(ConceptosTarifa.ServicioFlit, tipoId, organismoId, tarifas) is not null;


    // ── Pendientes de su HU: contrato publicado, sin implementación ─────────────────────────────────────────────────

    // ── Identidad de las partes (HU C1 y C2): el caso de uso vive en PersonaNaturalIdentidadService ─────────────────

    /// <summary>
    /// Pide una validación NUEVA para la parte de esa solicitud (HU #14044). Idempotente por (solicitud, parte, documento);
    /// nunca reutiliza una validación de otra solicitud (decisión del PO). Persona jurídica: <c>persona_juridica_no_soportada</c>.
    /// </summary>
    public override Task<SolicitarValidacionIdentidadResponse> SolicitarValidacionIdentidad(SolicitarValidacionIdentidadRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        Verificar(context);
        return Identidad(context).SolicitarAsync(request, context.CancellationToken);
    }

    /// <summary>Reenvía el enlace de la MISMA validación (HU #14045), opcionalmente a un correo corregido.</summary>
    public override Task<ReenviarValidacionIdentidadResponse> ReenviarValidacionIdentidad(ReenviarValidacionIdentidadRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        Verificar(context);
        return Identidad(context).ReenviarAsync(request, context.CancellationToken);
    }

    /// <summary>Estado de una validación del portal (HU #14045): pendiente | aprobada | rechazada | vencida.</summary>
    public override Task<ObtenerEstadoValidacionIdentidadResponse> ObtenerEstadoValidacionIdentidad(ObtenerEstadoValidacionIdentidadRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        Verificar(context);
        return Identidad(context).ObtenerEstadoAsync(request, context.CancellationToken);
    }

    private static PersonaNaturalIdentidadService Identidad(ServerCallContext context) =>
        ActivatorUtilities.CreateInstance<PersonaNaturalIdentidadService>(context.GetHttpContext().RequestServices);

    /// <summary>
    /// HU #14073 — contenido PUBLICADO y vigente del portal y parámetros públicos. Solo lee la última versión publicada
    /// (nunca el borrador) y el contacto de soporte; NO devuelve datos bancarios ni parámetros de protección. Sin contenido
    /// publicado responde versión «0» y listas vacías: el portal conserva su contenido actual. Los textos son texto plano.
    /// </summary>
    public override async Task<ObtenerConfiguracionPortalResponse> ObtenerConfiguracionPortal(ObtenerConfiguracionPortalRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        Verificar(context);
        var c = await configuracionPublica.LeerAsync(context.CancellationToken).ConfigureAwait(false);
        var respuesta = new ObtenerConfiguracionPortalResponse
        {
            Version = c.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Soporte = new ContactoSoportePortal { Correo = c.SoporteCorreo ?? string.Empty, Telefono = c.SoporteTelefono ?? string.Empty, Horario = c.SoporteHorario ?? string.Empty },
            Parametros = new ParametrosPublicosPortal { BorradorVigenciaDias = c.BorradorVigenciaDias ?? 0 },
        };
        if (c.PublicadoEn is { } publicado)
            respuesta.PublicadoEn = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(publicado);
        respuesta.Preguntas.AddRange(c.Preguntas.Select(p => new PreguntaFrecuentePortal { Id = p.Id.ToString(), Pregunta = p.Pregunta, Respuesta = p.Respuesta, Orden = p.Orden }));
        respuesta.Banners.AddRange(c.Banners.Select(b => new BannerPortal
        {
            Id = b.Id.ToString(),
            Titulo = b.Titulo,
            TextoAlternativo = b.TextoAlternativo,
            EnlaceUrl = b.EnlaceUrl ?? string.Empty,
            Orden = b.Orden,
            ImagenSha256 = b.ImagenSha256,
            ImagenContentType = b.ImagenContentType,
        }));
        respuesta.Enlaces.AddRange(c.Enlaces.Select(e => new EnlacePortal { Codigo = e.Codigo, Url = e.Url ?? string.Empty, ConDocumento = !string.IsNullOrEmpty(e.Url) }));
        return respuesta;
    }

    /// <summary>
    /// HU #14073 — bytes de la imagen de un banner publicado y vigente, con su sha256 como ETag: si el cliente ya lo tiene
    /// responde <c>no_modificado</c> sin abrir el archivo. Un banner que no está publicado, venció o perdió su archivo responde
    /// <c>encontrado = false</c> (sin distinguir el motivo).
    /// </summary>
    public override async Task<ObtenerImagenBannerPortalResponse> ObtenerImagenBannerPortal(ObtenerImagenBannerPortalRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        Verificar(context);
        if (!Guid.TryParse(Texto(request.BannerId), out var id))
            throw Invalido("banner_id es obligatorio y debe ser un identificador válido.");
        var ct = context.CancellationToken;
        if (await configuracionPublica.BannerVigenteAsync(id, ct).ConfigureAwait(false) is not { } banner)
            return new ObtenerImagenBannerPortalResponse { Encontrado = false };
        if (string.Equals(Texto(request.IfNoneMatch), banner.ImagenSha256, StringComparison.OrdinalIgnoreCase))
            return new ObtenerImagenBannerPortalResponse { Encontrado = true, NoModificado = true, Sha256 = banner.ImagenSha256, ContentType = banner.ImagenContentType };

        var stream = await imagenesBanner.OpenReadAsync(banner.ImagenPath, ct).ConfigureAwait(false);
        if (stream is null)
            return new ObtenerImagenBannerPortalResponse { Encontrado = false };
        await using (stream.ConfigureAwait(false))
        {
            return new ObtenerImagenBannerPortalResponse
            {
                Encontrado = true,
                Sha256 = banner.ImagenSha256,
                ContentType = banner.ImagenContentType,
                Contenido = await Google.Protobuf.ByteString.FromStreamAsync(stream, ct).ConfigureAwait(false),
            };
        }
    }

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

    /// <summary>
    /// <c>ok</c> se cumple; <c>warn</c>/<c>fail</c>/<c>error</c> se muestran como pendientes (POR REVISAR)
    /// — p. ej. multas SIMIT en amarillo como en FLIT 2. El resto no aplica. El bloqueo de continuar
    /// sigue el overall del preflight (<c>PuedeContinuar</c>), no este mapeo por sí solo.
    /// </summary>
    internal static string EstadoRequisito(string status) => status switch
    {
        "ok" => "cumple",
        "warn" or "fail" or "error" => "falta",
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
