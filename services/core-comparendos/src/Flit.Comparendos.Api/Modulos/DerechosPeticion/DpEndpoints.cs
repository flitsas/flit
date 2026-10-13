using Flit.Comparendos.Api.Modulos.Comparendos;
using Flit.Comparendos.Api.Modulos.Correo;
using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Persistence.Modelo;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Modelo;
using Microsoft.EntityFrameworkCore;

namespace Flit.Comparendos.Api.Modulos.DerechosPeticion;

/// <summary>Derecho de petición individual y respuesta del OT (HU #13488, #13490; §7.3).</summary>
public static class DpEndpoints
{
    public sealed record PedidoDp(Guid TipologiaId);

    public static void Map(RouteGroupBuilder api)
    {
        var g = api.MapGroup("/comparendos/{id:guid}/dp").RequireCapacidad(Capacidad.DerechosPeticion);
        g.MapGet("/tipologias", Tipologias).RequirePermiso(Permisos.DpGenerar);
        g.MapPost("/vista-previa", VistaPrevia).RequirePermiso(Permisos.DpGenerar);
        g.MapGet("/vista-previa.pdf", VistaPreviaPdf).RequirePermiso(Permisos.DpGenerar);
        g.MapPost("", Enviar).RequirePermiso(Permisos.DpEnviar);
        g.MapPost("/reintentar", Reintentar).RequirePermiso(Permisos.DpEnviar);
        g.MapPost("/respuesta", Responder).RequirePermiso(Permisos.DpResponder).DisableAntiforgery();
    }

    private static async Task<IResult> Tipologias(Guid id, AccesoComparendo acceso, ServicioDp servicio, CancellationToken ct)
    {
        var c = await acceso.ParaLeerAsync(id, ct).ConfigureAwait(false);
        var lista = await servicio.TipologiasAsync(c, ct).ConfigureAwait(false);
        return Results.Ok(lista.Select(x => new
        {
            x.Tipologia.Id, x.Tipologia.Codigo, x.Tipologia.Nombre, x.Tipologia.DescripcionJuridica, x.Tipologia.EstadosLegalesPermitidos, x.Tipologia.PermiteMasivo, procede = x.Procede,
        }));
    }

    private static async Task<IResult> VistaPrevia(Guid id, PedidoDp pedido, AccesoComparendo acceso, ServicioDp servicio, Reloj reloj, CancellationToken ct)
    {
        var c = await acceso.ParaOperarAsync(id, ct).ConfigureAwait(false);
        var p = await servicio.PrepararAsync(c, pedido.TipologiaId, masivo: false, reintento: false, ct).ConfigureAwait(false);
        return Results.Ok(new
        {
            puedeEnviar = p.Bloqueos.Count == 0,
            bloqueos = p.Bloqueos,
            advertencias = p.Advertencias,
            radicado = ServicioDp.RadicadoEnmascarado(p.Tipologia, reloj.Hoy),
            organismo = p.Organismo?.Nombre,
            destinatarios = p.Organismo?.CorreosRadicacion ?? [],
            representante = p.Representante is { } r ? new { r.Nombres, r.Cargo, r.NumeroDocumento } : null,
            anexos = p.Anexos.Select(a => a.Nombre).Prepend("Derecho de petición (PDF)"),
            estadoLegal = p.EstadoLegal.ToString(),
        });
    }

    private static async Task<IResult> VistaPreviaPdf(Guid id, Guid tipologiaId, AccesoComparendo acceso, ServicioDp servicio, Reloj reloj, CancellationToken ct)
    {
        var c = await acceso.ParaOperarAsync(id, ct).ConfigureAwait(false);
        var p = await servicio.PrepararAsync(c, tipologiaId, masivo: false, reintento: false, ct).ConfigureAwait(false);
        if (p.Bloqueos.FirstOrDefault(b => b.Codigo == "SIN_PLANTILLA") is { } sinPlantilla)
            throw new ReglaException(sinPlantilla.Codigo, sinPlantilla.Mensaje);
        var (pdf, _) = await servicio.DocumentoAsync(c, p, ServicioDp.RadicadoEnmascarado(p.Tipologia, reloj.Hoy), vistaPrevia: true, ct).ConfigureAwait(false);
        return Results.File(pdf, "application/pdf");
    }

    private static async Task<IResult> Enviar(Guid id, PedidoDp pedido, AccesoComparendo acceso, ServicioDp servicio, UsuarioActual usuario, ComparendosDb db, DespachadorCorreos despachador, CancellationToken ct)
    {
        var c = await acceso.ParaOperarAsync(id, ct).ConfigureAwait(false);
        var dp = await servicio.EnviarAsync(c, pedido.TipologiaId, Actor.De(usuario), loteId: null, ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await despachador.DespacharAsync(dp.ComunicacionId!.Value, ct).ConfigureAwait(false);
        var estado = await db.DerechosPeticion.AsNoTracking().Where(d => d.Id == dp.Id).Select(d => d.Estado).FirstAsync(ct).ConfigureAwait(false);
        return Results.Ok(new { dp.Id, dp.Radicado, estado = estado.ToString() });
    }

    private static async Task<IResult> Reintentar(Guid id, AccesoComparendo acceso, ServicioDp servicio, UsuarioActual usuario, ComparendosDb db, DespachadorCorreos despachador, CancellationToken ct)
    {
        var c = await acceso.ParaOperarAsync(id, ct).ConfigureAwait(false);
        var dp = await db.DerechosPeticion.FirstOrDefaultAsync(d => d.ComparendoId == id, ct).ConfigureAwait(false) ?? throw ReglaException.NoEncontrado("El derecho de petición");
        await servicio.ReintentarAsync(c, dp, Actor.De(usuario), ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await despachador.DespacharAsync(dp.ComunicacionId!.Value, ct).ConfigureAwait(false);
        var estado = await db.DerechosPeticion.AsNoTracking().Where(d => d.Id == dp.Id).Select(d => d.Estado).FirstAsync(ct).ConfigureAwait(false);
        return Results.Ok(new { dp.Radicado, estado = estado.ToString() });
    }

    /// <summary>Formulario: <c>radicadoOt</c>, <c>fecha</c>, <c>sentido</c> (Favorable o Desfavorable) y <c>archivo</c> (PDF).</summary>
    private static async Task<IResult> Responder(Guid id, HttpRequest request, AccesoComparendo acceso, ServicioDp servicio, UsuarioActual usuario, ComparendosDb db, CancellationToken ct)
    {
        var form = await request.ReadFormAsync(ct).ConfigureAwait(false);
        if (!Enum.TryParse<SentidoRespuestaOt>(form["sentido"].ToString(), true, out var sentido))
            throw new ReglaException("SENTIDO_INVALIDO", "El sentido es Favorable o Desfavorable.");
        if (!DateOnly.TryParse(form["fecha"].ToString(), System.Globalization.CultureInfo.InvariantCulture, out var fecha))
            throw new ReglaException("FECHA_INVALIDA", "Escribe la fecha de la respuesta.");
        var c = await acceso.ParaOperarAsync(id, ct).ConfigureAwait(false);
        var dp = await db.DerechosPeticion.FirstOrDefaultAsync(d => d.ComparendoId == id, ct).ConfigureAwait(false) ?? throw ReglaException.NoEncontrado("El derecho de petición");
        await servicio.RegistrarRespuestaAsync(c, dp, form["radicadoOt"].ToString(), fecha, sentido, form.Files.GetFile("archivo"), Actor.De(usuario), ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.NoContent();
    }
}
