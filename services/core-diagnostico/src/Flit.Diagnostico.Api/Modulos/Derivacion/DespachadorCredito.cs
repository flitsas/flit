using Flit.Diagnostico.Api.Modulos.Derivacion.Bus;
using Flit.Diagnostico.Api.Modulos.Trabajos;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Domain.Derivacion;
using Flit.Platform.Sdk.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Flit.Diagnostico.Api.Modulos.Derivacion;

/// <summary>
/// Despachador del crédito por compañía (Punto 4, incremento 3, diseño §5.3 punto 2, ADR-0084). En la transacción de quien llama
/// (encolar, cierre del consumidor, reintentar fallidas, reconciliador) y con la empresa ya fijada en el contexto:
/// <list type="number">
/// <item>Guarda lo pendiente del contexto (el ítem que acaba de cerrar ya no cuenta como ENVIADO).</item>
/// <item>Toma el candado de la compañía (<see cref="ColaDerivacion.BloquearCompaniaAsync"/>): dos liberaciones de la misma compañía
/// se ordenan y ninguna pasa del crédito.</item>
/// <item>Cuenta los ENVIADO de la compañía y libera hasta <c>CreditoPorCompania</c> − en vuelo PENDIENTE (<see cref="Siguientes"/>:
/// operación más antigua primero, luego el orden de la selección): ENVIADO desde ahora y el evento
/// <see cref="DerivacionSolicitada"/> (solo ids) en la outbox del scope.</item>
/// </list>
/// Sin SQL entre compañías: el conteo y la selección pasan por EF con el filtro de empresa (y la compañía explícita).
/// </summary>
internal sealed partial class DespachadorCredito(
    DiagnosticoDb db, IPlatformOutbox outbox, IOptions<OpcionesDerivacion> opciones, TimeProvider reloj, ILogger<DespachadorCredito> log)
{
    /// <summary>Libera lo que quepa del crédito de <paramref name="empresa"/>; devuelve cuántos ítems liberó. Exige transacción.</summary>
    public async Task<int> LiberarAsync(Guid empresa, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("La liberación del crédito va en la transacción de quien llama (el candado se suelta con ella).");
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await ColaDerivacion.BloquearCompaniaAsync(db, empresa, ct).ConfigureAwait(false);
        var enVuelo = await db.Set<ItemDerivacion>()
            .CountAsync(i => i.TenantId == empresa && i.Estado == EstadoItemDerivacion.Enviado, ct).ConfigureAwait(false);
        var libres = CreditoDerivacion.Libres(opciones.Value.CreditoPorCompania, enVuelo);
        if (libres == 0)
            return 0;

        var siguientes = await Siguientes(db.Set<ItemDerivacion>(), db.Set<OperacionDerivacion>(), empresa).Take(libres).ToListAsync(ct).ConfigureAwait(false);
        var ahora = reloj.GetUtcNow();
        foreach (var item in siguientes)
        {
            Marcar(item, ahora);
            outbox.Enqueue(DerivacionSolicitada.Tipo, DerivacionSolicitada.VersionContrato, empresa, new DerivacionSolicitada(item.OperacionId, item.Id, item.VehiculoId));
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        if (siguientes.Count > 0)
            Liberados(log, empresa, siguientes.Count, enVuelo);
        return siguientes.Count;
    }

    /// <summary>
    /// Ítems PENDIENTE de <paramref name="empresa"/> en orden de liberación: la operación más antigua primero (y su id para desempatar)
    /// y luego el orden de la selección. La misma consulta corre en EF y en memoria (pruebas de equidad).
    /// </summary>
    public static IQueryable<ItemDerivacion> Siguientes(IQueryable<ItemDerivacion> items, IQueryable<OperacionDerivacion> operaciones, Guid empresa) =>
        from i in items
        join o in operaciones on new { i.TenantId, i.OperacionId } equals new { o.TenantId, OperacionId = o.Id }
        where i.TenantId == empresa && i.Estado == EstadoItemDerivacion.Pendiente
        orderby o.CreadaEn, o.Id, i.Orden
        select i;

    /// <summary>El ítem sale hacia Trámites: ENVIADO desde <paramref name="ahora"/>, sin espera de reintento.</summary>
    public static void Marcar(ItemDerivacion item, DateTimeOffset ahora)
    {
        ArgumentNullException.ThrowIfNull(item);
        item.Estado = EstadoItemDerivacion.Enviado;
        item.EnviadoEn = ahora;
        item.DisponibleDesde = null;
        item.ActualizadoEn = ahora;
    }

    [LoggerMessage(EventId = 84301, Level = LogLevel.Debug, Message = "Crédito de la compañía {Empresa}: liberó {Liberados} ítems de derivación ({EnVuelo} ya en vuelo).")]
    private static partial void Liberados(ILogger logger, Guid empresa, int liberados, int enVuelo);
}
