using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Plataforma;
using Microsoft.EntityFrameworkCore;

namespace Flit.Comparendos.Api.Modulos.Comun;

/// <summary>
/// Historial de cambios de configuración (RN-010; §7.12, §7.19, §7.23, §7.25). La gestora ve el de su empresa (NITs,
/// placas, representantes, correo…); el SuperAdmin ve el de cualquier empresa y el global (calendario, trabajos,
/// scheduler, organismos, tipologías, plantillas, prompts).
/// </summary>
public static class AuditoriaEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/auditoria", DeLaEmpresa).RequirePermiso(Permisos.ConfiguracionVer);
        api.MapGet("/admin/auditoria", Global).RequireAuthorization(AutorizacionExtensions.PoliticaSuperAdmin);
    }

    private static IQueryable<Persistence.Modelo.AuditoriaConfiguracion> Filtrar(IQueryable<Persistence.Modelo.AuditoriaConfiguracion> q, string? entidad, string? entidadId, DateOnly? desde, DateOnly? hasta)
    {
        if (!string.IsNullOrWhiteSpace(entidad))
            q = q.Where(a => a.Entidad == entidad);
        if (!string.IsNullOrWhiteSpace(entidadId))
            q = q.Where(a => a.EntidadId == entidadId);
        if (desde is { } d)
        {
            var i = Reportes.Alcance.Utc(d);
            q = q.Where(a => a.RegistradoEn >= i);
        }

        if (hasta is { } h)
        {
            var f = Reportes.Alcance.Utc(h.AddDays(1));
            q = q.Where(a => a.RegistradoEn < f);
        }

        return q;
    }

    private static async Task<IResult> Listar(IQueryable<Persistence.Modelo.AuditoriaConfiguracion> q, int? pagina, int? tamano, CancellationToken ct)
    {
        var (p, t) = (Math.Max(pagina ?? 1, 1), Math.Clamp(tamano ?? 50, 1, 200));
        var total = await q.CountAsync(ct).ConfigureAwait(false);
        var items = await q.OrderByDescending(a => a.RegistradoEn).Skip((p - 1) * t).Take(t)
            .Select(a => new { a.Id, a.TenantId, a.Entidad, a.EntidadId, a.Accion, a.Cambios, a.UsuarioNombre, a.RegistradoEn }).ToListAsync(ct).ConfigureAwait(false);
        return Results.Ok(new { total, pagina = p, tamano = t, items });
    }

    private static Task<IResult> DeLaEmpresa(string? entidad, string? entidadId, DateOnly? desde, DateOnly? hasta, int? pagina, int? tamano, Guid? empresa, UsuarioActual usuario, ComparendosDb db, CancellationToken ct)
    {
        var tenant = usuario.EmpresaLectura(empresa, obligatoria: true);
        return Listar(Filtrar(db.AuditoriasConfiguracion.AsNoTracking().Where(a => a.TenantId == tenant), entidad, entidadId, desde, hasta), pagina, tamano, ct);
    }

    /// <summary><c>empresa</c> vacía y <c>global=true</c>: solo lo global; con empresa: esa empresa; sin nada: todo.</summary>
    private static Task<IResult> Global(string? entidad, string? entidadId, DateOnly? desde, DateOnly? hasta, int? pagina, int? tamano, Guid? empresa, bool? global, ComparendosDb db, CancellationToken ct)
    {
        var q = db.AuditoriasConfiguracion.AsNoTracking();
        if (empresa is { } e)
            q = q.Where(a => a.TenantId == e);
        else if (global == true)
            q = q.Where(a => a.TenantId == null);
        return Listar(Filtrar(q, entidad, entidadId, desde, hasta), pagina, tamano, ct);
    }
}
