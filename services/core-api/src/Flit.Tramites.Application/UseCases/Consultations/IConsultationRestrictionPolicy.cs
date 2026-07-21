namespace Flit.Tramites.Application.UseCases.Consultations;

/// <summary>
/// Consultas que la compañía inhabilitó para el OT destino del trámite (HU #10760). Conjunto
/// case-insensitive de <see cref="ConsultationRestrictionKinds"/>; vacío = nada restringido.
/// </summary>
public sealed record ConsultationRestrictions(IReadOnlyDictionary<string, bool> Settings)
{
    /// <summary>Sin filas configuradas: tabla dispersa, ausencia de fila = sin ajuste explícito.</summary>
    public static readonly ConsultationRestrictions None =
        new(new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// Construye desde los ajustes explícitos por kind (kind → enabled) de la BD, normalizando a
    /// comparación case-insensitive. Cada fila representa una decisión EXPLÍCITA de la compañía:
    /// <c>enabled=true</c> = consultar, <c>enabled=false</c> = no consultar.
    /// </summary>
    public static ConsultationRestrictions FromSettings(IEnumerable<KeyValuePair<string, bool>> settings)
    {
        var map = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var (kind, enabled) in settings)
            map[kind] = enabled;
        return map.Count == 0 ? None : new ConsultationRestrictions(map);
    }

    /// <summary>Compat: construye a partir solo de los kinds inhabilitados (<c>enabled=false</c>).</summary>
    public static ConsultationRestrictions From(IEnumerable<string> disabledKinds) =>
        FromSettings(disabledKinds.Select(k => new KeyValuePair<string, bool>(k, false)));

    /// <summary>Kinds inhabilitados explícitamente (fila con <c>enabled=false</c>).</summary>
    public IEnumerable<string> DisabledKinds => Settings.Where(kv => !kv.Value).Select(kv => kv.Key);

    /// <summary>¿El kind está inhabilitado (opt-out) por una fila explícita <c>enabled=false</c>?</summary>
    public bool IsDisabled(string kind) => Settings.TryGetValue(kind, out var enabled) && !enabled;

    /// <summary>
    /// Ajuste EXPLÍCITO de la compañía para el kind: <c>true</c> consultar, <c>false</c> no consultar,
    /// <c>null</c> si no hay fila. Lo consume el RNMC como opt-in por compañía+OT (FEATURE 05).
    /// </summary>
    public bool? SettingOf(string kind) => Settings.TryGetValue(kind, out var enabled) ? enabled : null;
}

/// <summary>
/// Puerto para resolver QUÉ consultas NO debe correr el preflight para un trámite, según la política
/// comercial que la compañía fijó sobre el OT destino
/// (<c>admin.tenant_transit_office_consultation_restrictions</c>, HU #10759). Desacopla Trámites de
/// Admin (mismo patrón que <c>IRnmcRequirementPolicy</c>).
///
/// Eje ORTOGONAL al de <c>IRnmcRequirementPolicy</c>: el OT declara qué EXIGE, la compañía qué NO
/// quiere consultar. Una consulta corre si el OT la exige Y la compañía no la restringió.
///
/// Distinto también de <c>IConsultationTenantOverrideProvider</c>, que responde QUÉ proveedor
/// atiende la consulta (por tenant, sin OT); este responde SI la consulta se hace (por tenant + OT).
/// </summary>
public interface IConsultationRestrictionPolicy
{
    /// <summary>
    /// Restricciones vigentes para el par (tenant, OT destino). Si el OT no se puede resolver
    /// devuelve <see cref="ConsultationRestrictions.None"/>: sin OT no hay política que aplicar, y
    /// el default de la tabla es permisivo (coherente con <c>RnmcRequirementPolicy</c>).
    /// </summary>
    Task<ConsultationRestrictions> GetAsync(
        Guid tenantId,
        Guid? transitOfficeId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Implementación que NUNCA restringe — default permisivo para tests de aplicación que no ejercitan
/// la configurabilidad por OT.
/// </summary>
public sealed class NullConsultationRestrictionPolicy : IConsultationRestrictionPolicy
{
    public static NullConsultationRestrictionPolicy Instance { get; } = new();

    public Task<ConsultationRestrictions> GetAsync(
        Guid tenantId,
        Guid? transitOfficeId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(ConsultationRestrictions.None);
}
