using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using System.Text.Json.Serialization;
using Flit.PersonaNatural.Api.Abuso;
using Flit.PersonaNatural.Api.Consultas;
using Flit.PersonaNatural.Api.Cotizaciones;
using Flit.PersonaNatural.Api.Definiciones;
using Flit.PersonaNatural.Api.Identidad;
using Flit.PersonaNatural.Api.Sesiones;
using Flit.PersonaNatural.Api.Solicitudes;
using Flit.Tramites.Grpc.V1;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Flit.PersonaNatural.Api.Punto;

/// <summary>
/// Canal del punto de atención (HU #14216, #14219, #14221, #14231, #14236 y #14242). La cookie fija el organismo: no hay forma
/// de cambiarlo en la petición. Las consultas previas a Actores no crean trámite en FLIT 2; <c>POST /actores/abrir</c>
/// materializa un borrador PN (sin radicar ni procedure instance). <c>GET /requisitos</c> proyecta la matriz documental
/// vigente de FLIT 2.0 sin crear procedure instance.
/// </summary>
public static class PuntoEndpoints
{
    public const string Cookie = CookiePunto.Nombre;
    public const string CodigoDesconocido = "codigo_desconocido";
    public const string OrganismoNoOperable = "organismo_no_operable";
    public const string CanalApagado = "canal_apagado";
    public const string SinTramitesDisponibles = "sin_tramites_disponibles";
    public const string CodigoProveedor = ConsultasEndpoints.CodigoProveedor;

    private const string ItemSesion = "flit.pn.punto";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static IServiceCollection AddPuntoPortal(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddDataProtection();
        services.TryAddSingleton<CookiePunto>();
        services.TryAddScoped<ArranquePuntoService>();
        services.TryAddScoped<ConsultasPuntoService>();
        services.TryAddScoped<DiagnosticoPuntoService>();
        services.TryAddScoped<IAbrirBorradorDesdePunto, AbrirBorradorDesdePunto>();
        services.TryAddScoped<ActoresPuntoService>();
        services.TryAddScoped<IdentidadPuntoService>();
        services.TryAddScoped<RequisitosPuntoService>();
        services.TryAddScoped<IBorradorPunto, BorradorPuntoDb>();
        services.TryAddScoped<CompletitudRequisitosPuntoService>();
        services.TryAddScoped<ICanalPunto>(sp =>
        {
            var cliente = sp.GetService<PersonaNaturalService.PersonaNaturalServiceClient>();
            return cliente is null
                ? new CanalPuntoNoConfigurado()
                : ActivatorUtilities.CreateInstance<CanalPuntoGrpc>(sp, cliente);
        });
        return services;
    }

    public static IEndpointRouteBuilder MapPuntoEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var grupo = app.MapGroup(SesionEndpoints.Base + "/punto").AllowAnonymous().RequireRateLimiting(AbusoEndpoints.PoliticaPorIp);

        grupo.MapPost("/entrada", async (EntradaPuntoRequest? body, HttpContext http, ICanalPunto canal, CookiePunto cookie, IFachadaTramites? catalogo, CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            var codigo = (body?.Codigo ?? string.Empty).Trim();
            if (codigo.Length == 0 || codigo.Length > 20)
                return Fallo(http, cookie, CodigoDesconocido, "No encontramos un organismo con ese código.");

            ResolucionCanalPunto resolucion;
            try
            {
                resolucion = await canal.ResolverAsync(codigo, ct).ConfigureAwait(false);
            }
            catch (FachadaException ex)
            {
                cookie.Borrar(http);
                return Results.Json(new { code = ex.Codigo, message = ex.Message }, Json, statusCode: ex.EstadoHttp);
            }

            if (!string.Equals(resolucion.Estado, "ok", StringComparison.OrdinalIgnoreCase) || resolucion.OrganismoId == Guid.Empty)
                return Fallo(http, cookie, CodigoDe(resolucion.Estado), MensajeDe(resolucion.Estado));

            // HU #14391 AC3 — un punto sin ningún tipo con precio no tiene nada que ofrecer: se informa y no se abre la sesión. Es la
            // MISMA lista que muestra el selector (/tipos-tramite). Si la consulta falla no se bloquea la entrada: el selector
            // reportará el error por su cuenta.
            if (catalogo is not null && await SinTramitesAsync(catalogo, resolucion.OrganismoId, ct).ConfigureAwait(false))
                return Fallo(http, cookie, SinTramitesDisponibles, "En este momento este punto no tiene trámites disponibles.");

            var organismo = new OrganismoPunto(resolucion.OrganismoId, resolucion.Codigo, resolucion.Nombre);
            cookie.Fijar(http, organismo);
            return Results.Json(new { organismoId = organismo.OrganismoId, codigo = organismo.Codigo, nombre = organismo.Nombre }, Json);
        });

        // HU 13 — tipos que el selector del punto ofrece: la MISMA lista del portal web (GET /tipos-tramite?organismoId=), calculada con
        // el organismo de la cookie: tipo elegible, habilitado por el superadministrador y con tarifa vigente en ese organismo.
        grupo.MapGet("/tipos-tramite", async (HttpContext http, IFachadaTramites catalogo, CancellationToken ct) =>
        {
            var sesion = Sesion(http);
            http.Response.Headers.CacheControl = "no-store";
            try
            {
                return Results.Json(await catalogo.ListarTiposAsync(sesion.OrganismoId.ToString(), ct).ConfigureAwait(false), Json);
            }
            catch (FachadaException ex)
            {
                return SesionEndpoints.Problema(ex.EstadoHttp, "No disponible", ex.Message, ex.Codigo);
            }
        }).AddEndpointFilter(ConSesionDePunto);

        grupo.MapGet("/sesion", (HttpContext http, CookiePunto cookie) =>
        {
            var organismo = cookie.Leer(http);
            return organismo is null
                ? SinSesion()
                : Results.Json(new { organismoId = organismo.OrganismoId, codigo = organismo.Codigo, nombre = organismo.Nombre }, Json);
        });

        grupo.MapPost("/arranque", async (ArranquePuntoRequest? body, HttpContext http, ArranquePuntoService arranque, CancellationToken ct) =>
        {
            var sesion = Sesion(http);
            var resultado = await arranque.EvaluarAsync(sesion, body?.Placa, body?.Documento, body?.TipoCodigo, ct).ConfigureAwait(false);
            return resultado switch
            {
                ResultadoArranque.Seguir => Results.Json(new { puedeSeguir = true }, Json),
                ResultadoArranque.Detener detener => Results.Json(new { puedeSeguir = false, message = detener.Mensaje }, Json),
                ResultadoArranque.ProveedorCaido => Results.Json(
                    new { puedeSeguir = false, code = CodigoProveedor, message = TiposDelPunto.RuntCaido }, Json, statusCode: StatusCodes.Status503ServiceUnavailable),
                ResultadoArranque.DatosInvalidos datos => SesionEndpoints.Problema(
                    StatusCodes.Status400BadRequest, "Datos inválidos", datos.Mensaje, ConsultasEndpoints.CodigoDatosInvalidos),
                _ => SesionEndpoints.Problema(StatusCodes.Status400BadRequest, "Datos inválidos", "Indique la placa, el documento y el tipo de trámite.", ConsultasEndpoints.CodigoDatosInvalidos),
            };
        }).AddEndpointFilter(ConSesionDePunto);

        grupo.MapPost("/diagnostico", async (DiagnosticoPuntoRequest? body, HttpContext http, DiagnosticoPuntoService diagnostico, CancellationToken ct) =>
        {
            var sesion = Sesion(http);
            var resultado = await diagnostico.EvaluarAsync(
                sesion, body?.Placa, body?.Documento, body?.TipoCodigo, body?.TipoDocumento, ct).ConfigureAwait(false);
            return resultado switch
            {
                ResultadoDiagnosticoPunto.Ok ok => Results.Json(ok.Diagnostico, Json),
                ResultadoDiagnosticoPunto.FachadaCaida caida => Results.Json(
                    new { code = CodigoProveedor, message = caida.Mensaje }, Json, statusCode: StatusCodes.Status503ServiceUnavailable),
                ResultadoDiagnosticoPunto.DatosInvalidos datos => SesionEndpoints.Problema(
                    StatusCodes.Status400BadRequest, "Datos inválidos", datos.Mensaje, ConsultasEndpoints.CodigoDatosInvalidos),
                _ => SesionEndpoints.Problema(
                    StatusCodes.Status400BadRequest, "Datos inválidos", "Indique la placa, el documento y el tipo de trámite.", ConsultasEndpoints.CodigoDatosInvalidos),
            };
        }).AddEndpointFilter(ConSesionDePunto);

        grupo.MapPost("/actores/abrir", async (AbrirActoresPuntoRequest? body, HttpContext http, ActoresPuntoService actores, PiiHasher hasher, CancellationToken ct) =>
        {
            var sesion = Sesion(http);
            try
            {
                var resultado = await actores.AbrirAsync(
                    sesion,
                    body?.Placa,
                    body?.Documento,
                    body?.TipoCodigo,
                    body?.TipoDocumento,
                    hasher.HashIp(http.Connection.RemoteIpAddress?.ToString()),
                    ct).ConfigureAwait(false);
                return resultado switch
                {
                    ResultadoAbrirActores.Ok ok => Results.Json(new
                    {
                        solicitudId = ok.Sesion.SolicitudId,
                        codigoPublico = ok.Sesion.CodigoPublico,
                        token = ok.Sesion.Token,
                        expiraEn = ok.Sesion.ExpiraEn,
                        estado = ok.Sesion.Estado,
                        tipoTramiteCodigo = ok.Sesion.TipoTramiteCodigo,
                        definicionVersion = ok.Sesion.DefinicionVersion,
                        organismoId = sesion.OrganismoId,
                        puedeSerRadicante = ok.PuedeSerRadicante,
                        message = ok.Message,
                        roles = ok.Roles.Select(r => new
                        {
                            rol = r.Rol,
                            etiqueta = r.Etiqueta,
                            esRadicante = r.EsRadicante,
                            minimo = r.Minimo,
                            maximo = r.Maximo,
                            personaNaturalPermitida = r.PersonaNaturalPermitida,
                        }),
                    }, Json),
                    ResultadoAbrirActores.ProveedorCaido => Results.Json(
                        new { code = CodigoProveedor, message = TiposDelPunto.RuntCaido }, Json, statusCode: StatusCodes.Status503ServiceUnavailable),
                    ResultadoAbrirActores.DatosInvalidos datos => SesionEndpoints.Problema(
                        StatusCodes.Status400BadRequest, "Datos inválidos", datos.Mensaje, ConsultasEndpoints.CodigoDatosInvalidos),
                    _ => SesionEndpoints.Problema(
                        StatusCodes.Status400BadRequest, "Datos inválidos", "Indique la placa, el documento y el tipo de trámite.", ConsultasEndpoints.CodigoDatosInvalidos),
                };
            }
            catch (ReglaDeNegocioException ex)
            {
                var extensiones = new Dictionary<string, object?> { ["code"] = ex.Codigo };
                if (ex.Extensiones is not null)
                    foreach (var (k, v) in ex.Extensiones)
                        extensiones[k] = v;
                return Results.Problem(statusCode: ex.Estado, title: ex.Titulo, detail: ex.Message, extensions: extensiones);
            }
            catch (FachadaException ex)
            {
                return SesionEndpoints.Problema(ex.EstadoHttp, "No disponible", ex.Message, ex.Codigo);
            }
        }).AddEndpointFilter(ConSesionDePunto);

        // Identidad se resuelve desde RequestServices para que el host de PuntoCanalTests (sin Db) pueda mapear el grupo.
        grupo.MapPost("/actores/identidad/radicante", async (IdentidadRadicantePuntoRequest? body, HttpContext http, CancellationToken ct) =>
        {
            var identidadPunto = http.RequestServices.GetRequiredService<IdentidadPuntoService>();
            var (solicitudId, errorSesion) = await identidadPunto.ResolverSolicitudAsync(TokenSolicitud(http), body?.SolicitudId, ct).ConfigureAwait(false);
            if (errorSesion is not null)
                return ResponderIdentidad(http, errorSesion);
            return ResponderIdentidad(http, await identidadPunto.RadicanteInSituAsync(
                solicitudId,
                body?.Parte,
                body?.Ordinal ?? 1,
                body?.TipoDocumento,
                body?.Documento,
                body?.Nombre,
                body?.Correo,
                body?.Celular,
                body?.Direccion,
                body?.Ciudad,
                ct).ConfigureAwait(false));
        }).AddEndpointFilter(ConSesionDePunto);

        grupo.MapGet("/actores/identidad", async (Guid? solicitudId, HttpContext http, CancellationToken ct) =>
        {
            var identidadPunto = http.RequestServices.GetRequiredService<IdentidadPuntoService>();
            var (id, errorSesion) = await identidadPunto.ResolverSolicitudAsync(TokenSolicitud(http), solicitudId, ct).ConfigureAwait(false);
            if (errorSesion is not null)
                return ResponderIdentidad(http, errorSesion);
            var estado = await identidadPunto.EstadoAsync(id, ct).ConfigureAwait(false);
            return estado is null
                ? SesionEndpoints.Problema(StatusCodes.Status404NotFound, "No encontrado", SesionEndpoints.MensajeNoEncontrada, SesionEndpoints.CodigoNoEncontrada)
                : Results.Json(estado, Json);
        }).AddEndpointFilter(ConSesionDePunto);

        grupo.MapPost("/actores/invitar", async (InvitarPartePuntoRequest? body, HttpContext http, CancellationToken ct) =>
        {
            if (body is null)
                return SesionEndpoints.Problema(StatusCodes.Status400BadRequest, "Datos inválidos", "Indique la parte a invitar.", ConsultasEndpoints.CodigoDatosInvalidos);
            var identidadPunto = http.RequestServices.GetRequiredService<IdentidadPuntoService>();
            var (solicitudId, errorSesion) = await identidadPunto.ResolverSolicitudAsync(TokenSolicitud(http), null, ct).ConfigureAwait(false);
            if (errorSesion is not null)
                return ResponderIdentidad(http, errorSesion);
            return ResponderIdentidad(http, await identidadPunto.InvitarAsync(solicitudId, body, ct).ConfigureAwait(false));
        }).AddEndpointFilter(ConSesionDePunto);

        grupo.MapGet("/requisitos", async (string? tipoCodigo, HttpContext http, RequisitosPuntoService requisitos, CancellationToken ct) =>
        {
            var sesion = Sesion(http);
            try
            {
                var resultado = await requisitos.ObtenerAsync(sesion, tipoCodigo, ct).ConfigureAwait(false);
                return resultado switch
                {
                    ResultadoMatrizRequisitosPunto.Ok ok => Results.Json(ok.Matriz, Json),
                    ResultadoMatrizRequisitosPunto.DatosInvalidos datos => SesionEndpoints.Problema(
                        StatusCodes.Status400BadRequest, "Datos inválidos", datos.Mensaje, ConsultasEndpoints.CodigoDatosInvalidos),
                    _ => SesionEndpoints.Problema(
                        StatusCodes.Status400BadRequest, "Datos inválidos", "Indique el tipo de trámite.", ConsultasEndpoints.CodigoDatosInvalidos),
                };
            }
            catch (FachadaException ex)
            {
                return SesionEndpoints.Problema(ex.EstadoHttp, "No disponible", ex.Message, ex.Codigo);
            }
        }).AddEndpointFilter(ConSesionDePunto);

        grupo.MapGet("/requisitos/valor-sugerido", async (string? placa, HttpContext http, CompletitudRequisitosPuntoService completitud, CancellationToken ct) =>
        {
            _ = Sesion(http);
            var sugerido = await completitud.SugerirValorVentaAsync(placa, ct).ConfigureAwait(false);
            return Results.Json(sugerido, Json);
        }).AddEndpointFilter(ConSesionDePunto);

        grupo.MapPost("/requisitos/validar-archivo", (ValidarArchivoPuntoRequest? body, HttpContext http, CompletitudRequisitosPuntoService completitud) =>
        {
            _ = Sesion(http);
            var r = completitud.ValidarArchivo(body?.NombreArchivo, body?.TamanoBytes ?? 0);
            return r switch
            {
                ResultadoValidacionArchivoPunto.Ok => Results.Json(new { valido = true }, Json),
                ResultadoValidacionArchivoPunto.Rechazo rechazo => Results.Json(
                    new { valido = false, code = rechazo.Codigo, message = rechazo.Mensaje }, Json, statusCode: StatusCodes.Status400BadRequest),
                _ => Results.Json(new { valido = false, code = "archivo_invalido", message = "Archivo no válido." }, Json, statusCode: StatusCodes.Status400BadRequest),
            };
        }).AddEndpointFilter(ConSesionDePunto);

        grupo.MapPost("/requisitos/completitud", async (CompletitudRequisitosPuntoRequest? body, HttpContext http, CompletitudRequisitosPuntoService completitud, CancellationToken ct) =>
        {
            var sesion = Sesion(http);
            try
            {
                var resultado = await completitud.EvaluarAsync(sesion, TokenSolicitud(http), body?.TipoCodigo, ct).ConfigureAwait(false);
                return resultado switch
                {
                    ResultadoCompletitudPunto.Ok ok => Results.Json(ok.Completitud, Json),
                    ResultadoCompletitudPunto.Rechazo r => SesionEndpoints.Problema(r.Http, "No se puede evaluar", r.Mensaje, r.Codigo),
                    ResultadoCompletitudPunto.DatosInvalidos datos => SesionEndpoints.Problema(
                        StatusCodes.Status400BadRequest, "Datos inválidos", datos.Mensaje, ConsultasEndpoints.CodigoDatosInvalidos),
                    _ => SesionEndpoints.Problema(
                        StatusCodes.Status400BadRequest, "Datos inválidos", "Indique el tipo de trámite.", ConsultasEndpoints.CodigoDatosInvalidos),
                };
            }
            catch (FachadaException ex)
            {
                return SesionEndpoints.Problema(ex.EstadoHttp, "No disponible", ex.Message, ex.Codigo);
            }
        }).AddEndpointFilter(ConSesionDePunto);

        grupo.MapPost("/consultas/vehiculo", async (ConsultaVehiculoRequest? body, ConsultasPuntoService consultas, CancellationToken ct) =>
        {
            if (body is null || !body.Consentimiento)
                return ConsultasEndpointsPublico.SinConsentimiento();
            var entrada = EntradaVehiculo(body);
            if (entrada is null)
                return ConsultasEndpointsPublico.DatosInvalidos("Indique la placa y el documento del propietario, o el VIN del vehículo.");
            try
            {
                return Results.Json(await consultas.VehiculoAsync(entrada.Value.Placa, entrada.Value.Vin, entrada.Value.Propietario, ct).ConfigureAwait(false), Json);
            }
            catch (ProveedorNoDisponibleException)
            {
                return Proveedor(TiposDelPunto.RuntCaido);
            }
        }).AddEndpointFilter(ConSesionDePunto);

        grupo.MapPost("/consultas/persona", async (ConsultaPersonaRequest? body, ConsultasPuntoService consultas, CancellationToken ct) =>
        {
            if (body is null || !body.Consentimiento)
                return ConsultasEndpointsPublico.SinConsentimiento();
            var documento = EntradaConsulta.Documento(body.Documento?.Tipo, body.Documento?.Numero);
            if (documento is null)
                return ConsultasEndpointsPublico.DatosInvalidos("Indique el tipo y el número del documento.");
            try
            {
                return Results.Json(await consultas.PersonaAsync(documento, ct).ConfigureAwait(false), Json);
            }
            catch (ProveedorNoDisponibleException)
            {
                return Proveedor(TiposDelPunto.RuntCaido);
            }
        }).AddEndpointFilter(ConSesionDePunto);

        grupo.MapPost("/consultas/multas", async (ConsultaMultasPuntoRequest? body, ConsultasPuntoService consultas, CancellationToken ct) =>
        {
            if (body is null || !body.Consentimiento)
                return ConsultasEndpointsPublico.SinConsentimiento();
            var placa = string.IsNullOrWhiteSpace(body.Placa) ? null : EntradaConsulta.Placa(body.Placa);
            var documento = body.Documento is null ? null : EntradaConsulta.Documento(body.Documento.Tipo, body.Documento.Numero);
            if (placa is null && documento is null)
                return ConsultasEndpointsPublico.DatosInvalidos("Indique la placa o el documento.");
            try
            {
                return Results.Json(await consultas.MultasAsync(placa, documento, ct).ConfigureAwait(false), Json);
            }
            catch (ProveedorNoDisponibleException)
            {
                return Proveedor(TiposDelPunto.MultasCaidas);
            }
        }).AddEndpointFilter(ConSesionDePunto);

        return app;
    }

    private static async ValueTask<object?> ConSesionDePunto(EndpointFilterInvocationContext contexto, EndpointFilterDelegate siguiente)
    {
        var http = contexto.HttpContext;
        var organismo = http.RequestServices.GetRequiredService<CookiePunto>().Leer(http);
        if (organismo is null)
            return SinSesion();
        http.Items[ItemSesion] = organismo;
        return await siguiente(contexto).ConfigureAwait(false);
    }

    private static OrganismoPunto Sesion(HttpContext http) =>
        http.Items[ItemSesion] as OrganismoPunto ?? throw new InvalidOperationException("La sesión del punto no está en la petición.");

    private static string? TokenSolicitud(HttpContext http)
    {
        var cabecera = http.Request.Headers.Authorization.ToString();
        return cabecera.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? cabecera["Bearer ".Length..] : null;
    }

    private static IResult ResponderIdentidad(HttpContext http, ResultadoIdentidad r)
    {
        if (r.Exito)
        {
            var cuerpo = new { parte = r.Parte, ordinal = r.Ordinal, estado = r.Estado, capturaUrl = r.CapturaUrl };
            return r.Aceptada ? Results.Accepted(value: cuerpo) : Results.Json(cuerpo, Json);
        }

        var extensiones = new Dictionary<string, object?> { ["code"] = r.Codigo };
        if (r.Estado is not null)
            extensiones["state"] = r.Estado;
        if (r.Campo is not null)
            extensiones["field"] = r.Campo;
        return Results.Problem(statusCode: r.Http, title: r.Codigo, detail: r.Mensaje, extensions: extensiones);
    }

    private static async Task<bool> SinTramitesAsync(IFachadaTramites catalogo, Guid organismoId, CancellationToken ct)
    {
        try
        {
            return (await catalogo.ListarTiposAsync(organismoId.ToString(), ct).ConfigureAwait(false)).Count == 0;
        }
        catch (FachadaException)
        {
            return false;
        }
    }

    private static IResult SinSesion() => SesionEndpoints.Problema(
        StatusCodes.Status401Unauthorized, "Sesión del punto", "Entre con el código del organismo.", "sesion_punto_ausente");

    private static IResult Fallo(HttpContext http, CookiePunto cookie, string codigo, string mensaje)
    {
        cookie.Borrar(http);
        return Results.Json(new { code = codigo, message = mensaje }, Json, statusCode: StatusCodes.Status422UnprocessableEntity);
    }

    private static IResult Proveedor(string mensaje) =>
        Results.Json(new { code = CodigoProveedor, message = mensaje }, Json, statusCode: StatusCodes.Status503ServiceUnavailable);

    private static string CodigoDe(string estado) => estado switch
    {
        OrganismoNoOperable => OrganismoNoOperable,
        CanalApagado => CanalApagado,
        _ => CodigoDesconocido,
    };

    private static string MensajeDe(string estado) => estado switch
    {
        OrganismoNoOperable => "Este organismo no está operable en FLIT.",
        CanalApagado => "El canal del punto está apagado.",
        _ => "No encontramos un organismo con ese código.",
    };

    private static (string? Placa, string? Vin, DocumentoConsulta? Propietario)? EntradaVehiculo(ConsultaVehiculoRequest body)
    {
        if (!string.IsNullOrWhiteSpace(body.Vin))
        {
            var vin = EntradaConsulta.Vin(body.Vin);
            return vin is null ? null : (null, vin, null);
        }

        var placa = EntradaConsulta.Placa(body.Placa);
        var propietario = EntradaConsulta.DocumentoPropietario(body.Propietario?.Tipo, body.Propietario?.Numero);
        return placa is null || propietario is null ? null : (placa, null, propietario);
    }
}

/// <summary>Puente a los resultados de validación del portal, que son privados del grupo de consultas.</summary>
internal static class ConsultasEndpointsPublico
{
    public static IResult SinConsentimiento() => SesionEndpoints.Problema(
        StatusCodes.Status400BadRequest, "Consentimiento requerido", "Acepte el tratamiento de datos personales para consultar.", ConsultasEndpoints.CodigoConsentimiento);

    public static IResult DatosInvalidos(string detalle) =>
        SesionEndpoints.Problema(StatusCodes.Status400BadRequest, "Datos inválidos", detalle, ConsultasEndpoints.CodigoDatosInvalidos);
}

public sealed record EntradaPuntoRequest(string? Codigo);

public sealed record ArranquePuntoRequest(string? Placa, string? Documento, string? TipoCodigo);

public sealed record DiagnosticoPuntoRequest(string? Placa, string? Documento, string? TipoCodigo, string? TipoDocumento);

public sealed record AbrirActoresPuntoRequest(string? Placa, string? Documento, string? TipoCodigo, string? TipoDocumento);

public sealed record IdentidadRadicantePuntoRequest(
    Guid? SolicitudId,
    string? Parte,
    short? Ordinal,
    string? TipoDocumento,
    string? Documento,
    string? Nombre,
    string? Correo = null,
    string? Celular = null,
    string? Direccion = null,
    string? Ciudad = null);

public sealed record ConsultaMultasPuntoRequest(string? Placa, DocumentoRequest? Documento, bool Consentimiento);
