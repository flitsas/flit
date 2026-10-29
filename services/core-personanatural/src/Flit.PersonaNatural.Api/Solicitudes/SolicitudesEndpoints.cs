using Flit.PersonaNatural.Api.Abuso;
using Flit.PersonaNatural.Api.Cotizaciones;
using Flit.PersonaNatural.Api.Radicacion;
using Flit.PersonaNatural.Api.Sesiones;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Flit.PersonaNatural.Api.Solicitudes;

/// <summary>
/// Endpoints del borrador del trámite (HU #14047, ADR-0075). <c>POST /sesiones</c> es público (captcha y límite por IP); los
/// de <c>/solicitudes/{id}</c> exigen la sesión de ESA solicitud (<see cref="SesionEndpoints.ConSesionDeLaSolicitud"/>).
/// Los errores son <c>application/problem+json</c> con <c>code</c> estable; los de validación traen <c>errors</c> por campo.
/// </summary>
public static class SolicitudesEndpoints
{
    public static IServiceCollection AddSolicitudesPortal(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IFachadaRequisitos>(sp =>
            sp.GetService<Flit.Tramites.Grpc.V1.PersonaNaturalService.PersonaNaturalServiceClient>() is { } cliente
                ? new FachadaRequisitosGrpc(cliente)
                : new FachadaRequisitosNoConfigurada());
        services.AddScoped<SolicitudesService>();
        return services;
    }

    public static IEndpointRouteBuilder MapSolicitudesEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var grupo = app.MapGroup(SesionEndpoints.Base).AllowAnonymous().RequireRateLimiting(AbusoEndpoints.PoliticaPorIp);

        grupo.MapPost("/sesiones", (CrearSolicitudRequest body, HttpContext http, SolicitudesService s, PiiHasher hasher, CancellationToken ct) =>
            Atender(async () => Results.Created(
                $"{SesionEndpoints.Base}/solicitudes/{{id}}",
                await s.CrearAsync(body, hasher.HashIp(http.Connection.RemoteIpAddress?.ToString()), ct).ConfigureAwait(false)))).ConCaptcha();

        var conSesion = grupo.MapGroup("/solicitudes/{id:guid}");
        conSesion.AddEndpointFilter(Radicacion.SesionTrasRadicar.Filtro);

        conSesion.MapGet(string.Empty, (Guid id, SolicitudesService s, CancellationToken ct) =>
            Atender(async () => Results.Ok(await s.ObtenerAsync(id, ct).ConfigureAwait(false)))).WithMetadata(PermiteTrasRadicar.Instancia);
        // HU #14049: estado sin datos personales; es lo que sondea el front tras radicar con la sesión de solo estado.
        conSesion.MapGet("/estado", (Guid id, SolicitudesService s, CancellationToken ct) =>
            Atender(async () => Results.Ok(await s.ObtenerEstadoAsync(id, ct).ConfigureAwait(false)))).WithMetadata(PermiteTrasRadicar.Instancia);
        conSesion.MapPatch("/actores", (Guid id, ActoresRequest body, SolicitudesService s, CancellationToken ct) =>
            Atender(async () => Results.Ok(await s.GuardarActoresAsync(id, body, ct).ConfigureAwait(false))));
        conSesion.MapPut("/campos", (Guid id, CamposRequest body, SolicitudesService s, CancellationToken ct) =>
            Atender(async () => Results.Ok(await s.GuardarCamposAsync(id, body, ct).ConfigureAwait(false))));
        conSesion.MapPut("/comercial", (Guid id, ComercialRequest body, SolicitudesService s, CancellationToken ct) =>
            Atender(async () => Results.Ok(await s.GuardarComercialAsync(id, body, ct).ConfigureAwait(false))));
        conSesion.MapPut("/requisitos", (Guid id, RequisitosRequest body, SolicitudesService s, CancellationToken ct) =>
            Atender(async () => Results.Ok(await s.GuardarRequisitosAsync(id, body, ct).ConfigureAwait(false))));
        conSesion.MapPut("/organismo", (Guid id, AsignarOrganismoRequest body, SolicitudesService s, CancellationToken ct) =>
            Atender(async () => Results.Ok(await s.AsignarOrganismoAsync(id, body, ct).ConfigureAwait(false))));
        conSesion.MapPost("/diagnostico", (Guid id, DiagnosticoRequest? body, SolicitudesService s, CancellationToken ct) =>
            Atender(async () => Results.Ok(await s.DiagnosticarAsync(id, body, ct).ConfigureAwait(false))));
        conSesion.MapDelete(string.Empty, (Guid id, SolicitudesService s, CancellationToken ct) =>
            Atender(async () => Results.Ok(await s.CancelarAsync(id, ct).ConfigureAwait(false))));

        return app;
    }

    /// <summary>Ejecuta la acción y traduce las reglas del negocio y los errores de la fachada a <c>problem+json</c> con código estable.</summary>
    internal static async Task<IResult> Atender(Func<Task<IResult>> accion)
    {
        try
        {
            return await accion().ConfigureAwait(false);
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
    }
}
