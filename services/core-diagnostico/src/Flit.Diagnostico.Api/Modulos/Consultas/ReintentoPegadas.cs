using System.Text.Json.Serialization;
using Flit.Diagnostico.Api.Modulos.Consultas.Bus;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Modulos.Tablero;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Consultas;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Flit.Diagnostico.Api.Modulos.Consultas;

/// <summary>Respuesta de los reintentos manuales: cuántas celdas se reencolaron (0 si no había ninguna pegada).</summary>
public sealed record ReintentoPegadasDto([property: JsonPropertyName("reencoladas")] int Reencoladas);

/// <summary>
/// Reintento manual de las celdas pegadas (ajuste 2026-11-11 Punto 3, decisiones 3.2–3.4): de una placa
/// (<see cref="ReintentarVehiculoAsync"/>) o de todo el lote (<see cref="ReintentarLoteAsync"/>). Las celdas que reencola son las de
/// <see cref="CriterioPegada.Reencolables"/> (ERROR, o PENDIENTE/EN_CURSO sin cambio hace más de
/// <see cref="OpcionesConsultas.UmbralPegada"/>; con la RUNT sin completar, solo la RUNT): las completas y las recientes no se tocan.
/// Cada una sale por el MISMO camino del reproceso por celda (<see cref="Reproceso.PrepararReencolado"/> y el
/// <see cref="PublicadorCeldas"/>): PENDIENTE, <c>intentos_auto=0</c> (vuelve a tener sus automáticos), <c>disponible_desde=ahora</c>,
/// el usuario del reproceso y un trabajo con <c>trabajo_id</c> NUEVO, así que la respuesta tardía del anterior se descarta
/// (<see cref="ConsumidorResultados"/>, V6). Un lote COMPLETADO o INCOMPLETO vuelve a EN_PROCESO con historial REPROCESO (R-8). Todo
/// en una transacción con la cabecera bloqueada por la subida de la versión. Idempotente: una celda recién reencolada cambió hace
/// menos del umbral y no cuenta (AC5). La compañía y la visibilidad son las de <see cref="AccesoLote"/> (404 si no se ve); un
/// vehículo que no es del lote, 404.
/// </summary>
/// <example>
/// <code>
/// var dto = await reintento.ReintentarVehiculoAsync(diagnosticoId, vehiculoId, ct); // { "reencoladas": 2 }
/// </code>
/// </example>
internal sealed class ReintentoPegadas(
    DiagnosticoDb db, UsuarioActual usuario, TimeProvider reloj, PublicadorCeldas publicador, IOptions<OpcionesConsultas> opciones)
{
    /// <summary>Reencola las celdas pegadas de un vehículo del lote.</summary>
    public Task<ReintentoPegadasDto> ReintentarVehiculoAsync(Guid diagnosticoId, Guid vehiculoId, CancellationToken ct) =>
        ReintentarAsync(diagnosticoId, vehiculoId, ct);

    /// <summary>Reencola las celdas pegadas de todo el lote.</summary>
    public Task<ReintentoPegadasDto> ReintentarLoteAsync(Guid diagnosticoId, CancellationToken ct) => ReintentarAsync(diagnosticoId, null, ct);

    private async Task<ReintentoPegadasDto> ReintentarAsync(Guid diagnosticoId, Guid? vehiculoId, CancellationToken ct)
    {
        var autor = usuario.Id != Guid.Empty
            ? usuario.Id
            : throw ReglaException.Prohibido("SIN_USUARIO", "El token no identifica al usuario: el reintento no se puede registrar.");

        // El reintento de Npgsql exige que la transacción del usuario vaya dentro de la estrategia de ejecución.
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaccion = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            await AccesoLote.LeerVisibleAsync(db, usuario, diagnosticoId, seguir: false, ct).ConfigureAwait(false);
            if (vehiculoId is { } vid
                && !await db.Set<VehiculoDiagnostico>().AnyAsync(v => v.Id == vid && v.DiagnosticoId == diagnosticoId, ct).ConfigureAwait(false))
                throw ReglaException.NoEncontrado("El vehículo");

            var ahora = reloj.GetUtcNow();
            var celdas = await db.Set<CeldaConsulta>()
                .Where(c => c.DiagnosticoId == diagnosticoId && (vehiculoId == null || c.VehiculoId == vehiculoId))
                .Where(c => c.Fuente == Fuente.Runt || c.Estado == EstadoCelda.Error || c.Estado == EstadoCelda.Pendiente || c.Estado == EstadoCelda.EnCurso)
                .ToListAsync(ct).ConfigureAwait(false);
            var ids = CriterioPegada.Reencolables(
                    celdas.Select(c => new CeldaParaReintento(c.Id, c.VehiculoId, c.Fuente, c.Estado, c.ActualizadoEn)), ahora, opciones.Value.UmbralPegada)
                .Select(c => c.Id).ToHashSet();
            if (ids.Count == 0)
                return new ReintentoPegadasDto(0);

            var aReencolar = celdas.Where(c => ids.Contains(c.Id)).ToList();
            var version = await EscritorCeldas.SubirVersionTableroAsync(db, diagnosticoId, ct).ConfigureAwait(false);
            var cabecera = await db.Set<CabeceraDiagnostico>().SingleAsync(c => c.Id == diagnosticoId, ct).ConfigureAwait(false);
            if (cabecera.Estado is Domain.Diagnosticos.EstadoDiagnostico.Completado or Domain.Diagnosticos.EstadoDiagnostico.Incompleto)
                Reproceso.Reabrir(db, cabecera, autor, ahora);
            foreach (var celda in aReencolar)
                Reproceso.PrepararReencolado(celda, autor, ahora, version);
            try
            {
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                // Como el reproceso: el trabajo nuevo de cada celda (trabajo_id nuevo) va en la misma transacción, después de guardar el
                // reencolado para que, si una celda no tiene qué consultar y se cierra ahí mismo, el recálculo vea el lote reabierto.
                foreach (var celda in aReencolar)
                    await publicador.PublicarAsync(celda, ct, version).ConfigureAwait(false);
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
            catch (DbUpdateConcurrencyException)
            {
                throw ReglaException.Conflicto(Reproceso.CodigoCeldaModificada, "Una consulta cambió mientras se pedía el reintento. Vuelve a cargar el tablero.");
            }

            await transaccion.CommitAsync(ct).ConfigureAwait(false);
            return new ReintentoPegadasDto(aReencolar.Count);
        }).ConfigureAwait(false);
    }
}
