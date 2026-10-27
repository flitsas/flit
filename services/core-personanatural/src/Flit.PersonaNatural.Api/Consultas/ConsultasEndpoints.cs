using System.Globalization;
using Flit.PersonaNatural.Api.Abuso;
using Flit.PersonaNatural.Api.Sesiones;

namespace Flit.PersonaNatural.Api.Consultas;

/// <summary>Documento de la petición tal como lo escribe el ciudadano (tipo y número); se valida antes de llamar a nadie.</summary>
public sealed record DocumentoRequest(string? Tipo, string? Numero);

/// <summary>
/// Consulta pública de vehículo: por VIN, o por placa con el documento del propietario (core-consultas no consulta una placa
/// suelta). <c>consentimiento</c> es el de tratamiento de datos del documento consultado y es obligatorio.
/// </summary>
public sealed record ConsultaVehiculoRequest(string? Placa, string? Vin, DocumentoRequest? Propietario, bool Consentimiento);

public sealed record ConsultaPersonaRequest(DocumentoRequest? Documento, bool Consentimiento);

/// <summary><c>parte</c> es el rol del actor que respalda la consulta; si falta vale cualquiera con identidad validada.</summary>
public sealed record DetalleVehiculoRequest(string? Parte);

/// <summary>
/// Endpoints de consulta del BFF (HU #14041; diseño §6.1). Catálogo cerrado: un endpoint por consulta, sin proxy genérico
/// hacia FLIT 2 ni rutas arbitrarias. El front nunca habla con FLIT 2.
/// <list type="bullet">
/// <item><c>POST /consultas/vehiculo</c> y <c>POST /consultas/persona</c> (conductor): públicos, con captcha, límite por IP y
/// topes por documento y globales; devuelven el dato mínimo.</item>
/// <item><c>POST /solicitudes/{id}/consultas/vehiculo-detalle</c>: con la sesión de ESA solicitud y un actor con identidad validada.</item>
/// </list>
/// </summary>
public static class ConsultasEndpoints
{
    public const string CodigoDatosInvalidos = "datos_invalidos";
    public const string CodigoConsentimiento = "consentimiento_requerido";
    public const string CodigoProveedor = "proveedor_no_disponible";
    public const string CodigoIdentidad = "identity_required";
    public const string CodigoSinVehiculo = "vehiculo_no_definido";

    /// <summary>Segundos que se le pide esperar al ciudadano cuando el proveedor no responde.</summary>
    public const int EsperaProveedorSegundos = 30;

    /// <summary>HU #14041: medición de consumo y detalle ampliado (los endpoints se mapean con <see cref="MapConsultasEndpoints"/>).</summary>
    public static IServiceCollection AddConsultasPublicas(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<MedicionConsultasService>();
        services.AddScoped<ConsultaDetalleService>();
        return services;
    }

    public static IEndpointRouteBuilder MapConsultasEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var grupo = app.MapGroup(SesionEndpoints.Base).AllowAnonymous().RequireRateLimiting(AbusoEndpoints.PoliticaPorIp);

        // El orden de los filtros es el de ejecución: captcha primero, luego los topes. Un dato mal formado no gasta tope
        // (el extractor devuelve null y el endpoint responde 400): la IP ya está limitada por el grupo.
        grupo.MapPost("/consultas/vehiculo", async (ConsultaVehiculoRequest body, HttpContext http, ConsultaVehiculoService vehiculos, MedicionConsultasService medicion, CancellationToken ct) =>
        {
            if (body is null || !body.Consentimiento)
                return SinConsentimiento();
            var entrada = ValidarVehiculo(body);
            if (entrada is null)
                return DatosInvalidos("Indique la placa y el documento del propietario, o el VIN del vehículo.");

            return await ConsultarAsync(http, medicion, MedicionConsultasService.TipoVehiculo,
                () => vehiculos.ConsultarAsync(entrada.Placa, entrada.Vin, entrada.Propietario, null, ct), ct).ConfigureAwait(false);
        }).ConCaptcha().ConLimiteDeConsultas(c =>
        {
            var body = c.Arguments.OfType<ConsultaVehiculoRequest>().FirstOrDefault();
            return body is { Consentimiento: true } ? ValidarVehiculo(body)?.ClaveTope : null;
        });

        grupo.MapPost("/consultas/persona", async (ConsultaPersonaRequest body, HttpContext http, ConsultaConductorService conductores, MedicionConsultasService medicion, CancellationToken ct) =>
        {
            if (body is null || !body.Consentimiento)
                return SinConsentimiento();
            var documento = EntradaConsulta.Documento(body.Documento?.Tipo, body.Documento?.Numero);
            if (documento is null)
                return DatosInvalidos("Indique el tipo y el número del documento.");

            return await ConsultarAsync(http, medicion, MedicionConsultasService.TipoPersona,
                () => conductores.ConsultarAsync(documento, null, ct), ct).ConfigureAwait(false);
        }).ConCaptcha().ConLimiteDeConsultas(c =>
        {
            var body = c.Arguments.OfType<ConsultaPersonaRequest>().FirstOrDefault();
            return body is { Consentimiento: true } ? EntradaConsulta.Documento(body.Documento?.Tipo, body.Documento?.Numero)?.Numero : null;
        });

        // Con sesión de la solicitud: el filtro responde 401 o 403 antes que el handler. La id de la ruta es el primer Guid
        // de los argumentos, que es lo que ConSesionDeLaSolicitud compara con la sesión.
        grupo.MapPost("/solicitudes/{id:guid}/consultas/vehiculo-detalle", async (Guid id, DetalleVehiculoRequest? body, HttpContext http, ConsultaDetalleService detalle, CancellationToken ct) =>
        {
            ResultadoDetalle resultado;
            try
            {
                resultado = await detalle.ConsultarAsync(id, body?.Parte, ct).ConfigureAwait(false);
            }
            catch (ProveedorNoDisponibleException)
            {
                return ProveedorNoDisponible(http);
            }

            return resultado.Estado switch
            {
                EstadoDetalle.Ok => Results.Ok(resultado.Detalle),
                EstadoDetalle.IdentidadRequerida => SesionEndpoints.Problema(
                    StatusCodes.Status403Forbidden, "Identidad requerida", "Valide su identidad para ver el detalle del vehículo.", CodigoIdentidad),
                EstadoDetalle.SinVehiculo => SesionEndpoints.Problema(
                    StatusCodes.Status409Conflict, "Sin vehículo", "El trámite todavía no tiene placa ni VIN.", CodigoSinVehiculo),
                _ => SesionEndpoints.Problema(StatusCodes.Status404NotFound, "No encontrado", SesionEndpoints.MensajeNoEncontrada, SesionEndpoints.CodigoNoEncontrada),
            };
        }).AddEndpointFilter(SesionEndpoints.ConSesionDeLaSolicitud);

        return app;
    }

    /// <summary>Ejecuta la consulta, la cuenta en la medición y traduce un proveedor caído a 503 con <c>Retry-After</c>.</summary>
    private static async Task<IResult> ConsultarAsync<T>(HttpContext http, MedicionConsultasService medicion, string tipo, Func<Task<T>> consulta, CancellationToken ct)
    {
        try
        {
            return Results.Ok(await consulta().ConfigureAwait(false));
        }
        catch (ProveedorNoDisponibleException)
        {
            return ProveedorNoDisponible(http);
        }
        finally
        {
            // Llegó a core-consultas: cuenta, haya respondido o no. Sin el token de la petición, para que un cliente que
            // se desconecta no deje la consulta sin contar.
            await medicion.RegistrarAsync(tipo, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static IResult ProveedorNoDisponible(HttpContext http)
    {
        http.Response.Headers.RetryAfter = EsperaProveedorSegundos.ToString(CultureInfo.InvariantCulture);
        return SesionEndpoints.Problema(
            StatusCodes.Status503ServiceUnavailable, "Consulta no disponible",
            "No pudimos consultar el RUNT en este momento. Intente de nuevo en unos minutos.", CodigoProveedor);
    }

    private static IResult SinConsentimiento() => SesionEndpoints.Problema(
        StatusCodes.Status400BadRequest, "Consentimiento requerido", "Acepte el tratamiento de datos personales para consultar.", CodigoConsentimiento);

    private static IResult DatosInvalidos(string detalle) => SesionEndpoints.Problema(StatusCodes.Status400BadRequest, "Datos inválidos", detalle, CodigoDatosInvalidos);

    /// <summary>
    /// Modo de la consulta de vehículo ya validado. <c>ClaveTope</c> es lo que cuentan los topes: el documento del propietario,
    /// o el VIN cuando no hay propietario (un VIN consultado sin límite sería la puerta trasera del tope por documento).
    /// </summary>
    private sealed record EntradaVehiculo(string? Placa, string? Vin, DocumentoConsulta? Propietario, string ClaveTope);

    private static EntradaVehiculo? ValidarVehiculo(ConsultaVehiculoRequest? body)
    {
        if (body is null)
            return null;

        if (!string.IsNullOrWhiteSpace(body.Vin))
        {
            var vin = EntradaConsulta.Vin(body.Vin);
            return vin is null ? null : new EntradaVehiculo(null, vin, null, vin);
        }

        var placa = EntradaConsulta.Placa(body.Placa);
        var propietario = EntradaConsulta.Documento(body.Propietario?.Tipo, body.Propietario?.Numero);
        return placa is null || propietario is null ? null : new EntradaVehiculo(placa, null, propietario, propietario.Numero);
    }
}
