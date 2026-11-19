using Flit.Comparendos.Api.Modulos.Configuracion;
using Flit.Comparendos.Domain.Modelo;
using Microsoft.EntityFrameworkCore;

namespace Flit.Comparendos.Api.Modulos.Reportes;

public sealed record SaludEmpresas(int Total, int Activas, int Pendientes, int Inactivas);

public sealed record SaludIngesta(int Consultas, int ConError, int EmpresasConError);

public sealed record SaludDp(int Enviados, int Favorables);

public sealed record EmpresaQueNecesita(Guid EmpresaId, string Nombre, IReadOnlyList<string> Motivos);

public sealed record ResumenPlataforma(
    DateOnly Hoy,
    SaludEmpresas Empresas,
    SaludIngesta Ingesta24h,
    int CorreosFallidos7d,
    DateOnly CorreosDesde,
    int OcrPorRevisar,
    SaludDp DpDelMes,
    IReadOnlyList<EmpresaQueNecesita> NecesitanAlgo);

/// <summary>
/// Inicio del SuperAdmin (rediseño R2): salud de la plataforma entre todas las empresas y las que necesitan algo. Solo
/// lectura y sin N+1: una consulta agrupada (o un DISTINCT tenant) por tabla, nunca una subconsulta por empresa.
/// </summary>
public sealed partial class ServicioReportes
{
    /// <summary>Lo que el SuperAdmin tiene que revisar a mano en OCR: leído pero sin registrar.</summary>
    private static readonly EstadoDocumentoOcr[] OcrPorRevisar = [EstadoDocumentoOcr.Completo, EstadoDocumentoOcr.Incompleto, EstadoDocumentoOcr.Duplicado];

    public async Task<ResumenPlataforma> ResumenPlataformaAsync(CancellationToken ct)
    {
        var ahora = reloj.Ahora;
        var hoy = reloj.Hoy;

        var empresas = await db.ConfiguracionesEmpresa.AsNoTracking()
            .Select(c => new { c.TenantId, c.EmpresaNombre, c.HasRightPetitionModule, c.AllowsContravenerChange, c.CorreoRespuesta })
            .ToListAsync(ct).ConfigureAwait(false);
        var conMonitoreo = (await db.IdentificacionesMonitoreadas.AsNoTracking().Where(i => i.Activo).Select(i => i.TenantId).Distinct().ToListAsync(ct).ConfigureAwait(false))
            .Concat(await db.PlacasMonitoreadas.AsNoTracking().Where(p => p.Activo).Select(p => p.TenantId).Distinct().ToListAsync(ct).ConfigureAwait(false))
            .ToHashSet();
        var conRepresentante = (await db.RepresentantesLegales.AsNoTracking().Where(r => r.Activo).Select(r => r.TenantId).Distinct().ToListAsync(ct).ConfigureAwait(false)).ToHashSet();
        var conTipologias = (await db.TipologiasEmpresa.AsNoTracking().Where(t => t.Habilitada).Select(t => t.TenantId).Distinct().ToListAsync(ct).ConfigureAwait(false)).ToHashSet();
        var consultas = await db.ConfiguracionesConsultas.AsNoTracking().GroupBy(k => k.TenantId)
            .Select(g => new { Tenant = g.Key, Estado = g.Select(k => k.Estado).First() }).ToDictionaryAsync(x => x.Tenant, x => (EstadoConfiguracionConsultas?)x.Estado, ct).ConfigureAwait(false);

        var estados = new List<string>();
        var necesitan = new List<EmpresaQueNecesita>();
        foreach (var e in empresas)
        {
            var estadoConsultas = consultas.GetValueOrDefault(e.TenantId);
            var faltantes = FaltantesEmpresa.Calcular(conMonitoreo.Contains(e.TenantId), estadoConsultas, e.HasRightPetitionModule, e.AllowsContravenerChange,
                conRepresentante.Contains(e.TenantId), conTipologias.Contains(e.TenantId));
            estados.Add(FaltantesEmpresa.Estado(estadoConsultas, faltantes));
            if (FaltantesEmpresa.FaltaCorreoRespuesta(e.HasRightPetitionModule, e.AllowsContravenerChange, e.CorreoRespuesta))
                faltantes.Add("Sin correo para respuestas");
            if (faltantes.Count > 0)
                necesitan.Add(new EmpresaQueNecesita(e.TenantId, e.EmpresaNombre ?? e.TenantId.ToString(), faltantes));
        }

        var hace24h = ahora.AddHours(-24);
        var ingesta = await db.LogMigracion.AsNoTracking().Where(l => l.InicioEn >= hace24h).GroupBy(_ => 1).Select(g => new
        {
            N = g.Count(),
            Error = g.Count(l => l.Resultado == ResultadoCiclo.Fallido),
            Empresas = g.Where(l => l.Resultado == ResultadoCiclo.Fallido).Select(l => l.TenantId).Distinct().Count(),
        }).FirstOrDefaultAsync(ct).ConfigureAwait(false);

        // Por día de Colombia, como filtra /mensajes?desde=: así la cifra es la que muestra la lista al hacer clic.
        var correosDesde = hoy.AddDays(-7);
        var hace7d = Alcance.Utc(correosDesde);
        var fallidos = await db.Comunicaciones.AsNoTracking()
            .CountAsync(c => c.CreadoEn >= hace7d && (c.Estado == EstadoComunicacion.Fallido || c.Estado == EstadoComunicacion.Rebotado), ct).ConfigureAwait(false);
        var ocr = await db.DocumentosOcr.AsNoTracking().CountAsync(d => OcrPorRevisar.Contains(d.Estado), ct).ConfigureAwait(false);

        var inicioMes = new DateOnly(hoy.Year, hoy.Month, 1);
        var dp = await db.DerechosPeticion.AsNoTracking().GroupBy(_ => 1).Select(g => new
        {
            Enviados = g.Count(d => (d.Estado == EstadoDp.Enviado || d.Estado == EstadoDp.Respondido) && d.FechaRadicacion >= inicioMes),
            Favorables = g.Count(d => d.Estado == EstadoDp.Respondido && d.RespuestaSentido == SentidoRespuestaOt.Favorable && d.RespuestaFecha >= inicioMes),
        }).FirstOrDefaultAsync(ct).ConfigureAwait(false);

        return new ResumenPlataforma(
            hoy,
            new SaludEmpresas(empresas.Count, estados.Count(s => s == "Activa"), estados.Count(s => s == "Pendiente"), estados.Count(s => s == "Inactiva")),
            new SaludIngesta(ingesta?.N ?? 0, ingesta?.Error ?? 0, ingesta?.Empresas ?? 0),
            fallidos,
            correosDesde,
            ocr,
            new SaludDp(dp?.Enviados ?? 0, dp?.Favorables ?? 0),
            necesitan.OrderByDescending(n => n.Motivos.Count).ThenBy(n => n.Nombre, StringComparer.Ordinal).ToList());
    }
}
