using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using System.Text.Json.Serialization;
using Flit.PersonaNatural.Api.Abuso;
using Flit.PersonaNatural.Api.Consultas;
using Flit.PersonaNatural.Api.Cotizaciones;
using Flit.PersonaNatural.Api.Sesiones;
using Flit.Tramites.Grpc.V1;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Flit.PersonaNatural.Api.Punto;

/// <summary>
/// Canal del punto de atención (HU #14216, #14219 y #14221). La cookie fija el organismo: no hay forma de cambiarlo
/// en la petición. Ninguna ruta crea un trámite en FLIT 2 ni una solicitud de radicación.
/// </summary>
public static class PuntoEndpoints
{
    public const string Cookie = CookiePunto.Nombre;
    public const string CodigoDesconocido = "codigo_desconocido";
    public const string OrganismoNoOperable = "organismo_no_operable";
    public const string CanalApagado = "canal_apagado";
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

        grupo.MapPost("/entrada", async (EntradaPuntoRequest? body, HttpContext http, ICanalPunto canal, CookiePunto cookie, CancellationToken ct) =>
        {
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

            var organismo = new OrganismoPunto(resolucion.OrganismoId, resolucion.Codigo, resolucion.Nombre);
            cookie.Fijar(http, organismo);
            return Results.Json(new { organismoId = organismo.OrganismoId, codigo = organismo.Codigo, nombre = organismo.Nombre }, Json);
        });

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

public sealed record ConsultaMultasPuntoRequest(string? Placa, DocumentoRequest? Documento, bool Consentimiento);
