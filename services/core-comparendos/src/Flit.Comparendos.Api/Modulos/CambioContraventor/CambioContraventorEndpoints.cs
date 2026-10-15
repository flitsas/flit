using Flit.Comparendos.Api.Modulos.Comparendos;
using Flit.Comparendos.Api.Modulos.Correo;
using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Modelo;
using Microsoft.EntityFrameworkCore;

namespace Flit.Comparendos.Api.Modulos.CambioContraventor;

/// <summary>Cambio de contraventor (HU #13479, §7.4): viabilidad, vista previa, solicitud, reenvío del aviso 4 y respuesta del OT.</summary>
public static class CambioContraventorEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        var g = api.MapGroup("/comparendos/{id:guid}/cambio-contraventor");
        g.MapGet("/viabilidad", Viabilidad).RequirePermiso(Permisos.BandejaVer);
        g.MapGet("/vista-previa", VistaPrevia).RequirePermiso(Permisos.CambioContraventorSolicitar).RequireCapacidad(Capacidad.CambioContraventor);
        g.MapPost("", Solicitar).RequirePermiso(Permisos.CambioContraventorSolicitar).RequireCapacidad(Capacidad.CambioContraventor);
        g.MapPost("/aviso4", ReenviarAviso4).RequirePermiso(Permisos.AvisosEnviar).RequireCapacidad(Capacidad.CambioContraventor);
        g.MapPost("/{solicitudId:guid}/respuesta", Responder).RequirePermiso(Permisos.CambioContraventorResponder).RequireCapacidad(Capacidad.CambioContraventor).DisableAntiforgery();
    }

    private static async Task<IResult> Viabilidad(Guid id, AccesoComparendo acceso, ServicioCambioContraventor servicio, CancellationToken ct)
    {
        var c = await acceso.ParaLeerAsync(id, ct).ConfigureAwait(false);
        var (condiciones, rl, ot) = await servicio.ViabilidadAsync(c, ct).ConfigureAwait(false);
        return Results.Ok(new { viable = condiciones.All(x => x.Cumple), condiciones, representante = rl?.Nombres, organismo = ot?.Nombre, correos = ot?.CorreosRadicacion });
    }

    /// <summary>Vista previa del formato al OT o, con <c>?documento=carta</c>, de la carta del aviso 4 al conductor (§7.4).</summary>
    private static async Task<IResult> VistaPrevia(Guid id, string? documento, AccesoComparendo acceso, ServicioCambioContraventor servicio, Configuracion.ResolutorRepresentante resolutor, CancellationToken ct)
    {
        var c = await acceso.ParaOperarAsync(id, ct).ConfigureAwait(false);
        if (string.Equals(documento, "carta", StringComparison.OrdinalIgnoreCase))
            return Results.File(await servicio.CartaAsync(c, "VISTA PREVIA", ct).ConfigureAwait(false), "application/pdf");
        var rl = await resolutor.ResolverAsync(c.TenantId, c.IdentificacionMonitoreadaId, Configuracion.ParaFirmar.CambioContraventor, ct).ConfigureAwait(false);
        return Results.File(await servicio.FormatoAsync(c, rl, "VISTA PREVIA", ct).ConfigureAwait(false), "application/pdf");
    }

    private static async Task<IResult> Solicitar(Guid id, AccesoComparendo acceso, ServicioCambioContraventor servicio, UsuarioActual usuario, ComparendosDb db, DespachadorCorreos despachador, CancellationToken ct)
    {
        var c = await acceso.ParaOperarAsync(id, ct).ConfigureAwait(false);
        var solicitud = await servicio.SolicitarAsync(c, Comparendos.Actor.De(usuario), ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        var estado = await despachador.DespacharAsync(solicitud.ComunicacionId!.Value, ct).ConfigureAwait(false);
        // Si el OT recibió la solicitud, la reacción del despachador dejó el aviso 4 listo: se envía ya.
        var aviso4 = await db.SolicitudesCambio.AsNoTracking().Where(s => s.Id == solicitud.Id).Select(s => s.Aviso4ComunicacionId).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (aviso4 is { } a4)
            await despachador.DespacharAsync(a4, ct).ConfigureAwait(false);
        return Results.Ok(new { solicitud.Id, solicitud.NumeroIntento, correoOt = estado.ToString() });
    }

    private static async Task<IResult> ReenviarAviso4(Guid id, AccesoComparendo acceso, ServicioCambioContraventor servicio, UsuarioActual usuario, ComparendosDb db, DespachadorCorreos despachador, CancellationToken ct)
    {
        var c = await acceso.ParaOperarAsync(id, ct).ConfigureAwait(false);
        var solicitud = await db.SolicitudesCambio.Where(s => s.ComparendoId == id && s.Estado != EstadoSolicitudCambio.EnvioFallido).OrderByDescending(s => s.NumeroIntento).FirstOrDefaultAsync(ct).ConfigureAwait(false)
            ?? throw new ReglaException("SIN_SOLICITUD", "El aviso 4 solo se envía cuando hay una solicitud de cambio de contraventor.");
        var comunicacion = await servicio.EnviarAviso4Async(c, solicitud, Comparendos.Actor.De(usuario), ct).ConfigureAwait(false)
            ?? throw new ReglaException("SIN_CORREO_CONDUCTOR", "El conductor no tiene correo.");
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Ok(new { estado = (await despachador.DespacharAsync(comunicacion, ct).ConfigureAwait(false)).ToString() });
    }

    /// <summary>Formulario: <c>resultado</c> (Aceptado o Rechazado), <c>causal</c> (obligatoria si se rechaza), <c>fecha</c> y <c>soporte</c> opcional.</summary>
    private static async Task<IResult> Responder(Guid id, Guid solicitudId, HttpRequest request, AccesoComparendo acceso, ServicioCambioContraventor servicio, UsuarioActual usuario, ComparendosDb db, CancellationToken ct)
    {
        var form = await request.ReadFormAsync(ct).ConfigureAwait(false);
        var resultado = form["resultado"].ToString();
        if (resultado is not ("Aceptado" or "Rechazado"))
            throw new ReglaException("RESULTADO_INVALIDO", "El resultado es Aceptado o Rechazado.");
        if (!DateOnly.TryParse(form["fecha"].ToString(), System.Globalization.CultureInfo.InvariantCulture, out var fecha))
            throw new ReglaException("FECHA_INVALIDA", "Escribe la fecha de la respuesta del OT.");
        var c = await acceso.ParaOperarAsync(id, ct).ConfigureAwait(false);
        await servicio.RegistrarRespuestaAsync(c, solicitudId, resultado == "Aceptado", form["causal"].ToString(), fecha, form.Files.GetFile("soporte"), Comparendos.Actor.De(usuario), ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.NoContent();
    }
}
