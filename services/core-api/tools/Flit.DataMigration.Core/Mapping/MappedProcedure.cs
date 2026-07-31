using Flit.Tramites.Domain.Entities;

namespace Flit.DataMigration.V1.Mapping;

/// <summary>
/// Resultado del mapeo: el grafo de entidades de V2 listo para cargar, más lo que el
/// operador necesita saber. Todavía no toca la base — separar "mapear" de "cargar"
/// es lo que permite que <c>--dry-run</c> muestre exactamente qué se escribiría.
/// </summary>
public sealed class MappedProcedure
{
    public required long V1Id { get; init; }
    public required string V1Table { get; init; }
    public required ProcedureInstance Instance { get; init; }
    public required IReadOnlyList<ProcedureInstanceActor> Actors { get; init; }
    public required IReadOnlyList<ProcedureInstanceFieldValue> FieldValues { get; init; }
    public required IReadOnlyList<ProcedureInstanceStatusHistory> StatusHistory { get; init; }

    /// <summary>
    /// Datos comerciales del traspaso (valor de venta / causal), 1:1 con la instancia. <c>null</c>
    /// cuando V1 no tenía valor de compraventa. Alimenta el paso "comercial" del wizard de V2.
    /// </summary>
    public ProcedureInstanceCommercial? Commercial { get; init; }

    /// <summary>
    /// Estado FINAL que debe quedar en V2. Se guarda aparte porque la instancia se inserta
    /// en <c>borrador</c> (lo exige el trigger de inmutabilidad) y solo al final se sube
    /// a su estado real.
    /// </summary>
    public required string FinalStatus { get; init; }

    /// <summary>Avisos no bloqueantes: estados ambiguos, historial divergente, actores incompletos…</summary>
    public required IReadOnlyList<string> Warnings { get; init; }
}
