using Flit.Comparendos.Api.Infraestructura.Excel;
using Flit.Comparendos.Api.Modulos.Catalogos;
using Flit.Comparendos.Api.Modulos.Comun;
using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Persistence.Modelo;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Modelo;
using Flit.Comparendos.Domain.Tiempo;
using Microsoft.EntityFrameworkCore;
using Cell = Flit.Comparendos.Api.Infraestructura.Excel.TabularWorkbookWriter.Cell;

namespace Flit.Comparendos.Api.Modulos.Ingesta;

/// <summary>
/// Configuración de alertas de integración y log de migración (HU #13462). Solo SuperAdmin (RN-042): el log por empresa
/// y clave con filtros por empresa, NIT o placa, canal, fechas y resultado, exportable a Excel.
/// </summary>
public static class AlertasYLogEndpoints
{
    public sealed record ConfigAlertas(bool AlertasHabilitadas, int UmbralFallosCiclo, string PlantillaAsunto, string RemitenteNombre, IReadOnlyList<string>? CorreosInternosReportes = null);

    public sealed record DestinatarioDto(Guid Id, string Correo, string Nombre, string TipoAlerta, bool Activo);

    public sealed record NuevoDestinatario(string Correo, string Nombre, string TipoAlerta);

    public sealed record CambioDestinatario(string? Nombre, string? TipoAlerta, bool? Activo);

    public sealed record LogDto(Guid Id, Guid CicloId, Guid EmpresaId, string? Empresa, string Canal, string? Clave, string? Municipio, DateTimeOffset InicioEn,
        int Obtenidas, int Nuevas, int Actualizadas, int SinCambios, int DuplicadasOtraFuente, int Inactivadas, int Reactivadas, int DpFavorableAutomatico,
        string Resultado, string? MensajeError);

    public static void Map(RouteGroupBuilder api)
    {
        var g = api.MapGroup("/admin").RequireAuthorization(AutorizacionExtensions.PoliticaSuperAdmin);
        g.MapGet("/alertas", ObtenerConfig);
        g.MapPut("/alertas", GuardarConfig);
        g.MapGet("/alertas/destinatarios", ListarDestinatarios);
        g.MapPost("/alertas/destinatarios", CrearDestinatario);
        g.MapPatch("/alertas/destinatarios/{id:guid}", CambiarDestinatario);
        g.MapGet("/log-migracion", ListarLog);
        g.MapGet("/log-migracion/excel", ExportarLog);
    }

    private static async Task<IResult> ObtenerConfig(ComparendosDb db, CancellationToken ct)
    {
        var c = await db.AlertaConfiguraciones.AsNoTracking().FirstAsync(ct).ConfigureAwait(false);
        return Results.Ok(new ConfigAlertas(c.AlertasHabilitadas, c.UmbralFallosCiclo, c.PlantillaAsunto, c.RemitenteNombre, c.CorreosInternosReportes));
    }

    private static async Task<IResult> GuardarConfig(ConfigAlertas pedido, ComparendosDb db, Auditoria auditoria, Reloj reloj, UsuarioActual usuario, CancellationToken ct)
    {
        if (pedido.UmbralFallosCiclo < 0 || string.IsNullOrWhiteSpace(pedido.PlantillaAsunto) || pedido.PlantillaAsunto.Length > 200)
            throw new ReglaException("ALERTAS_INVALIDAS", "El umbral no puede ser negativo y el asunto es obligatorio (hasta 200 caracteres).");
        var internos = pedido.CorreosInternosReportes?.Select(x => x.Trim().ToLowerInvariant()).Where(x => x.Length > 0).Distinct().ToList();
        if (internos is not null && internos.Any(x => !System.Net.Mail.MailAddress.TryCreate(x, out _)))
            throw new ReglaException("CORREO_INVALIDO", "Hay un correo inválido en la lista interna de reportes.");
        var c = await db.AlertaConfiguraciones.FirstAsync(ct).ConfigureAwait(false);
        var cambios = Auditoria.Diferencias(("alertas_habilitadas", c.AlertasHabilitadas, pedido.AlertasHabilitadas), ("umbral_fallos_ciclo", c.UmbralFallosCiclo, pedido.UmbralFallosCiclo),
            ("plantilla_asunto", c.PlantillaAsunto, pedido.PlantillaAsunto.Trim()), ("remitente_nombre", c.RemitenteNombre, pedido.RemitenteNombre.Trim()),
            ("correos_internos_reportes", string.Join(", ", c.CorreosInternosReportes), string.Join(", ", internos ?? c.CorreosInternosReportes)));
        if (cambios.Length > 0)
        {
            (c.AlertasHabilitadas, c.UmbralFallosCiclo, c.PlantillaAsunto, c.RemitenteNombre) = (pedido.AlertasHabilitadas, pedido.UmbralFallosCiclo, pedido.PlantillaAsunto.Trim(), pedido.RemitenteNombre.Trim());
            c.CorreosInternosReportes = internos ?? c.CorreosInternosReportes;
            (c.ActualizadoEn, c.ActualizadoPor) = (reloj.Ahora, usuario.Id);
            auditoria.Registrar(null, "alerta_configuracion", 1, "ACTUALIZAR", cambios);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return Results.Ok(pedido);
    }

    private static DestinatarioDto Dto(AlertaDestinatario d) => new(d.Id, d.Correo, d.Nombre, d.TipoAlerta.ToString(), d.Activo);

    private static async Task<IResult> ListarDestinatarios(ComparendosDb db, CancellationToken ct) =>
        Results.Ok((await db.AlertaDestinatarios.AsNoTracking().OrderBy(d => d.Nombre).ToListAsync(ct).ConfigureAwait(false)).Select(Dto));

    private static async Task<IResult> CrearDestinatario(NuevoDestinatario pedido, ComparendosDb db, Auditoria auditoria, Reloj reloj, UsuarioActual usuario, CancellationToken ct)
    {
        var correo = CatalogosEndpoints.NormalizarCorreos([pedido.Correo]).SingleOrDefault() ?? throw new ReglaException("CORREO_INVALIDO", "Escribe un correo válido.");
        if (!Enum.TryParse<TipoAlertaDestinatario>(pedido.TipoAlerta, true, out var tipo))
            throw new ReglaException("TIPO_ALERTA_INVALIDO", "Elige qué alertas recibe: todas, solo críticas, solo SIMIT, solo municipal o solo OCR.");
        if (await db.AlertaDestinatarios.AnyAsync(d => d.Correo == correo, ct).ConfigureAwait(false))
            throw ReglaException.Conflicto("DESTINATARIO_EXISTE", "Ese correo ya recibe alertas.");

        var d = new AlertaDestinatario { Id = Guid.CreateVersion7(), Correo = correo, Nombre = string.IsNullOrWhiteSpace(pedido.Nombre) ? correo : pedido.Nombre.Trim(), TipoAlerta = tipo, CreadoEn = reloj.Ahora, CreadoPor = usuario.Id };
        db.AlertaDestinatarios.Add(d);
        auditoria.Registrar(null, "alerta_destinatario", d.Id, "CREAR", new CambioCampo("correo", null, correo));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Created($"{Program.Prefijo}/admin/alertas/destinatarios/{d.Id}", Dto(d));
    }

    private static async Task<IResult> CambiarDestinatario(Guid id, CambioDestinatario pedido, ComparendosDb db, Auditoria auditoria, Reloj reloj, UsuarioActual usuario, CancellationToken ct)
    {
        var d = await db.AlertaDestinatarios.FirstOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false) ?? throw ReglaException.NoEncontrado("El destinatario");
        var tipo = pedido.TipoAlerta is null ? d.TipoAlerta
            : Enum.TryParse<TipoAlertaDestinatario>(pedido.TipoAlerta, true, out var t) ? t : throw new ReglaException("TIPO_ALERTA_INVALIDO", "Tipo de alerta inválido.");
        var nombre = string.IsNullOrWhiteSpace(pedido.Nombre) ? d.Nombre : pedido.Nombre.Trim();
        var activo = pedido.Activo ?? d.Activo;
        var cambios = Auditoria.Diferencias(("nombre", d.Nombre, nombre), ("tipo_alerta", d.TipoAlerta, tipo), ("activo", d.Activo, activo));
        if (cambios.Length > 0)
        {
            (d.Nombre, d.TipoAlerta, d.Activo, d.ActualizadoEn, d.ActualizadoPor) = (nombre, tipo, activo, reloj.Ahora, usuario.Id);
            auditoria.Registrar(null, "alerta_destinatario", d.Id, "ACTUALIZAR", cambios);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return Results.Ok(Dto(d));
    }

    private static IQueryable<LogMigracion> Filtrar(ComparendosDb db, Guid? empresa, string? clave, string? canal, string? resultado, Guid? ciclo, DateOnly? desde, DateOnly? hasta)
    {
        var q = db.LogMigracion.AsNoTracking();
        if (empresa is { } e)
            q = q.Where(l => l.TenantId == e);
        if (ciclo is { } c)
            q = q.Where(l => l.CicloId == c);
        if (!string.IsNullOrWhiteSpace(clave))
            q = q.Where(l => l.Clave != null && EF.Functions.ILike(l.Clave, $"%{clave.Trim()}%"));
        if (Enum.TryParse<CanalIngesta>(canal, true, out var ca))
            q = q.Where(l => l.Canal == ca);
        if (Enum.TryParse<ResultadoCiclo>(resultado, true, out var r))
            q = q.Where(l => l.Resultado == r);
        if (desde is { } d)
            q = q.Where(l => l.InicioEn >= new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue), ColombiaTime.Offset).ToUniversalTime());
        if (hasta is { } h)
            q = q.Where(l => l.InicioEn < new DateTimeOffset(h.AddDays(1).ToDateTime(TimeOnly.MinValue), ColombiaTime.Offset).ToUniversalTime());
        return q;
    }

    private static async Task<List<LogDto>> Leer(ComparendosDb db, IQueryable<LogMigracion> q, CancellationToken ct)
    {
        var empresas = await db.ConfiguracionesEmpresa.AsNoTracking().ToDictionaryAsync(e => e.TenantId, e => e.EmpresaNombre, ct).ConfigureAwait(false);
        var filas = await q.ToListAsync(ct).ConfigureAwait(false);
        return filas.Select(l => new LogDto(l.Id, l.CicloId, l.TenantId, empresas.GetValueOrDefault(l.TenantId), l.Canal.ToString(), l.Clave, l.MunicipioCodigo, l.InicioEn,
            l.CantidadObtenidas, l.CantidadNuevas, l.CantidadActualizadas, l.CantidadSinCambios, l.CantidadDuplicadasOtraFuente, l.CantidadInactivadas,
            l.CantidadReactivadas, l.CantidadDpFavorableAutomatico, l.Resultado.ToString(), l.MensajeError)).ToList();
    }

    private static async Task<IResult> ListarLog(Guid? empresa, string? clave, string? canal, string? resultado, Guid? ciclo, DateOnly? desde, DateOnly? hasta, int? pagina, int? tamano, ComparendosDb db, CancellationToken ct)
    {
        var (p, t) = Paginacion.Normalizar(pagina, tamano, 50);
        var q = Filtrar(db, empresa, clave, canal, resultado, ciclo, desde, hasta);
        var total = await q.CountAsync(ct).ConfigureAwait(false);
        var items = await Leer(db, q.OrderByDescending(l => l.InicioEn).Skip((p - 1) * t).Take(t), ct).ConfigureAwait(false);
        return Results.Ok(new Pagina<LogDto>(items, total, p, t));
    }

    private static async Task<IResult> ExportarLog(Guid? empresa, string? clave, string? canal, string? resultado, Guid? ciclo, DateOnly? desde, DateOnly? hasta, ComparendosDb db, Reloj reloj, CancellationToken ct)
    {
        var filas = await Leer(db, Filtrar(db, empresa, clave, canal, resultado, ciclo, desde, hasta).OrderByDescending(l => l.InicioEn).Take(50_000), ct).ConfigureAwait(false);
        string[] encabezados = ["Fecha", "Empresa", "Canal", "Clave", "Municipio", "Obtenidos", "Nuevos", "Actualizados", "Sin cambios", "De otra fuente", "Inactivados", "Reactivados", "DP favorables", "Resultado", "Error"];
        var hoja = new TabularWorkbookWriter.Sheet("Log de migración", encabezados.Select(e => new TabularWorkbookWriter.Column(e, e is "Error" ? 60 : e is "Empresa" ? 30 : 14)).ToList(),
            filas.Select(l =>
            {
                var local = ColombiaTime.From(l.InicioEn);
                return (IReadOnlyList<Cell>)
                [
                    Cell.OfDateTime(DateOnly.FromDateTime(local.DateTime), local.Hour, local.Minute), Cell.Of(l.Empresa ?? l.EmpresaId.ToString()), Cell.Of(l.Canal),
                    Cell.Of(l.Clave), Cell.Of(l.Municipio), Cell.Of(l.Obtenidas), Cell.Of(l.Nuevas), Cell.Of(l.Actualizadas), Cell.Of(l.SinCambios),
                    Cell.Of(l.DuplicadasOtraFuente), Cell.Of(l.Inactivadas), Cell.Of(l.Reactivadas), Cell.Of(l.DpFavorableAutomatico), Cell.Of(l.Resultado), Cell.Of(l.MensajeError),
                ];
            }).ToList());
        var bytes = TabularWorkbookWriter.Write([hoja]);
        return Results.File(bytes, TabularWorkbookWriter.ContentType, $"log-migracion-{reloj.Hoy:yyyyMMdd}.xlsx");
    }
}
