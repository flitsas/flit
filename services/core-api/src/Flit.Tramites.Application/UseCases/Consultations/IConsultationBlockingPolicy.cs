namespace Flit.Tramites.Application.UseCases.Consultations;

/// <summary>
/// Decisión de bloqueo por criterio para el par (tenant, OT destino) del trámite (FEATURE 05).
/// Solo transporta los OVERRIDES explícitos que la compañía configuró; los criterios sin fila
/// caen al default de <see cref="ConsultationBlockingCriteria.DefaultBlocks"/>. Comparación de
/// criterios case-insensitive.
/// </summary>
public sealed record ConsultationBlockingRules(IReadOnlyDictionary<string, bool> Overrides)
{
    /// <summary>Sin overrides: todos los criterios aplican su default (tabla dispersa).</summary>
    public static readonly ConsultationBlockingRules None =
        new(new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// Construye el mapa normalizado (case-insensitive) a partir de los pares (criterio, blocks)
    /// crudos de la BD. Si un criterio viene repetido gana la última ocurrencia.
    /// </summary>
    public static ConsultationBlockingRules From(IEnumerable<KeyValuePair<string, bool>> overrides)
    {
        var map = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var (criterion, blocks) in overrides)
            map[criterion] = blocks;
        return map.Count == 0 ? None : new ConsultationBlockingRules(map);
    }

    /// <summary>
    /// ¿El criterio bloquea (fail→rojo) para este par (tenant, OT)? Usa el override configurado o,
    /// en su ausencia, el default del criterio. Lo consume la SEVERIDAD del preflight, donde el
    /// default de comparendos/RNMC es "advierte" (no eleva el warn a fail sin configuración).
    /// </summary>
    public bool Blocks(string criterion) =>
        Overrides.TryGetValue(criterion, out var blocks)
            ? blocks
            : ConsultationBlockingCriteria.DefaultBlocks(criterion);

    /// <summary>
    /// Override EXPLÍCITO configurado por la compañía, o <c>null</c> si no hay fila. Lo consume el
    /// gate <c>simit_multas</c> del wizard, cuyo default previo (sin fila) era BLOQUEAR — distinto del
    /// default de severidad del preflight (advertir). Así, sin configuración cada superficie conserva
    /// su comportamiento previo; con un override, ambas lo respetan.
    /// </summary>
    public bool? Override(string criterion) =>
        Overrides.TryGetValue(criterion, out var blocks) ? blocks : null;
}

/// <summary>
/// Puerto para resolver, por criterio, SI un hallazgo negativo del preflight bloquea el trámite
/// (rojo, subsanable) o solo advierte (amarillo), según la política que la compañía fijó sobre el
/// OT destino (<c>admin.tenant_transit_office_blocking_policies</c>, FEATURE 05). Desacopla Trámites
/// de Admin (mismo patrón que <see cref="IConsultationRestrictionPolicy"/>).
///
/// Eje ORTOGONAL al de <see cref="IConsultationRestrictionPolicy"/>: aquel decide SI la consulta se
/// ejecuta; este decide, para una que SÍ corre, si su resultado negativo bloquea.
/// </summary>
public interface IConsultationBlockingPolicy
{
    /// <summary>
    /// Reglas de bloqueo vigentes para el par (tenant, OT destino). Si el OT no se puede resolver
    /// devuelve <see cref="ConsultationBlockingRules.None"/>: sin OT no hay política configurada y
    /// cada criterio aplica su default (coherente con <c>ConsultationRestrictionPolicy</c>).
    /// </summary>
    Task<ConsultationBlockingRules> GetAsync(
        Guid tenantId,
        Guid? transitOfficeId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Implementación que aplica SIEMPRE los defaults por criterio — permisiva para tests de aplicación
/// que no ejercitan la configurabilidad por OT.
/// </summary>
public sealed class NullConsultationBlockingPolicy : IConsultationBlockingPolicy
{
    public static NullConsultationBlockingPolicy Instance { get; } = new();

    public Task<ConsultationBlockingRules> GetAsync(
        Guid tenantId,
        Guid? transitOfficeId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(ConsultationBlockingRules.None);
}
