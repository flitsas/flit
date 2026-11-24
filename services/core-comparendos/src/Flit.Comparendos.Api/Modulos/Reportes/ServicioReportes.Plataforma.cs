using Flit.Comparendos.Domain.Modelo;
using Flit.Comparendos.Domain.Tiempo;
using Microsoft.EntityFrameworkCore;

namespace Flit.Comparendos.Api.Modulos.Reportes;

/// <summary>Un día de una fuente: consultas (una por clave y empresa), las que fallaron, comparendos obtenidos y nuevos.</summary>
public sealed record PuntoFuente(DateOnly Dia, string Fuente, int Consultas, int ConError, int Obtenidos, int Nuevos);

public sealed record TotalFuente(string Fuente, int Consultas, int ConError, int Obtenidos, int Nuevos);

/// <summary>Errores técnicos de las fuentes por tipo (solo con «todas las empresas»: la consulta técnica no guarda la empresa).</summary>
public sealed record ErrorFuente(string Fuente, string Tipo, int Cantidad);

public sealed record MotivoCorreo(string Motivo, int Cantidad);

public sealed record CorreoFallido(Guid Id, Guid? EmpresaId, string Empresa, string Plantilla, string Asunto, string Estado, string? Motivo, DateTimeOffset CreadoEn);

public sealed record CorreosPlataforma(int Fallidos, int Rebotados, IReadOnlyList<MotivoCorreo> Motivos, IReadOnlyList<CorreoFallido> Recientes);

public sealed record EmpresaConErrores(Guid EmpresaId, string Empresa, int ConsultasConError, int CorreosFallidos);

public sealed record ReportePlataforma(
    DateOnly Desde,
    DateOnly Hasta,
    IReadOnlyList<TotalFuente> Fuentes,
    IReadOnlyList<PuntoFuente> PorDia,
    IReadOnlyList<ErrorFuente>? ErroresPorTipo,
    CorreosPlataforma Correos,
    IReadOnlyList<EmpresaConErrores> EmpresasConMasErrores);

/// <summary>
/// Pestaña Plataforma (solo SuperAdmin; V1: «Log de migración» y la estadística diaria). Es el ÚNICO lugar donde se ven
/// la fuente y el error técnico (R3): este servicio solo se llama desde <c>/admin/reportes/plataforma</c>.
/// </summary>
public sealed partial class ServicioReportes
{
    public const int MaxDiasPlataforma = 366;


    public async Task<ReportePlataforma> PlataformaAsync(Alcance a, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(a);
        var (i, f, t) = (a.InicioUtc, a.FinUtc, a.Tenant);

        // log_migracion: una fila por clave consultada, ciclo y empresa (millones en un año de Renting). Se agrupa EN LA BASE
        // por día de Colombia, fuente y empresa (REX): a la memoria llegan cientos de filas, no millones. Colombia no tiene
        // horario de verano: el día es la fecha UTC menos 5 horas (ColombiaTime.Offset).
        var grupos = await db.LogMigracion.AsNoTracking().Where(l => (t == null || l.TenantId == t) && l.InicioEn >= i && l.InicioEn < f)
            .GroupBy(l => new { Dia = l.InicioEn.AddHours(ColombiaTime.Offset.TotalHours).Date, l.Canal, l.TenantId })
            .Select(g => new
            {
                g.Key.Dia, g.Key.Canal, g.Key.TenantId, N = g.Count(), Error = g.Count(l => l.Resultado == ResultadoCiclo.Fallido),
                Obtenidos = g.Sum(l => l.CantidadObtenidas), Nuevos = g.Sum(l => l.CantidadNuevas),
            })
            .ToListAsync(ct).ConfigureAwait(false);
        var porDia = grupos.GroupBy(g => (Dia: DateOnly.FromDateTime(g.Dia), g.Canal))
            .Select(g => new PuntoFuente(g.Key.Dia, g.Key.Canal.ToString(), g.Sum(x => x.N), g.Sum(x => x.Error), g.Sum(x => x.Obtenidos), g.Sum(x => x.Nuevos)))
            .OrderBy(p => p.Dia).ThenBy(p => p.Fuente, StringComparer.Ordinal).ToList();
        var fuentes = porDia.GroupBy(p => p.Fuente)
            .Select(g => new TotalFuente(g.Key, g.Sum(p => p.Consultas), g.Sum(p => p.ConError), g.Sum(p => p.Obtenidos), g.Sum(p => p.Nuevos)))
            .OrderByDescending(x => x.Consultas).ThenBy(x => x.Fuente, StringComparer.Ordinal).ToList();

        List<ErrorFuente>? errores = null;
        if (t is null)
        {
            errores = (await db.ConsultasFuente.AsNoTracking().Where(c => c.Resultado == ResultadoConsulta.Error && c.ConsultadoEn >= i && c.ConsultadoEn < f)
                    .GroupBy(c => new { c.Canal, c.TipoError }).Select(g => new { g.Key.Canal, g.Key.TipoError, N = g.Count() })
                    .ToListAsync(ct).ConfigureAwait(false))
                .Select(x => new ErrorFuente(x.Canal.ToString(), x.TipoError?.ToString() ?? "SinTipo", x.N))
                .OrderByDescending(x => x.Cantidad).ThenBy(x => x.Fuente, StringComparer.Ordinal).ThenBy(x => x.Tipo, StringComparer.Ordinal).ToList();
        }

        var correos = db.Comunicaciones.AsNoTracking()
            .Where(c => (t == null || c.TenantId == t) && c.CreadoEn >= i && c.CreadoEn < f && (c.Estado == EstadoComunicacion.Fallido || c.Estado == EstadoComunicacion.Rebotado));
        var porEstado = await correos.GroupBy(c => c.Estado).Select(g => new { Estado = g.Key, N = g.Count() }).ToListAsync(ct).ConfigureAwait(false);
        var motivos = (await correos.GroupBy(c => c.MotivoFallo).Select(g => new { Motivo = g.Key, N = g.Count() })
                .OrderByDescending(x => x.N).ThenBy(x => x.Motivo).Take(10).ToListAsync(ct).ConfigureAwait(false))
            .Select(x => new MotivoCorreo(string.IsNullOrWhiteSpace(x.Motivo) ? "Sin motivo registrado" : x.Motivo, x.N)).ToList();
        var recientes = await correos.OrderByDescending(c => c.CreadoEn).Take(50)
            .Select(c => new { c.Id, c.TenantId, c.PlantillaClave, c.Asunto, c.Estado, c.MotivoFallo, c.CreadoEn }).ToListAsync(ct).ConfigureAwait(false);
        var correosPorEmpresa = await correos.Where(c => c.TenantId != null).GroupBy(c => c.TenantId!.Value)
            .Select(g => new { Tenant = g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Tenant, x => x.N, ct).ConfigureAwait(false);

        var erroresPorEmpresa = grupos.Where(g => g.Error > 0).GroupBy(g => g.TenantId).ToDictionary(g => g.Key, g => g.Sum(x => x.Error));
        var tenants = erroresPorEmpresa.Keys.Concat(correosPorEmpresa.Keys).Concat(recientes.Where(r => r.TenantId != null).Select(r => r.TenantId!.Value)).Distinct().ToList();
        var nombres = await db.ConfiguracionesEmpresa.AsNoTracking().Where(e => tenants.Contains(e.TenantId))
            .ToDictionaryAsync(e => e.TenantId, e => e.EmpresaNombre, ct).ConfigureAwait(false);
        string Nombre(Guid? id) => id is { } x ? nombres.GetValueOrDefault(x) ?? x.ToString() : "Plataforma (sin empresa)";

        var empresas = erroresPorEmpresa.Keys.Concat(correosPorEmpresa.Keys).Distinct()
            .Select(e => new EmpresaConErrores(e, Nombre(e), erroresPorEmpresa.GetValueOrDefault(e), correosPorEmpresa.GetValueOrDefault(e)))
            .OrderByDescending(e => e.ConsultasConError + e.CorreosFallidos).ThenBy(e => e.Empresa, StringComparer.Ordinal).Take(10).ToList();

        return new ReportePlataforma(a.Desde, a.Hasta, fuentes, porDia, errores,
            new CorreosPlataforma(porEstado.Where(x => x.Estado == EstadoComunicacion.Fallido).Sum(x => x.N), porEstado.Where(x => x.Estado == EstadoComunicacion.Rebotado).Sum(x => x.N),
                motivos, recientes.Select(r => new CorreoFallido(r.Id, r.TenantId, Nombre(r.TenantId), r.PlantillaClave, r.Asunto, r.Estado.ToString(), r.MotivoFallo, r.CreadoEn)).ToList()),
            empresas);
    }
}
