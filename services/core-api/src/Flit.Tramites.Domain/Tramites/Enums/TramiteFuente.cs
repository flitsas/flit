namespace Flit.Tramites.Domain.Tramites.Enums;

/// <summary>
/// HU #11056 — fuente por la que el trámite entró a FLIT, para la columna "Fuente" del listado.
/// <para>
/// Se DERIVA de lo que ya persiste la instancia; no es una columna nueva:
/// <c>origin</c> (null = plataforma, <c>'ict'</c> = integración con terceros) e <c>is_migrated</c>
/// (foto importada de FLIT 1.0). El DDL fija ese dominio en
/// <c>40-ICT-procedure-external-ref.sql</c> y el único productor de <c>'ict'</c> es
/// <c>IctOrchestrationService</c>.
/// </para>
/// <para>
/// <b>No hay fuente "QX".</b> Quipux es canal de SALIDA (radicación ante el organismo de tránsito),
/// no de creación: ningún trámite nace de Quipux. Si algún día existe un originador Quipux, se añade
/// un código aquí y un caso en <see cref="Desde"/>; el resto del listado no cambia.
/// </para>
/// </summary>
public static class TramiteFuente
{
    /// <summary>Creado por un gestor en la plataforma (<c>origin</c> null).</summary>
    public const string Dashboard = "dashboard";

    /// <summary>Materializado por la integración con terceros (<c>origin='ict'</c>).</summary>
    public const string Integracion = "integracion";

    /// <summary>Importado por la migración V1→V2 (<c>is_migrated</c>).</summary>
    public const string Migrado = "migrado";

    /// <summary>Valor persistido de <c>origin</c> que identifica a la integración ICT.</summary>
    public const string OriginIct = "ict";

    /// <summary>
    /// Fuente del trámite. La migración gana sobre <paramref name="origin"/>: un trámite importado es
    /// una foto de V1 y su origen operativo dejó de ser relevante.
    /// </summary>
    public static string Desde(string? origin, bool isMigrated)
    {
        if (isMigrated)
            return Migrado;

        return string.Equals(origin?.Trim(), OriginIct, StringComparison.OrdinalIgnoreCase)
            ? Integracion
            : Dashboard;
    }
}
