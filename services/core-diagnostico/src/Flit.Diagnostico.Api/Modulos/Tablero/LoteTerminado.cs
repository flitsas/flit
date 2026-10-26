using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Api.Persistence.Configuraciones;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Diagnosticos;
using Flit.Platform.Sdk.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Flit.Diagnostico.Api.Modulos.Tablero;

/// <summary>
/// <c>data</c> del evento <c>diagnostico.lote.terminado</c> v1 (HU #13743, ADR-0075, brecha 2, Q8 = 8a;
/// <c>contracts/asyncapi/diagnostico-events.v1.yaml</c>): el lote llegó a COMPLETADO o INCOMPLETO. Lleva el id, el estado, si es
/// Express, la <see cref="Version"/> del tablero en el cierre (con el id, la llave de deduplicación de los consumidores), la fecha
/// de cierre y los totales: vehículos, celdas por estado y celdas en ERROR por <c>tipo_error</c> (solo las que lo tienen). Sin
/// datos personales: ni placas, ni VIN, ni documentos. La empresa va en el sobre. Por ahora nadie lo consume.
/// </summary>
/// <example>
/// <code>
/// await LoteTerminado.EncolarAsync(db, outbox, diagnosticoId, versionTablero, ahora, ct); // dentro de la transacción del cierre
/// </code>
/// </example>
public sealed record LoteTerminado(
    Guid DiagnosticoId, string Estado, bool EsExpress, long Version, DateTimeOffset TerminadoEn, int Vehiculos,
    IReadOnlyDictionary<string, int> Celdas, IReadOnlyDictionary<string, int> ErroresPorTipo)
{
    /// <summary>Tipo (y llave de ruteo) del evento en <c>flit.diagnostico</c>.</summary>
    public const string Tipo = "diagnostico.lote.terminado";

    /// <summary>Versión del sobre de este evento.</summary>
    public const int VersionContrato = 1;

    /// <summary>
    /// Calcula los totales del lote con el contexto de su empresa (las celdas que el contexto sigue cuentan con su valor en memoria:
    /// la que se está cerrando y las que se acaban de liberar o bloquear) y encola el evento en la outbox de ese contexto, sin
    /// guardar: lo guarda quien cierra la celda, en su transacción.
    /// </summary>
    internal static async Task<LoteTerminado> EncolarAsync(
        DiagnosticoDb db, IPlatformOutbox outbox, Guid diagnosticoId, long versionTablero, DateTimeOffset terminadoEn, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(outbox);
        var cabecera = await db.Set<CabeceraDiagnostico>().SingleAsync(c => c.Id == diagnosticoId, ct).ConfigureAwait(false);
        if (cabecera.Estado is not (EstadoDiagnostico.Completado or EstadoDiagnostico.Incompleto))
            throw new InvalidOperationException("El lote terminado solo se emite con el lote en COMPLETADO o INCOMPLETO.");

        var enMemoria = db.ChangeTracker.Entries<CeldaConsulta>()
            .Where(e => e.Entity.DiagnosticoId == diagnosticoId)
            .ToDictionary(e => e.Entity.Id, e => (e.Entity.Estado, e.Entity.TipoError));
        var deBase = await db.Set<CeldaConsulta>().AsNoTracking()
            .Where(c => c.DiagnosticoId == diagnosticoId)
            .Select(c => new { c.Id, c.Estado, c.TipoError })
            .ToListAsync(ct).ConfigureAwait(false);
        var celdas = deBase.Select(c => enMemoria.TryGetValue(c.Id, out var actual) ? actual : (c.Estado, c.TipoError)).ToList();
        var vehiculos = await db.Set<VehiculoDiagnostico>().CountAsync(v => v.DiagnosticoId == diagnosticoId, ct).ConfigureAwait(false);

        var evento = new LoteTerminado(
            cabecera.Id,
            CodigosDeEnum.Codigo(cabecera.Estado),
            cabecera.EsExpress,
            versionTablero,
            // El mismo instante que queda en cerrado_en (Postgres guarda microsegundos).
            terminadoEn.AddTicks(-(terminadoEn.Ticks % (TimeSpan.TicksPerMillisecond / 1000))),
            vehiculos,
            celdas.GroupBy(c => CodigosDeEnum.Codigo(c.Estado)).OrderBy(g => g.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count()),
            celdas.Where(c => c.Estado == EstadoCelda.Error && !string.IsNullOrEmpty(c.TipoError))
                .GroupBy(c => c.TipoError!).OrderBy(g => g.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count()));
        outbox.Enqueue(Tipo, VersionContrato, cabecera.TenantId, evento);
        return evento;
    }
}
