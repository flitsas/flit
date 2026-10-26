using Flit.Diagnostico.Api.Modulos.Consultas.Bus;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Diagnosticos;
using Microsoft.EntityFrameworkCore;

namespace Flit.Diagnostico.Api.Modulos.Consultas;

/// <summary>
/// Encolador real del motor de consultas (HU #13727, F03-H2, ADR a): reemplaza al nulo de D1. Lo invocan la confirmación del
/// lote y el Express dentro de SU transacción, así que si algo falla no queda vehículo, celda ni transición (AC2). Crea una
/// celda <c>PENDIENTE</c> por vehículo y fuente del alcance de la cabecera (las no elegidas no nacen); la tabla
/// <c>consultas</c> es la cola. La celda RUNT queda disponible ya y las demás nacen con <see cref="Diferida"/> (las libera
/// F03-H4 cuando RUNT termina). Sube <c>diagnosticos.version_tablero</c> y la copia en las celdas nuevas. Pasa el lote de
/// <c>CREADO</c> a <c>EN_PROCESO</c> con historial <c>AUTO</c> (<see cref="TransicionesLote"/>). Idempotente: una celda que
/// ya existe (en cualquier estado, también <c>COMPLETADO</c> o <c>NA</c>) no se crea ni se reabre, y un lote que ya está en
/// proceso no repite la transición. Escribe con el filtro de la empresa del contexto. Desde ADR-0075 (HU #13727) deja además, en
/// esa misma transacción, el trabajo de consulta de la celda RUNT de CADA vehículo nuevo en la outbox
/// (<see cref="PublicadorCeldas"/>, <c>consultas.diagnostico[-express].runt</c>); las diferidas se publican al liberarse. Nadie lo
/// consume todavía: el procesador de la cola sigue ejecutando la celda como siempre.
/// </summary>
internal sealed class EncoladorConsultas(DiagnosticoDb db, TimeProvider reloj, PublicadorCeldas publicador) : IEncoladorConsultas
{
    /// <summary>
    /// <c>disponible_desde</c> de una celda que espera a la RUNT de su vehículo: fuera del alcance de la cola hasta que
    /// F03-H4 la libere. Fecha finita (no <c>infinity</c>) para no depender de la conversión de Npgsql.
    /// </summary>
    public static readonly DateTimeOffset Diferida = new(9999, 12, 31, 0, 0, 0, TimeSpan.Zero);

    public async Task EncolarAsync(Guid diagnosticoId, IReadOnlyList<VehiculoAEncolar> vehiculos, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(vehiculos);
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("El encolado exige la transacción de quien guarda los vehículos.");
        var cabecera = await db.Set<CabeceraDiagnostico>().SingleOrDefaultAsync(c => c.Id == diagnosticoId, ct).ConfigureAwait(false)
            ?? throw ReglaException.NoEncontrado("El diagnóstico");

        var ahora = reloj.GetUtcNow();
        var ids = vehiculos.Select(v => v.VehiculoId).Distinct().ToList();
        var existentes = (await db.Set<CeldaConsulta>()
                .Where(c => c.DiagnosticoId == diagnosticoId && ids.Contains(c.VehiculoId))
                .Select(c => new { c.VehiculoId, c.Fuente })
                .ToListAsync(ct).ConfigureAwait(false))
            .Select(c => (c.VehiculoId, c.Fuente)).ToHashSet();
        var nuevas = (from vehiculo in ids
                      from fuente in cabecera.Alcance.Distinct()
                      where !existentes.Contains((vehiculo, fuente))
                      select new CeldaConsulta
                      {
                          TenantId = cabecera.TenantId,
                          DiagnosticoId = cabecera.Id,
                          VehiculoId = vehiculo,
                          Fuente = fuente,
                          Estado = EstadoCelda.Pendiente,
                          DisponibleDesde = fuente == Fuente.Runt ? ahora : Diferida,
                          ActualizadoEn = ahora,
                      }).ToList();

        if (nuevas.Count > 0)
        {
            cabecera.VersionTablero++;
            foreach (var celda in nuevas)
                celda.VersionTablero = cabecera.VersionTablero;
            db.AddRange(nuevas);
            // ADR-0075: la RUNT de todos los vehículos nuevos sale ya; las demás esperan a que su RUNT las libere.
            foreach (var celda in nuevas.Where(c => c.Fuente == Fuente.Runt))
                await publicador.PublicarAsync(celda, ct).ConfigureAwait(false);
        }

        if (cabecera.Estado == EstadoDiagnostico.Creado)
        {
            var transicion = TransicionesLote.Transicionar(cabecera.Estado, EstadoDiagnostico.EnProceso, DisparadorEstado.Auto);
            cabecera.Estado = transicion.Nuevo;
            db.Add(new HistorialEstadoDiagnostico
            {
                TenantId = cabecera.TenantId,
                DiagnosticoId = cabecera.Id,
                EstadoAnterior = transicion.Anterior,
                EstadoNuevo = transicion.Nuevo,
                Disparador = transicion.Disparador,
                En = ahora,
            });
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
