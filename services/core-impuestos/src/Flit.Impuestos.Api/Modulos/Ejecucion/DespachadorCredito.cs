using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Domain.Liquidaciones;
using Flit.Platform.Sdk.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Flit.Impuestos.Api.Modulos.Ejecucion;

/// <summary>
/// Despachador del crédito (plan §1.4 decisión C1, §2.3; DA-5, S-D; forma del de la derivación de Diagnóstico, sin su código). En la
/// transacción de quien llama (crear la liquidación, cerrar un ítem en el consumidor, el reconciliador) y con la empresa fijada en el
/// contexto:
/// <list type="number">
///   <item>Guarda lo pendiente del contexto (un ítem que acaba de cerrar ya no cuenta en vuelo).</item>
///   <item>Toma el candado global del crédito (<c>pg_advisory_xact_lock</c>, se suelta con la transacción): dos liberaciones a la vez
///   se ordenan y ninguna pasa los topes.</item>
///   <item>Cuenta los ítems en vuelo (<see cref="EnVuelo"/>) de la compañía (EF, con el filtro de empresa) y de todas (solo un
///   conteo, sin leer filas de otra compañía) y libera hasta <see cref="Libres"/>: <c>PENDIENTE</c> de la compañía, la liquidación
///   más antigua primero y luego la fila del archivo. Cada uno pasa a <c>CONSULTANDO_RUNT</c> (máquina de estados del dominio), su
///   lote a <c>EN_PROCESO</c> y deja <see cref="LiquidacionSolicitada"/> (solo ids) en la outbox del scope.</item>
/// </list>
/// Si el tope global está lleno, los pendientes de una compañía esperan a la siguiente liberación o a la vuelta del
/// <see cref="ReconciliadorEjecucion"/>. Logs: ids y conteos.
/// </summary>
/// <example>
/// <code>
/// await using var tx = await db.Database.BeginTransactionAsync(ct);
/// await despachador.LiberarAsync(empresa, ct);
/// await tx.CommitAsync(ct);
/// </code>
/// </example>
public sealed partial class DespachadorCredito(
    ImpuestosDb db, IPlatformOutbox outbox, IOptions<OpcionesEjecucion> opciones, TimeProvider reloj, ILogger<DespachadorCredito> log)
{
    /// <summary>Estados que ocupan crédito: el ítem está consultando el RUNT o enviando al API.</summary>
    public static IReadOnlyList<EstadoItem> EnVuelo { get; } = [EstadoItem.ConsultandoRunt, EstadoItem.Enviando];

    private static readonly string[] CodigosEnVuelo = [.. EnVuelo.Select(CodigosDeEnum.Codigo)];

    /// <summary>Cuántos se pueden liberar: el menor hueco entre el de la compañía y el global (nunca negativo).</summary>
    public static int Libres(int porEmpresa, int enVueloEmpresa, int global, int enVueloGlobal) =>
        Math.Max(0, Math.Min(porEmpresa - enVueloEmpresa, global - enVueloGlobal));

    /// <summary>Libera lo que quepa del crédito de <paramref name="empresa"/>; devuelve cuántos ítems liberó. Exige transacción.</summary>
    public async Task<int> LiberarAsync(Guid empresa, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("La liberación del crédito va en la transacción de quien llama (el candado se suelta con ella).");
        if (db.EmpresaActual != empresa)
            throw new InvalidOperationException("El contexto del despachador no trabaja con la empresa que libera.");

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(hashtextextended('impuestos:credito', 0))", ct).ConfigureAwait(false);
        var enVuelo = EnVuelo.ToList();
        var deLaEmpresa = await db.Set<LiquidacionItem>().CountAsync(i => i.TenantId == empresa && enVuelo.Contains(i.Estado), ct).ConfigureAwait(false);
        // Conteo global: un número, sin filas de otra compañía (el filtro de empresa no aplica a una consulta escalar).
        var global = await db.Database
            .SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM impuestos.liquidacion_items WHERE estado = ANY({CodigosEnVuelo})")
            .SingleAsync(ct).ConfigureAwait(false);
        var libres = Libres(opciones.Value.ConcurrenciaPorEmpresa, deLaEmpresa, opciones.Value.ConcurrenciaGlobal, global);
        if (libres == 0)
            return 0;

        var siguientes = await (
                from i in db.Set<LiquidacionItem>()
                join l in db.Set<Liquidacion>() on i.LiquidacionId equals l.Id
                where i.TenantId == empresa && i.Estado == EstadoItem.Pendiente
                orderby l.CreadaEn, l.Id, i.Fila, i.Id
                select new { Item = i, Lote = l })
            .Take(libres).ToListAsync(ct).ConfigureAwait(false);
        var ahora = reloj.GetUtcNow();
        foreach (var s in siguientes)
        {
            var nueva = TransicionesItem.Aplicar(new SituacionItem(s.Item.Estado, s.Item.IntentosRunt, s.Item.IntentosReenvio, s.Item.ErrorClase),
                new EventoItem.CreditoAsignado()).Nueva;
            s.Item.Estado = nueva.Estado;
            s.Item.ProximoIntentoEn = null;
            s.Item.ActualizadoEn = ahora;
            if (s.Lote.Estado == EstadoLote.Creado)
                s.Lote.Estado = CierreLote.Despachar(s.Lote.Estado);
            outbox.Enqueue(LiquidacionSolicitada.Tipo, LiquidacionSolicitada.Version, empresa, new LiquidacionSolicitada(s.Item.LiquidacionId, s.Item.Id));
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        if (siguientes.Count > 0)
            Liberados(log, empresa, siguientes.Count, deLaEmpresa, global);
        return siguientes.Count;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Crédito de la compañía {Empresa}: liberó {Liberados} ítems ({EnVuelo} en vuelo de la compañía, {Global} en total).")]
    private static partial void Liberados(ILogger logger, Guid empresa, int liberados, int enVuelo, int global);
}
