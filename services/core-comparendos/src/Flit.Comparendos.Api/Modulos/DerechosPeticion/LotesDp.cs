using Flit.Comparendos.Api.Modulos.Comparendos;
using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Persistence.Modelo;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Modelo;
using Microsoft.EntityFrameworkCore;

namespace Flit.Comparendos.Api.Modulos.DerechosPeticion;

/// <summary>
/// Envío masivo de DP (HU #13489, §6.17): misma tipología (que admita masivo) y mismo OT para todos, de 1 a 500
/// comparendos (S-62). Se procesa en segundo plano de uno en uno: lo que no pasa un bloqueo queda OMITIDO con su
/// motivo (p. ej. «ya tiene DP», Excel J4) y un fallo individual no detiene el lote.
/// </summary>
public static class LotesDpEndpoints
{
    public sealed record PedidoLote(Guid TipologiaId, IReadOnlyList<Guid> ComparendoIds);

    public static void Map(RouteGroupBuilder api)
    {
        api.MapPost("/dp/lotes", Crear).RequirePermiso(Permisos.DpEnviarMasivo);
        api.MapGet("/dp/lotes", Listar).RequirePermiso(Permisos.DpEnviarMasivo);
        api.MapGet("/dp/lotes/{id:guid}", Detalle).RequirePermiso(Permisos.DpEnviarMasivo);
    }

    private static async Task<IResult> Crear(PedidoLote pedido, UsuarioActual usuario, ComparendosDb db, Capacidades capacidades, Reloj reloj, CancellationToken ct)
    {
        var tenant = usuario.EmpresaOperacion();
        await capacidades.ExigirAsync(tenant, Capacidad.DerechosPeticion, ct).ConfigureAwait(false);
        var ids = (pedido.ComparendoIds ?? []).Distinct().ToList();
        if (ids.Count is 0 or > 500)
            throw new ReglaException("SELECCION_INVALIDA", "Selecciona entre 1 y 500 comparendos.");
        var t = await db.Tipologias.AsNoTracking().FirstOrDefaultAsync(x => x.Id == pedido.TipologiaId, ct).ConfigureAwait(false) ?? throw ReglaException.NoEncontrado("La tipología");
        if (!t.PermiteMasivo)
            throw new ReglaException("TIPOLOGIA_SIN_MASIVO", $"«{t.Nombre}» no admite envío masivo.");
        var ots = await db.Comparendos.Where(c => c.TenantId == tenant && ids.Contains(c.Id)).Select(c => c.OrganismoTransitoId).Distinct().ToListAsync(ct).ConfigureAwait(false);
        if (ots.Count != 1 || ots[0] is null)
            throw new ReglaException("OT_DISTINTOS", "Todos los comparendos del lote deben ser del mismo organismo de tránsito.");

        var lote = new LoteDp { Id = Guid.CreateVersion7(), TenantId = tenant, TipologiaId = t.Id, OrganismoTransitoId = ots[0]!.Value, Total = ids.Count, CreadoPor = usuario.Id, CreadoEn = reloj.Ahora };
        db.LotesDp.Add(lote);
        db.LoteDpItems.AddRange(ids.Select(id => new LoteDpItem { Id = Guid.CreateVersion7(), LoteId = lote.Id, ComparendoId = id }));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Accepted($"{Program.Prefijo}/dp/lotes/{lote.Id}", new { lote.Id, lote.Total });
    }

    private static async Task<IResult> Listar(UsuarioActual usuario, ComparendosDb db, CancellationToken ct)
    {
        var tenant = usuario.EmpresaLectura(null, obligatoria: false);
        return Results.Ok(await db.LotesDp.AsNoTracking().Where(l => tenant == null || l.TenantId == tenant).OrderByDescending(l => l.CreadoEn).Take(50)
            .Select(l => new { l.Id, estado = l.Estado.ToString(), l.Total, l.Enviados, l.Omitidos, l.Fallidos, l.CreadoEn, l.TerminadoEn }).ToListAsync(ct).ConfigureAwait(false));
    }

    private static async Task<IResult> Detalle(Guid id, UsuarioActual usuario, ComparendosDb db, CancellationToken ct)
    {
        var lote = await db.LotesDp.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id, ct).ConfigureAwait(false);
        if (lote is null || (!usuario.EsSuperAdmin && lote.TenantId != usuario.EmpresaId))
            throw ReglaException.NoEncontrado("El lote");
        var items = await (from i in db.LoteDpItems.AsNoTracking()
                           join c in db.Comparendos.AsNoTracking() on i.ComparendoId equals c.Id into cs
                           from c in cs.DefaultIfEmpty()
                           join d in db.DerechosPeticion.AsNoTracking() on i.DerechoPeticionId equals d.Id into ds
                           from d in ds.DefaultIfEmpty()
                           where i.LoteId == id
                           select new { i.ComparendoId, numero = c == null ? null : c.Numero, estado = i.Estado.ToString(), i.Motivo, radicado = d == null ? null : d.Radicado })
            .ToListAsync(ct).ConfigureAwait(false);
        return Results.Ok(new { lote.Id, estado = lote.Estado.ToString(), lote.Total, lote.Enviados, lote.Omitidos, lote.Fallidos, lote.CreadoEn, lote.TerminadoEn, items });
    }
}

/// <summary>Procesa los ítems pendientes de los lotes de DP, de uno en uno.</summary>
public sealed partial class ProcesadorLotesDp(IServiceScopeFactory scopes, TimeProvider time, ILogger<ProcesadorLotesDp> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                while (await ProcesarSiguienteAsync(stoppingToken).ConfigureAwait(false))
                {
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFallo(logger, ex);
            }

            await Task.Delay(TimeSpan.FromSeconds(5), time, stoppingToken).ConfigureAwait(false);
        }
    }

    public async Task<bool> ProcesarSiguienteAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ComparendosDb>();
        var ids = await db.Database.SqlQuery<Guid>($"""
            UPDATE comparendos.lote_dp_item SET estado = 'Enviado'
            WHERE id = (SELECT id FROM comparendos.lote_dp_item WHERE estado = 'Pendiente' ORDER BY lote_id, id LIMIT 1 FOR UPDATE SKIP LOCKED)
            RETURNING id AS "Value"
            """).ToListAsync(ct).ConfigureAwait(false);
        if (ids.Count == 0)
            return false;

        var item = await db.LoteDpItems.FirstAsync(i => i.Id == ids[0], ct).ConfigureAwait(false);
        var lote = await db.LotesDp.FirstAsync(l => l.Id == item.LoteId, ct).ConfigureAwait(false);
        var servicio = scope.ServiceProvider.GetRequiredService<ServicioDp>();
        var despachador = scope.ServiceProvider.GetRequiredService<Correo.DespachadorCorreos>();
        List<Guid> comunicaciones = [];
        try
        {
            var c = await db.Comparendos.Include(x => x.Contraventor).FirstOrDefaultAsync(x => x.Id == item.ComparendoId && x.TenantId == lote.TenantId, ct).ConfigureAwait(false)
                ?? throw ReglaException.NoEncontrado("El comparendo");
            var dp = await servicio.EnviarAsync(c, lote.TipologiaId, new Actor(ActorTipo.Usuario, lote.CreadoPor, "Envío masivo"), lote.Id, ct).ConfigureAwait(false);
            item.DerechoPeticionId = dp.Id;
            comunicaciones = dp.ComunicacionIds;
            lote.Enviados++;
        }
        catch (ReglaException ex)
        {
            db.ChangeTracker.Clear();
            item = await db.LoteDpItems.FirstAsync(i => i.Id == ids[0], ct).ConfigureAwait(false);
            lote = await db.LotesDp.FirstAsync(l => l.Id == item.LoteId, ct).ConfigureAwait(false);
            (item.Estado, item.Motivo) = (EstadoItemLote.Omitido, ex.Message);
            lote.Omitidos++;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFallo(logger, ex);
            db.ChangeTracker.Clear();
            item = await db.LoteDpItems.FirstAsync(i => i.Id == ids[0], ct).ConfigureAwait(false);
            lote = await db.LotesDp.FirstAsync(l => l.Id == item.LoteId, ct).ConfigureAwait(false);
            (item.Estado, item.Motivo) = (EstadoItemLote.Fallido, ex.Message.Length > 500 ? ex.Message[..500] : ex.Message);
            lote.Fallidos++;
        }

        if (await db.LoteDpItems.CountAsync(i => i.LoteId == lote.Id && i.Estado == EstadoItemLote.Pendiente, ct).ConfigureAwait(false) == 0)
        {
            lote.Estado = lote.Fallidos > 0 ? EstadoLoteDp.CompletadoConErrores : EstadoLoteDp.Completado;
            lote.TerminadoEn = time.GetUtcNow();
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        foreach (var id in comunicaciones)
            await despachador.DespacharAsync(id, ct).ConfigureAwait(false);
        return true;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Falló un ítem del envío masivo de DP.")]
    private static partial void LogFallo(ILogger logger, Exception ex);
}
