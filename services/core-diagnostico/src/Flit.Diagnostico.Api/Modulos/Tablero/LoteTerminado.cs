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
/// await LoteTerminado.EncolarAsync(db, outbox, cabecera, db.Set&lt;CeldaConsulta&gt;(), db.Set&lt;VehiculoDiagnostico&gt;(), version, ahora, ct);
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
    /// Calcula los totales del lote leyendo <paramref name="celdas"/> y <paramref name="vehiculos"/> (las de la empresa del lote; las
    /// celdas que el contexto sigue cuentan con su valor en memoria: la que se está cerrando y las que se acaban de liberar o
    /// bloquear) y encola el evento en la outbox del mismo contexto, sin guardar: lo guarda quien abrió la transacción. Una sola
    /// vez por transición: si el contexto ya tiene encolado el de este lote con esta versión, no repite y devuelve null.
    /// </summary>
    internal static async Task<LoteTerminado?> EncolarAsync(
        DiagnosticoDb db, IPlatformOutbox outbox, CabeceraDiagnostico cabecera, IQueryable<CeldaConsulta> celdas, IQueryable<VehiculoDiagnostico> vehiculos,
        long versionTablero, DateTimeOffset terminadoEn, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(outbox);
        ArgumentNullException.ThrowIfNull(cabecera);
        ArgumentNullException.ThrowIfNull(celdas);
        ArgumentNullException.ThrowIfNull(vehiculos);
        if (cabecera.Estado is not (EstadoDiagnostico.Completado or EstadoDiagnostico.Incompleto))
            throw new InvalidOperationException("El lote terminado solo se emite con el lote en COMPLETADO o INCOMPLETO.");
        if (YaEncolado(db, cabecera.Id, versionTablero))
            return null;

        var enMemoria = db.ChangeTracker.Entries<CeldaConsulta>()
            .Where(e => e.Entity.DiagnosticoId == cabecera.Id)
            .ToDictionary(e => e.Entity.Id, e => (e.Entity.Estado, e.Entity.TipoError));
        var deBase = await celdas.AsNoTracking()
            .Where(c => c.DiagnosticoId == cabecera.Id)
            .Select(c => new { c.Id, c.Estado, c.TipoError })
            .ToListAsync(ct).ConfigureAwait(false);
        var estados = deBase.Select(c => enMemoria.TryGetValue(c.Id, out var actual) ? actual : (c.Estado, c.TipoError)).ToList();
        var cuantos = await vehiculos.CountAsync(v => v.DiagnosticoId == cabecera.Id, ct).ConfigureAwait(false);

        var evento = new LoteTerminado(
            cabecera.Id,
            CodigosDeEnum.Codigo(cabecera.Estado),
            cabecera.EsExpress,
            versionTablero,
            // El mismo instante que queda en cerrado_en (Postgres guarda microsegundos).
            terminadoEn.AddTicks(-(terminadoEn.Ticks % (TimeSpan.TicksPerMillisecond / 1000))),
            cuantos,
            estados.GroupBy(c => CodigosDeEnum.Codigo(c.Estado)).OrderBy(g => g.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count()),
            estados.Where(c => c.Estado == EstadoCelda.Error && !string.IsNullOrEmpty(c.TipoError))
                .GroupBy(c => c.TipoError!).OrderBy(g => g.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count()));
        outbox.Enqueue(Tipo, VersionContrato, cabecera.TenantId, evento);
        return evento;
    }

    /// <summary>Si el contexto ya tiene sin guardar el evento de este lote con esta versión (una transición, un evento).</summary>
    private static bool YaEncolado(DiagnosticoDb db, Guid diagnosticoId, long versionTablero) =>
        db.ChangeTracker.Entries<OutboxMessage>()
            .Where(e => e.State == EntityState.Added && e.Entity.RoutingKey == Tipo)
            .Select(e => EventEnvelope.FromJson(System.Text.Encoding.UTF8.GetBytes(e.Entity.Payload)).Data)
            .Any(d => d.GetProperty("diagnosticoId").GetGuid() == diagnosticoId && d.GetProperty("version").GetInt64() == versionTablero);
}
