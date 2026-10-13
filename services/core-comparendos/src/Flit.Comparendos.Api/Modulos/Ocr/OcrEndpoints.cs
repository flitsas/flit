using Flit.Comparendos.Api.Modulos.Comparendos;
using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Persistence.Modelo;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Modelo;
using Microsoft.EntityFrameworkCore;

namespace Flit.Comparendos.Api.Modulos.Ocr;

/// <summary>Pantallas de OCR (§7.6): paquetes, documentos, revisión y estadísticas. Solo con <c>has_ocr_access</c>.</summary>
public static class OcrEndpoints
{
    public sealed record PedidoCorreccion(Dictionary<string, string?> Campos, string? Motivo);

    public sealed record PedidoDescarte(string? Motivo);

    public static void Map(RouteGroupBuilder api)
    {
        var g = api.MapGroup("/ocr");
        g.MapGet("/paquetes", Paquetes).RequirePermiso(Permisos.OcrVer);
        g.MapPost("/paquetes", Crear).RequirePermiso(Permisos.OcrGestionar).DisableAntiforgery();
        g.MapGet("/paquetes/{id:guid}", Paquete).RequirePermiso(Permisos.OcrVer);
        g.MapPost("/paquetes/{id:guid}/iniciar", Iniciar).RequirePermiso(Permisos.OcrGestionar);
        g.MapPost("/paquetes/{id:guid}/registrar-completos", RegistrarCompletos).RequirePermiso(Permisos.OcrGestionar);
        g.MapPost("/paquetes/{id:guid}/descartar-fallidos", DescartarFallidos).RequirePermiso(Permisos.OcrGestionar);
        g.MapGet("/documentos/{id:guid}", Documento).RequirePermiso(Permisos.OcrVer);
        g.MapPut("/documentos/{id:guid}/campos", Corregir).RequirePermiso(Permisos.OcrGestionar);
        g.MapPost("/documentos/{id:guid}/registrar", Registrar).RequirePermiso(Permisos.OcrGestionar);
        g.MapPost("/documentos/{id:guid}/descartar", Descartar).RequirePermiso(Permisos.OcrGestionar);
        g.MapPost("/documentos/{id:guid}/reintentar", Reintentar).RequirePermiso(Permisos.OcrGestionar);
        g.MapGet("/estadisticas", Estadisticas).RequirePermiso(Permisos.OcrVer);
    }

    private static async Task<Guid?> LecturaAsync(UsuarioActual usuario, Guid? empresa, Capacidades capacidades, CancellationToken ct)
    {
        var tenant = usuario.EmpresaLectura(empresa, obligatoria: false);
        if (tenant is { } t && !usuario.EsSuperAdmin)
            await capacidades.ExigirAsync(t, Capacidad.Ocr, ct).ConfigureAwait(false);
        return tenant;
    }

    private static async Task<PaqueteOcr> PaqueteAsync(ComparendosDb db, Guid id, UsuarioActual usuario, bool operar, Capacidades capacidades, CancellationToken ct)
    {
        var tenant = operar ? usuario.EmpresaOperacion() : (Guid?)null;
        var p = await db.PaquetesOcr.FirstOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false);
        if (p is null || (operar ? p.TenantId != tenant : !usuario.EsSuperAdmin && p.TenantId != usuario.EmpresaId))
            throw ReglaException.NoEncontrado("El paquete");
        if (!usuario.EsSuperAdmin)
            await capacidades.ExigirAsync(p.TenantId, Capacidad.Ocr, ct).ConfigureAwait(false);
        return p;
    }

    private static async Task<DocumentoOcr> DocumentoAsync(ComparendosDb db, Guid id, UsuarioActual usuario, bool operar, Capacidades capacidades, CancellationToken ct)
    {
        var tenant = operar ? usuario.EmpresaOperacion() : (Guid?)null;
        var d = await db.DocumentosOcr.FirstOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false);
        if (d is null || (operar ? d.TenantId != tenant : !usuario.EsSuperAdmin && d.TenantId != usuario.EmpresaId))
            throw ReglaException.NoEncontrado("El documento");
        if (!usuario.EsSuperAdmin)
            await capacidades.ExigirAsync(d.TenantId, Capacidad.Ocr, ct).ConfigureAwait(false);
        return d;
    }

    private static async Task<IResult> Paquetes(Guid? empresa, string? estado, DateOnly? desde, DateOnly? hasta, Guid? creador, UsuarioActual usuario, ComparendosDb db, Capacidades capacidades, CancellationToken ct)
    {
        var tenant = await LecturaAsync(usuario, empresa, capacidades, ct).ConfigureAwait(false);
        var q = db.PaquetesOcr.AsNoTracking().Where(p => tenant == null || p.TenantId == tenant);
        var globales = await q.GroupBy(_ => 1).Select(g => new
        {
            paquetes = g.Count(), documentos = g.Sum(p => p.CantidadDocumentos), pendientes = g.Sum(p => p.CantidadPendientes), completos = g.Sum(p => p.CantidadCompletos),
            incompletos = g.Sum(p => p.CantidadIncompletos), fallidos = g.Sum(p => p.CantidadFallidos), duplicados = g.Sum(p => p.CantidadDuplicados),
            registrados = g.Sum(p => p.CantidadRegistrados), descartados = g.Sum(p => p.CantidadDescartados),
        }).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (Enum.TryParse<EstadoPaqueteOcr>(estado, true, out var e))
            q = q.Where(p => p.Estado == e);
        if (desde is { } d)
            q = q.Where(p => p.CreadoEn >= Inicio(d));
        if (hasta is { } h)
            q = q.Where(p => p.CreadoEn < Inicio(h.AddDays(1)));
        if (creador is { } c)
            q = q.Where(p => p.CreadoPor == c);
        var items = await q.OrderByDescending(p => p.CreadoEn).Take(200).ToListAsync(ct).ConfigureAwait(false);
        return Results.Ok(new { contadores = globales, items = items.Select(Resumen) });
    }

    private static DateTimeOffset Inicio(DateOnly d) => new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue), Domain.Tiempo.ColombiaTime.Offset).ToUniversalTime();

    private static object Resumen(PaqueteOcr p) => new
    {
        p.Id, p.TenantId, p.Nombre, estado = p.Estado.ToString(), p.CantidadDocumentos, p.CantidadPendientes, p.CantidadCompletos, p.CantidadIncompletos, p.CantidadFallidos,
        p.CantidadDuplicados, p.CantidadRegistrados, p.CantidadDescartados, procesados = p.CantidadDocumentos - p.CantidadPendientes, p.CreadoPor, p.CreadoPorNombre, p.CreadoEn,
        p.ProcesamientoIniciadoEn, p.CerradoEn, p.PromptVersionId,
    };

    private static async Task<IResult> Crear(HttpRequest request, UsuarioActual usuario, ServicioOcr servicio, CancellationToken ct)
    {
        var form = await request.ReadFormAsync(ct).ConfigureAwait(false);
        var paquete = await servicio.CrearPaqueteAsync(usuario.EmpresaOperacion(), form["nombre"].ToString(), form.Files.ToList(), usuario.Id, ct, usuario.Nombre).ConfigureAwait(false);
        if (string.Equals(form["iniciar"].ToString(), "true", StringComparison.OrdinalIgnoreCase))
            await servicio.IniciarAsync(paquete, ct).ConfigureAwait(false);
        return Results.Created($"{Program.Prefijo}/ocr/paquetes/{paquete.Id}", Resumen(paquete));
    }

    private static async Task<IResult> Paquete(Guid id, string? estado, UsuarioActual usuario, ComparendosDb db, Capacidades capacidades, CancellationToken ct)
    {
        var p = await PaqueteAsync(db, id, usuario, operar: false, capacidades, ct).ConfigureAwait(false);
        var q = db.DocumentosOcr.AsNoTracking().Where(d => d.PaqueteId == id);
        if (Enum.TryParse<EstadoDocumentoOcr>(estado, true, out var e))
            q = q.Where(d => d.Estado == e);
        var documentos = await q.OrderBy(d => d.Id).Select(d => new
        {
            d.Id, d.NombreArchivo, d.TipoArchivo, d.ArchivoId, estado = d.Estado.ToString(), d.Intentos, d.NumeroComparendoExtraido, d.CamposFaltantes,
            tipoFallo = d.TipoFallo.HasValue ? d.TipoFallo.Value.ToString() : null, d.MensajeError, d.FuenteDuplicado, d.ComparendoId, d.ProcesadoEn,
        }).ToListAsync(ct).ConfigureAwait(false);
        return Results.Ok(new { paquete = Resumen(p), documentos });
    }

    private static async Task<IResult> Iniciar(Guid id, UsuarioActual usuario, ComparendosDb db, ServicioOcr servicio, Capacidades capacidades, CancellationToken ct)
    {
        var p = await PaqueteAsync(db, id, usuario, operar: true, capacidades, ct).ConfigureAwait(false);
        await servicio.IniciarAsync(p, ct).ConfigureAwait(false);
        return Results.Accepted($"{Program.Prefijo}/ocr/paquetes/{id}", Resumen(p));
    }

    private static async Task<IResult> RegistrarCompletos(Guid id, UsuarioActual usuario, ComparendosDb db, ServicioOcr servicio, Capacidades capacidades, CancellationToken ct)
    {
        var p = await PaqueteAsync(db, id, usuario, operar: true, capacidades, ct).ConfigureAwait(false);
        var ids = await db.DocumentosOcr.Where(d => d.PaqueteId == p.Id && d.Estado == EstadoDocumentoOcr.Completo).OrderBy(d => d.Id).Select(d => d.Id).ToListAsync(ct).ConfigureAwait(false);
        var registrados = new List<Registro>();
        var omitidos = new List<object>();
        var actor = Actor.De(usuario);
        foreach (var docId in ids)
        {
            db.ChangeTracker.Clear();
            var doc = await db.DocumentosOcr.FirstAsync(d => d.Id == docId, ct).ConfigureAwait(false);
            try
            {
                registrados.Add(await servicio.RegistrarAsync(doc, actor, ct).ConfigureAwait(false));
            }
            catch (ReglaException ex)
            {
                omitidos.Add(new { documentoId = docId, doc.NombreArchivo, ex.Codigo, motivo = ex.Message });
            }
        }

        return Results.Ok(new { registrados, omitidos, placasPorMonitorear = registrados.Where(r => r.Placa is not null && !r.PlacaYaMonitoreada).Select(r => r.Placa).Distinct() });
    }

    private static async Task<IResult> DescartarFallidos(Guid id, PedidoDescarte pedido, UsuarioActual usuario, ComparendosDb db, ServicioOcr servicio, Capacidades capacidades, CancellationToken ct)
    {
        var p = await PaqueteAsync(db, id, usuario, operar: true, capacidades, ct).ConfigureAwait(false);
        var docs = await db.DocumentosOcr.Where(d => d.PaqueteId == p.Id && d.Estado == EstadoDocumentoOcr.Fallido).ToListAsync(ct).ConfigureAwait(false);
        foreach (var d in docs)
            await servicio.DescartarAsync(d, string.IsNullOrWhiteSpace(pedido.Motivo) ? "Descarte masivo de fallidos" : pedido.Motivo, usuario.Id, ct).ConfigureAwait(false);
        return Results.Ok(new { descartados = docs.Count });
    }

    private static async Task<IResult> Documento(Guid id, UsuarioActual usuario, ComparendosDb db, Capacidades capacidades, CancellationToken ct)
    {
        var d = await DocumentoAsync(db, id, usuario, operar: false, capacidades, ct).ConfigureAwait(false);
        var campos = await db.DocumentoOcrCampos.AsNoTracking().Where(c => c.DocumentoOcrId == id).ToListAsync(ct).ConfigureAwait(false);
        var orden = CamposOcr.Todos.Select((n, i) => (n, i)).ToDictionary(x => x.n, x => x.i);
        var ediciones = await db.DocumentoOcrEdiciones.AsNoTracking().Where(e => e.DocumentoOcrId == id).OrderBy(e => e.EditadoEn)
            .Select(e => new { e.Campo, e.ValorAnterior, e.ValorNuevo, e.UsuarioId, e.UsuarioNombre, e.EditadoEn, e.Motivo }).ToListAsync(ct).ConfigureAwait(false);
        var intentos = await db.DocumentoOcrIntentos.AsNoTracking().Where(i => i.DocumentoOcrId == id).OrderBy(i => i.Numero)
            .Select(i => new { i.Numero, i.Resultado, tipoError = i.TipoError.HasValue ? i.TipoError.Value.ToString() : null, i.MensajeError, i.IntentadoEn, i.ProximoReintentoEn })
            .ToListAsync(ct).ConfigureAwait(false);

        object? duplicado = null;
        if (d.DuplicadoDeComparendoId is { } cid)
        {
            duplicado = await db.Comparendos.AsNoTracking().Where(c => c.Id == cid).Select(c => new
            {
                tipo = "comparendo", c.Id, c.Numero, fuente = d.FuenteDuplicado, registradoEn = c.CreadoEn, estadoGestion = c.EstadoGestion.ToString(), c.Activo, c.Pagado,
            }).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        }
        else if (d.DuplicadoDeDocumentoId is { } did)
        {
            duplicado = await (from o in db.DocumentosOcr.AsNoTracking()
                               join p in db.PaquetesOcr.AsNoTracking() on o.PaqueteId equals p.Id
                               where o.Id == did
                               select new { tipo = "documento", o.Id, numero = o.NumeroComparendoExtraido, fuente = d.FuenteDuplicado, paqueteId = p.Id, paquete = p.Nombre, registradoEn = p.CreadoEn, estado = o.Estado.ToString(), o.ComparendoId })
                .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        }

        return Results.Ok(new
        {
            d.Id, d.PaqueteId, d.NombreArchivo, d.TipoArchivo, d.ArchivoId, d.ArchivoOriginalId, estado = d.Estado.ToString(), d.Intentos,
            // El proveedor y el modelo son información interna de FLIT: la gestora nunca los ve.
            proveedorModelo = usuario.EsSuperAdmin ? d.ProveedorModelo : null, d.NumeroComparendoExtraido,
            d.CamposFaltantes, tipoFallo = d.TipoFallo?.ToString(), d.MensajeError, reintentable = d.Estado == EstadoDocumentoOcr.Fallido && ResultadoOcr.EsTecnico(d.TipoFallo),
            d.ComparendoId, d.ProcesadoEn, d.RevisadoPor, d.RevisadoEn, d.MotivoDescarte, d.FuenteDuplicado, duplicado,
            campos = campos.OrderBy(c => orden.GetValueOrDefault(c.NombreCampo, 99)).Select(c => new
            {
                c.NombreCampo, obligatorio = CamposOcr.Obligatorios.Contains(c.NombreCampo), c.ValorExtraido, c.Confianza, c.EsNull, c.ValorCorregido, c.ValorFinal,
            }),
            ediciones, historialIntentos = intentos,
        });
    }

    private static async Task<IResult> Corregir(Guid id, PedidoCorreccion pedido, UsuarioActual usuario, ComparendosDb db, ServicioOcr servicio, Capacidades capacidades, CancellationToken ct)
    {
        var d = await DocumentoAsync(db, id, usuario, operar: true, capacidades, ct).ConfigureAwait(false);
        await servicio.CorregirAsync(d, pedido.Campos ?? [], pedido.Motivo, usuario.Id, ct, usuario.Nombre).ConfigureAwait(false);
        return Results.Ok(new { d.Id, estado = d.Estado.ToString(), d.CamposFaltantes, d.FuenteDuplicado });
    }

    private static async Task<IResult> Registrar(Guid id, UsuarioActual usuario, ComparendosDb db, ServicioOcr servicio, Capacidades capacidades, CancellationToken ct)
    {
        var d = await DocumentoAsync(db, id, usuario, operar: true, capacidades, ct).ConfigureAwait(false);
        return Results.Ok(await servicio.RegistrarAsync(d, Actor.De(usuario), ct).ConfigureAwait(false));
    }

    private static async Task<IResult> Descartar(Guid id, PedidoDescarte pedido, UsuarioActual usuario, ComparendosDb db, ServicioOcr servicio, Capacidades capacidades, CancellationToken ct)
    {
        var d = await DocumentoAsync(db, id, usuario, operar: true, capacidades, ct).ConfigureAwait(false);
        await servicio.DescartarAsync(d, pedido.Motivo, usuario.Id, ct).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static async Task<IResult> Reintentar(Guid id, UsuarioActual usuario, ComparendosDb db, ServicioOcr servicio, Capacidades capacidades, CancellationToken ct)
    {
        var d = await DocumentoAsync(db, id, usuario, operar: true, capacidades, ct).ConfigureAwait(false);
        await servicio.ReintentarAsync(d, ct).ConfigureAwait(false);
        return Results.Accepted();
    }

    /// <summary>Estadísticas de éxito por mes (§7.6): documentos procesados por estado de salida.</summary>
    private static async Task<IResult> Estadisticas(Guid? empresa, DateOnly? desde, DateOnly? hasta, UsuarioActual usuario, ComparendosDb db, Capacidades capacidades, CancellationToken ct)
    {
        var tenant = await LecturaAsync(usuario, empresa, capacidades, ct).ConfigureAwait(false);
        var q = db.DocumentosOcr.AsNoTracking().Where(d => (tenant == null || d.TenantId == tenant) && d.ProcesadoEn != null);
        if (desde is { } de)
            q = q.Where(d => d.ProcesadoEn >= Inicio(de));
        if (hasta is { } h)
            q = q.Where(d => d.ProcesadoEn < Inicio(h.AddDays(1)));
        var filas = await q.Select(d => new { d.ProcesadoEn, d.Estado, d.ComparendoId, d.Id }).ToListAsync(ct).ConfigureAwait(false);
        var complementados = await db.Comparendos.AsNoTracking().Where(c => (tenant == null || c.TenantId == tenant) && c.TipoIngreso == TipoIngreso.OcrComplemento)
            .Select(c => c.Id).ToListAsync(ct).ConfigureAwait(false);
        var comp = complementados.ToHashSet();
        var periodos = filas.GroupBy(f => Domain.Tiempo.ColombiaTime.From(f.ProcesadoEn!.Value).ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture))
            .OrderBy(g => g.Key)
            .Select(g => new
            {
                periodo = g.Key,
                procesados = g.Count(),
                // Lo que salió completo del LLM incluye lo que luego se registró como tipo 2; lo complementado (tipo 3) salió incompleto.
                completos = g.Count(f => f.Estado == EstadoDocumentoOcr.Completo || (f.Estado == EstadoDocumentoOcr.Registrado && !comp.Contains(f.ComparendoId ?? Guid.Empty))),
                incompletos = g.Count(f => f.Estado == EstadoDocumentoOcr.Incompleto || (f.Estado == EstadoDocumentoOcr.Registrado && comp.Contains(f.ComparendoId ?? Guid.Empty))),
                fallidos = g.Count(f => f.Estado == EstadoDocumentoOcr.Fallido),
                duplicados = g.Count(f => f.Estado == EstadoDocumentoOcr.Duplicado),
                descartados = g.Count(f => f.Estado == EstadoDocumentoOcr.Descartado),
            });
        return Results.Ok(periodos);
    }
}
