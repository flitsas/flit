using System.Text.Json.Serialization;
using Flit.Diagnostico.Api.Modulos.Consultas.Bus;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Modulos.Tablero;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Api.Persistence.Configuraciones;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Diagnosticos;
using Microsoft.EntityFrameworkCore;

namespace Flit.Diagnostico.Api.Modulos.Consultas;

/// <summary>Respuesta de <c>POST /consultas/{cid}/reprocesar</c>: la celda ya está en cola y el estado del lote.</summary>
public sealed record ReprocesoDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("diagnostico_id")] Guid DiagnosticoId,
    [property: JsonPropertyName("estado")] string Estado,
    [property: JsonPropertyName("estado_lote")] string EstadoLote,
    [property: JsonPropertyName("version")] long Version);

/// <summary>
/// Reproceso de una celda en ERROR (HU #13746, F03-H8, R-1, R-8, plan §6.1 fila 03): la celda vuelve a PENDIENTE con
/// <c>intentos_auto=0</c>, <c>disponible_desde=ahora</c> y el usuario en <c>reproceso_usuario_id</c>; el procesador repite la
/// consulta real (sin reutilizar el dato del mismo día) y su intento queda con <c>disparador=REPROCESO</c> y ese usuario. Nunca
/// escribe datos de resultado: el endpoint no lee cuerpo. La celda sube <c>version_tablero</c> (el sondeo del tablero la ve
/// volver a gris). Si el lote estaba COMPLETADO o INCOMPLETO vuelve a EN_PROCESO, sin <c>cerrado_en</c> y con una fila del
/// historial REPROCESO con el usuario; al terminar, <see cref="RecalculadorEstado"/> fija el estado final. Todo en una
/// transacción, con la cabecera bloqueada por la subida de la versión (como <see cref="EscritorCeldas"/>). Desde ADR-0075 (HU #13746)
/// la celda publica en esa misma transacción su trabajo de consulta con un <c>trabajo_id</c> nuevo (<see cref="PublicadorCeldas"/>):
/// un resultado del trabajo anterior ya no es el vigente.
/// <para>
/// No procede (409, nada cambia): celda que no está en ERROR (<see cref="CodigoNoEnError"/>); celda
/// <see cref="OrquestadorVehiculo.BloqueadaPorRunt"/> mientras la RUNT del vehículo no esté COMPLETADO
/// (<see cref="CodigoBloqueadaPorRunt"/>: se reprocesa la RUNT y su cierre en OK la libera); la celda cambió entre la lectura y
/// el guardado (<see cref="CodigoCeldaModificada"/>). Celda de otra compañía, o de un lote que el usuario no ve
/// (<see cref="AccesoLote"/>): 404.
/// </para>
/// </summary>
/// <example>
/// <code>
/// var dto = await reproceso.ReprocesarAsync(consultaId, ct); // 202 { id, diagnostico_id, estado: "PENDIENTE", estado_lote, version }
/// </code>
/// </example>
internal sealed class Reproceso(DiagnosticoDb db, UsuarioActual usuario, TimeProvider reloj, PublicadorCeldas publicador)
{
    public const string CodigoNoEnError = "CELDA_NO_EN_ERROR";
    public const string CodigoBloqueadaPorRunt = "CELDA_BLOQUEADA_POR_RUNT";
    public const string CodigoCeldaModificada = "CELDA_MODIFICADA";

    public async Task<ReprocesoDto> ReprocesarAsync(Guid consultaId, CancellationToken ct)
    {
        var autor = usuario.Id != Guid.Empty
            ? usuario.Id
            : throw ReglaException.Prohibido("SIN_USUARIO", "El token no identifica al usuario: el reproceso no se puede registrar.");

        // El reintento de Npgsql exige que la transacción del usuario vaya dentro de la estrategia de ejecución.
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaccion = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            var celda = await db.Set<CeldaConsulta>().SingleOrDefaultAsync(c => c.Id == consultaId, ct).ConfigureAwait(false)
                ?? throw ReglaException.NoEncontrado("La consulta");
            await AccesoLote.LeerVisibleAsync(db, usuario, celda.DiagnosticoId, seguir: false, ct).ConfigureAwait(false);
            await ValidarAsync(celda, ct).ConfigureAwait(false);

            var ahora = reloj.GetUtcNow();
            var version = await SubirVersionAsync(celda.DiagnosticoId, ct).ConfigureAwait(false);
            var cabecera = await db.Set<CabeceraDiagnostico>().SingleAsync(c => c.Id == celda.DiagnosticoId, ct).ConfigureAwait(false);
            if (cabecera.Estado is EstadoDiagnostico.Completado or EstadoDiagnostico.Incompleto)
                Reabrir(cabecera, autor, ahora);

            celda.Estado = EstadoCelda.Pendiente;
            celda.TipoError = null;
            celda.IntentosAuto = 0;
            celda.DisponibleDesde = ahora;
            celda.LeaseHasta = null;
            celda.ReprocesoUsuarioId = autor;
            celda.ActualizadoEn = ahora;
            celda.VersionTablero = version;
            // ADR-0075: trabajo nuevo de la celda (trabajo_id nuevo), en la misma transacción del reproceso.
            await publicador.PublicarAsync(celda, ct).ConfigureAwait(false);
            try
            {
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
            catch (DbUpdateConcurrencyException)
            {
                throw ReglaException.Conflicto(CodigoCeldaModificada, "La consulta cambió mientras se pedía el reproceso. Vuelve a cargar el tablero.");
            }

            await transaccion.CommitAsync(ct).ConfigureAwait(false);
            return new ReprocesoDto(celda.Id, celda.DiagnosticoId, CodigosDeEnum.Codigo(celda.Estado), CodigosDeEnum.Codigo(cabecera.Estado), version);
        }).ConfigureAwait(false);
    }

    /// <summary>Solo una celda en ERROR; la bloqueada por RUNT, solo si su RUNT ya completó.</summary>
    private async Task ValidarAsync(CeldaConsulta celda, CancellationToken ct)
    {
        if (celda.Estado != EstadoCelda.Error)
            throw ReglaException.Conflicto(CodigoNoEnError, $"Solo se reprocesa una consulta en error; esta está {CodigosDeEnum.Codigo(celda.Estado)}.");
        if (celda.TipoError != OrquestadorVehiculo.BloqueadaPorRunt)
            return;

        var runt = await db.Set<CeldaConsulta>().AsNoTracking()
            .Where(c => c.VehiculoId == celda.VehiculoId && c.Fuente == Fuente.Runt)
            .Select(c => (EstadoCelda?)c.Estado)
            .SingleOrDefaultAsync(ct).ConfigureAwait(false);
        if (runt != EstadoCelda.Completado)
            throw ReglaException.Conflicto(CodigoBloqueadaPorRunt, "Esta consulta depende del RUNT del vehículo, que no se ha completado: reprocesa primero el RUNT.");
    }

    /// <summary>R-8: el lote cerrado vuelve a EN_PROCESO con historial REPROCESO y el usuario.</summary>
    private void Reabrir(CabeceraDiagnostico cabecera, Guid autor, DateTimeOffset ahora)
    {
        var transicion = TransicionesLote.Transicionar(cabecera.Estado, EstadoDiagnostico.EnProceso, DisparadorEstado.Reproceso);
        cabecera.Estado = transicion.Nuevo;
        cabecera.CerradoEn = null;
        db.Add(new HistorialEstadoDiagnostico
        {
            TenantId = cabecera.TenantId,
            DiagnosticoId = cabecera.Id,
            EstadoAnterior = transicion.Anterior,
            EstadoNuevo = transicion.Nuevo,
            Disparador = transicion.Disparador,
            UsuarioId = autor,
            En = ahora,
        });
    }

    /// <summary>Sube la versión del tablero; el UPDATE deja la cabecera bloqueada hasta el commit (frente al procesador).</summary>
    private async Task<long> SubirVersionAsync(Guid diagnosticoId, CancellationToken ct)
    {
        await db.Set<CabeceraDiagnostico>().Where(c => c.Id == diagnosticoId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.VersionTablero, c => c.VersionTablero + 1), ct).ConfigureAwait(false);
        return await db.Set<CabeceraDiagnostico>().Where(c => c.Id == diagnosticoId).Select(c => c.VersionTablero).SingleAsync(ct).ConfigureAwait(false);
    }
}
