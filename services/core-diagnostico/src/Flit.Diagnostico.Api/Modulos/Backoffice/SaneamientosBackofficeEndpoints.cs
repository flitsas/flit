using Flit.Diagnostico.Api.Modulos.Comun;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Modulos.Reportes;
using Flit.Diagnostico.Api.Modulos.Saneamiento;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Api.Plataforma;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SaneamientoEntidad = Flit.Diagnostico.Api.Modulos.Saneamiento.Saneamiento;

namespace Flit.Diagnostico.Api.Modulos.Backoffice;

/// <summary>
/// Rutas <c>/backoffice/saneamientos…</c> de la gestión FLIT (plan §6.2, ADR-0087 D-5; HU #14576). Todas exigen la política del
/// backoffice, la de operación FLIT (<see cref="PoliticaBackoffice.RequiereOperacionFlit{TBuilder}"/>) y
/// <c>diagnostico.saneamiento.gestionar</c>: un usuario de otra compañía con los permisos recibe 403. Dos grupos (SP-4):
/// <list type="bullet">
/// <item><b>Grupo A</b>, sin empresa explícita (única excepción a ADR-0073): bandeja, resumen (insignia), Excel y «tomar varios». La
/// bandeja y el Excel auditan cada empresa de la página en un guardado (<see cref="BandejaSaneamientos"/>).</item>
/// <item><b>Grupo B</b>, con <c>?empresa=</c> (<see cref="EmpresaExplicita"/>, que audita cada GET): detalle, deuda (SP-7), tomar,
/// observación, renglones, evidencia, cerrar, reabrir y anular. Rutas tipadas <c>{sid:guid}</c>/<c>{rid:guid}</c> (SP-6): un literal
/// del grupo A o un id que no es uuid nunca entra al grupo B.</item>
/// </list>
/// Respuestas (D-20 con D-65): las transiciones, la observación, agregar un OTRO, quitar un renglón y quitar la evidencia devuelven el
/// saneamiento completo; editar un renglón y subir la evidencia, el renglón. Las descargas llevan <c>DescargaSegura</c> (SP-18).
/// </summary>
internal static class SaneamientosBackofficeEndpoints
{
    public const string Saneamientos = "/saneamientos";

    public const string Ruta = BackofficeEndpoints.Grupo + Saneamientos;

    public static void Map(RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var maximo = EvidenciaSaneamiento.Maximo(((IEndpointRouteBuilder)api).ServiceProvider.GetRequiredService<IConfiguration>());

        // El grupo A cuelga de /backoffice con la ruta completa, para que la bandeja sea /backoffice/saneamientos sin barra final.
        var a = api.MapGroup(BackofficeEndpoints.Grupo).RequireAuthorization(PoliticaBackoffice.Nombre).RequiereOperacionFlit().RequirePermiso(Permisos.SaneamientoGestionar);
        a.MapGet(Saneamientos, (HttpRequest request, BandejaSaneamientos bandeja, CancellationToken ct) => bandeja.ListarAsync(request, ct))
            .WithName("ListarSaneamientosBackoffice");
        a.MapGet(Saneamientos + "/resumen", (BandejaSaneamientos bandeja, CancellationToken ct) => bandeja.ResumenAsync(ct))
            .SinCacheEnRespuesta().WithName("ResumenSaneamientosBackoffice");
        a.MapGet(Saneamientos + "/excel", async (HttpRequest request, BandejaSaneamientos bandeja, CancellationToken ct) =>
                Results.File(await bandeja.ExcelAsync(request, ct).ConfigureAwait(false), ExcelBandejaSaneamientos.ContentType, ExcelBandejaSaneamientos.NombreArchivo))
            .DescargaSegura().WithName("DescargarExcelSaneamientosBackoffice");
        a.MapPost(Saneamientos + "/tomar", (HttpRequest request, GestionSaneamientoBackoffice gestion, CancellationToken ct) => gestion.TomarVariosAsync(request, ct))
            .WithName("TomarSaneamientosBackoffice");

        var b = api.MapGroup(Ruta).RequireAuthorization(PoliticaBackoffice.Nombre).RequiereOperacionFlit().RequirePermiso(Permisos.SaneamientoGestionar)
            .ConEmpresaExplicita();
        b.MapGet("/{sid:guid}", (Guid sid, HttpContext http, GestionSaneamientoBackoffice g, CancellationToken ct) => g.DetalleAsync(EmpresaExplicita.De(http), sid, ct))
            .SinCacheEnRespuesta().WithName("ObtenerSaneamientoBackoffice");
        b.MapGet("/{sid:guid}/deuda", Deuda).SinCacheEnRespuesta().WithName("ObtenerDeudaSaneamientoBackoffice");
        b.MapPost("/{sid:guid}/tomar", (Guid sid, HttpContext http, GestionSaneamientoBackoffice g, CancellationToken ct) => g.TomarAsync(EmpresaExplicita.De(http), sid, ct))
            .WithName("TomarSaneamientoBackoffice");
        b.MapPost("/{sid:guid}/anular", (Guid sid, HttpContext http, GestionSaneamientoBackoffice g, CancellationToken ct) => g.AnularAsync(EmpresaExplicita.De(http), sid, http.Request, ct))
            .WithName("AnularSaneamientoBackoffice");
        b.MapPost("/{sid:guid}/cerrar", (Guid sid, HttpContext http, GestionSaneamientoBackoffice g, CancellationToken ct) => g.CerrarAsync(EmpresaExplicita.De(http), sid, http.Request, ct))
            .WithName("CerrarSaneamientoBackoffice");
        b.MapPost("/{sid:guid}/reabrir", (Guid sid, HttpContext http, GestionSaneamientoBackoffice g, CancellationToken ct) => g.ReabrirAsync(EmpresaExplicita.De(http), sid, http.Request, ct))
            .WithName("ReabrirSaneamientoBackoffice");
        b.MapPut("/{sid:guid}/observacion", (Guid sid, HttpContext http, GestionSaneamientoBackoffice g, CancellationToken ct) => g.ObservacionAsync(EmpresaExplicita.De(http), sid, http.Request, ct))
            .WithName("EditarObservacionSaneamientoBackoffice");
        b.MapPost("/{sid:guid}/renglones", (Guid sid, HttpContext http, GestionSaneamientoBackoffice g, CancellationToken ct) => g.AgregarRenglonAsync(EmpresaExplicita.De(http), sid, http.Request, ct))
            .WithName("AgregarRenglonSaneamientoBackoffice");
        b.MapPut("/{sid:guid}/renglones/{rid:guid}", (Guid sid, Guid rid, HttpContext http, GestionSaneamientoBackoffice g, CancellationToken ct) =>
                g.EditarRenglonAsync(EmpresaExplicita.De(http), sid, rid, http.Request, ct))
            .WithName("EditarRenglonSaneamientoBackoffice");
        b.MapDelete("/{sid:guid}/renglones/{rid:guid}", (Guid sid, Guid rid, HttpContext http, GestionSaneamientoBackoffice g, CancellationToken ct) =>
                g.QuitarRenglonAsync(EmpresaExplicita.De(http), sid, rid, ct))
            .WithName("QuitarRenglonSaneamientoBackoffice");
        b.MapPut("/{sid:guid}/renglones/{rid:guid}/evidencia", (Guid sid, Guid rid, HttpContext http, EvidenciaSaneamiento e, CancellationToken ct) =>
                e.SubirAsync(EmpresaExplicita.De(http), sid, rid, http.Request, ct))
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(maximo + EvidenciaSaneamiento.Margen))
            .WithFormOptions(multipartBodyLengthLimit: maximo + EvidenciaSaneamiento.Margen)
            .WithName("SubirEvidenciaSaneamientoBackoffice");
        b.MapDelete("/{sid:guid}/renglones/{rid:guid}/evidencia", (Guid sid, Guid rid, HttpContext http, GestionSaneamientoBackoffice g, CancellationToken ct) =>
                g.QuitarEvidenciaAsync(EmpresaExplicita.De(http), sid, rid, ct))
            .WithName("QuitarEvidenciaSaneamientoBackoffice");
        b.MapGet("/{sid:guid}/renglones/{rid:guid}/evidencia", (Guid sid, Guid rid, HttpContext http, EvidenciaSaneamiento e, CancellationToken ct) =>
                e.DescargarAsync(EmpresaExplicita.De(http), sid, rid, ct))
            .DescargaSegura().WithName("DescargarEvidenciaSaneamientoBackoffice");
    }

    /// <summary>
    /// SP-7 (opción a): el panel de deuda del vehículo del saneamiento, con la misma proyección del cliente (<see cref="ConsultaDeuda"/>,
    /// SP-11), leído en un scope fijado a la empresa explícita (D-81). 404 si el saneamiento no es de esa empresa; 409 si el vehículo no
    /// terminó. El GET ya quedó auditado por el filtro de empresa explícita.
    /// </summary>
    private static async Task<IResult> Deuda(Guid sid, HttpContext http, DiagnosticoDb db, IServiceScopeFactory scopes, CancellationToken ct)
    {
        var empresa = EmpresaExplicita.De(http);
        var ids = await db.DeLaEmpresa<SaneamientoEntidad>(empresa).Where(s => s.Id == sid)
            .Select(s => new { s.DiagnosticoId, s.VehiculoId }).SingleOrDefaultAsync(ct).ConfigureAwait(false)
            ?? throw GestionSaneamientoBackoffice.NoEncontrado();
        await using var ambito = AmbitoDeLaEmpresa.Abrir(scopes, empresa);
        var dbEmpresa = ambito.ServiceProvider.GetRequiredService<DiagnosticoDb>();
        var cabecera = await dbEmpresa.Set<CabeceraDiagnostico>().AsNoTracking().SingleOrDefaultAsync(c => c.Id == ids.DiagnosticoId, ct).ConfigureAwait(false)
            ?? throw GestionSaneamientoBackoffice.NoEncontrado();
        return Results.Ok(await ambito.ServiceProvider.GetRequiredService<ConsultaDeuda>().DeLoteAsync(cabecera, ids.VehiculoId, ct).ConfigureAwait(false));
    }
}
