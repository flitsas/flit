using System.Text;
using Flit.Comparendos.Api.Modulos.Comun;
using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Modelo;
using Flit.Comparendos.Domain.Tiempo;
using Microsoft.EntityFrameworkCore;

namespace Flit.Comparendos.Api.Modulos.Correo;

/// <summary>
/// Centro de Mensajes (HU #13484, §7.7): todo correo que salió o intentó salir (RN-121), con su estado, intentos,
/// adjuntos y la apertura como dato informativo [Excel C12]. El SuperAdmin ve todas las empresas y las alertas internas.
/// </summary>
public static class MensajesEndpoints
{
    public sealed record MensajeFila(Guid Id, Guid? EmpresaId, Guid? ComparendoId, string? Comparendo, string Tipo, string Canal, string? CanalCorreo, string Destinatarios,
        string Asunto, string Estado, int Reintentos, DateTimeOffset? AbiertoEn, int Aperturas, bool Desviado, DateTimeOffset CreadoEn, DateTimeOffset? EnviadoEn, string? MotivoFallo);

    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/mensajes", Listar).RequirePermiso(Permisos.MensajesVer);
        api.MapGet("/mensajes/{id:guid}", Detalle).RequirePermiso(Permisos.MensajesVer);
        api.MapPost("/mensajes/{id:guid}/reintentar", Reintentar).RequirePermiso(Permisos.AvisosEnviar);
    }

    private static async Task<IResult> Listar(Guid? empresa, bool? internas, string? tipo, string? estado, DateOnly? desde, DateOnly? hasta, string? comparendo, string? placa,
        int? pagina, int? tamano, UsuarioActual usuario, ComparendosDb db, CancellationToken ct)
    {
        var (p, t) = Paginacion.Normalizar(pagina, tamano, 50);
        var q = db.Comunicaciones.AsNoTracking();
        if (usuario.EsSuperAdmin)
        {
            if (internas == true)
                q = q.Where(c => c.TenantId == null);
            else if (empresa is { } e)
                q = q.Where(c => c.TenantId == e);
        }
        else
        {
            var propia = usuario.EmpresaLectura(empresa);
            q = q.Where(c => c.TenantId == propia);
        }

        var tipos = (tipo ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => Enum.TryParse<TipoComunicacion>(x, true, out var v) ? (TipoComunicacion?)v : null).OfType<TipoComunicacion>().ToList();
        if (tipos.Count > 0)
            q = q.Where(c => tipos.Contains(c.Tipo));
        var estados = (estado ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => Enum.TryParse<EstadoComunicacion>(x, true, out var v) ? (EstadoComunicacion?)v : null).OfType<EstadoComunicacion>().ToList();
        if (estados.Count > 0)
            q = q.Where(c => estados.Contains(c.Estado));
        if (desde is { } d)
        {
            var inicio = new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue), ColombiaTime.Offset).ToUniversalTime();
            q = q.Where(c => c.CreadoEn >= inicio);
        }

        if (hasta is { } h)
        {
            var fin = new DateTimeOffset(h.AddDays(1).ToDateTime(TimeOnly.MinValue), ColombiaTime.Offset).ToUniversalTime();
            q = q.Where(c => c.CreadoEn < fin);
        }

        if (!string.IsNullOrWhiteSpace(comparendo) || !string.IsNullOrWhiteSpace(placa))
        {
            var numero = Domain.Identificadores.NumeroComparendo.Normalizar(comparendo);
            var laPlaca = Domain.Identificadores.Placa.Normalizar(placa);
            q = q.Where(c => db.Comparendos.Any(x => x.Id == c.ComparendoId && (numero == null || x.NumeroNormalizado.Contains(numero)) && (laPlaca == null || x.Placa == laPlaca)));
        }

        var total = await q.CountAsync(ct).ConfigureAwait(false);
        var filas = await q.OrderByDescending(c => c.CreadoEn).Skip((p - 1) * t).Take(t)
            .Select(c => new MensajeFila(c.Id, c.TenantId, c.ComparendoId, db.Comparendos.Where(x => x.Id == c.ComparendoId).Select(x => x.Numero).FirstOrDefault(), c.Tipo.ToString(),
                c.Canal.ToString(), c.CanalCorreo.ToString(), c.Destinatarios, c.Asunto, c.Estado.ToString(), c.CantidadReintentos, c.AbiertoEn, c.Aperturas, c.Desviado, c.CreadoEn,
                c.EnviadoEn, c.MotivoFallo))
            .ToListAsync(ct).ConfigureAwait(false);
        return Results.Ok(new Pagina<MensajeFila>(filas, total, p, t));
    }

    private static async Task<IResult> Detalle(Guid id, UsuarioActual usuario, ComparendosDb db, RegistroArchivos archivos, CancellationToken ct)
    {
        var c = await db.Comunicaciones.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false);
        if (c is null || (!usuario.EsSuperAdmin && c.TenantId != usuario.EmpresaId))
            throw ReglaException.NoEncontrado("El mensaje");
        var html = await archivos.LeerAsync(c.CuerpoHtmlArchivoId, ct).ConfigureAwait(false);
        var adjuntos = await (from a in db.ComunicacionAdjuntos.AsNoTracking()
                              join f in db.Archivos.AsNoTracking() on a.ArchivoId equals f.Id
                              where a.ComunicacionId == id
                              select new { a.ArchivoId, a.Nombre, f.ContentType, f.TamanoBytes }).ToListAsync(ct).ConfigureAwait(false);
        var intentos = await db.ComunicacionIntentos.AsNoTracking().Where(i => i.ComunicacionId == id).OrderBy(i => i.Numero)
            .Select(i => new { i.Numero, i.ResultadoPlataforma, i.Mensaje, i.DuracionMs, i.IntentadoEn }).ToListAsync(ct).ConfigureAwait(false);
        var plantilla = c.PlantillaVersionId is { } vid
            ? await db.PlantillaVersiones.AsNoTracking().Where(v => v.Id == vid).Select(v => new { v.PlantillaId, v.NumeroVersion }).FirstOrDefaultAsync(ct).ConfigureAwait(false)
            : null;
        return Results.Ok(new
        {
            c.Id,
            empresaId = c.TenantId,
            c.ComparendoId,
            tipo = c.Tipo.ToString(),
            canal = c.Canal.ToString(),
            canalCorreo = c.CanalCorreo.ToString(),
            c.RemitenteNombre,
            c.Destinatarios,
            c.Asunto,
            html = html is { } h ? Encoding.UTF8.GetString(h.Contenido) : null,
            estado = c.Estado.ToString(),
            c.CantidadReintentos,
            c.ProximoIntentoEn,
            c.ResultadoPlataforma,
            c.EntregaIdPlataforma,
            c.MotivoFallo,
            c.Desviado,
            c.AbiertoEn,
            c.Aperturas,
            avisoApertura = "Dato informativo: no constituye prueba de notificación.",
            c.CreadoEn,
            c.EnviadoEn,
            plantilla,
            c.PlantillaClave,
            adjuntos,
            intentos,
        });
    }

    /// <summary>Vuelve a poner en cola un correo FALLIDO (no un rebote: el destinatario no existe y hay que corregirlo).</summary>
    private static async Task<IResult> Reintentar(Guid id, UsuarioActual usuario, ComparendosDb db, DespachadorCorreos despachador, CancellationToken ct)
    {
        var tenant = usuario.EmpresaOperacion();
        var c = await db.Comunicaciones.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenant, ct).ConfigureAwait(false) ?? throw ReglaException.NoEncontrado("El mensaje");
        if (c.Estado != EstadoComunicacion.Fallido)
            throw ReglaException.Conflicto("NO_REINTENTABLE", c.Estado == EstadoComunicacion.Rebotado ? "El correo rebotó: corrige el destinatario y vuelve a enviarlo desde el comparendo." : "Solo se reintenta un correo fallido.");
        (c.Estado, c.CantidadReintentos, c.ProximoIntentoEn, c.MotivoFallo) = (EstadoComunicacion.Pendiente, 0, null, null);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        var estado = await despachador.DespacharAsync(id, ct).ConfigureAwait(false);
        return Results.Ok(new { estado = estado.ToString() });
    }
}
