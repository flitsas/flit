using System.Diagnostics.CodeAnalysis;

namespace Flit.Queries.Domain.Tenancy;

/// <summary>
/// Alcance de lectura/escritura del cliente (tenant) de una petición (HU #12321, Feature #12254,
/// ADR-0057). Value object inmutable y cerrado por defecto: nunca representa "todos" salvo por la
/// fábrica <see cref="All"/>, que es <c>internal</c> y solo la usa el middleware para SuperAdmin.
/// <list type="bullet">
///   <item><see cref="Single"/>: cliente sin jerarquía — lee y escribe sobre sí mismo.</item>
///   <item><see cref="Group"/>: cabeza de grupo — lee {padre} ∪ hijos, escribe SOLO sobre el padre y
///   expone la clase de la cabeza (<see cref="GroupKind"/>, HU #12406) leída de la base de datos.</item>
///   <item><see cref="All"/>: SuperAdmin — sin filtro (<see cref="ReadTenantIds"/> vacío y
///   <see cref="WriteTenantId"/> <c>null</c> únicamente aquí).</item>
/// </list>
/// El alcance NUNCA viaja en la petición (headers/body/token): se calcula en el servidor desde la
/// base de datos (<see cref="ITenantScopeResolver"/>).
/// </summary>
public sealed class TenantScope
{
    private static readonly IReadOnlySet<Guid> EmptySet = new HashSet<Guid>();

    private TenantScope(Guid? writeTenantId, IReadOnlySet<Guid> readTenantIds, bool isAll, bool isGroup, GroupKind? groupKind)
    {
        WriteTenantId = writeTenantId;
        ReadTenantIds = readTenantIds;
        IsAll = isAll;
        IsGroup = isGroup;
        GroupKind = groupKind;
    }

    /// <summary>Tenant sobre el que se puede escribir. <c>null</c> SOLO en <see cref="All"/>.</summary>
    public Guid? WriteTenantId { get; }

    /// <summary>Tenants que se pueden leer. Vacío SOLO en <see cref="All"/>.</summary>
    public IReadOnlySet<Guid> ReadTenantIds { get; }

    /// <summary><c>true</c> = sin filtro (SuperAdmin).</summary>
    public bool IsAll { get; }

    /// <summary><c>true</c> = cabeza de grupo con hijos.</summary>
    public bool IsGroup { get; }

    /// <summary>
    /// Clase de la cabeza de grupo (HU #12406): Concesión o Marca Blanca. Con valor SOLO cuando
    /// <see cref="IsGroup"/>; <c>null</c> en <see cref="Single"/> y <see cref="All"/>. Sale de
    /// <c>identity.tenants.tenant_type</c> (CONCESION | MARCA_BLANCA) vía <see cref="ITenantScopeResolver"/>,
    /// nunca de la petición.
    /// </summary>
    public GroupKind? GroupKind { get; }

    /// <summary>
    /// Alcance total (SuperAdmin). <c>internal</c> a propósito: ningún resolver ni capa de aplicación
    /// puede fabricarlo; solo el middleware (<c>InternalsVisibleTo Flit.Api</c>).
    /// </summary>
    internal static TenantScope All() => new(writeTenantId: null, EmptySet, isAll: true, isGroup: false, groupKind: null);

    /// <summary>Cliente sin jerarquía: lee y escribe únicamente sobre <paramref name="tenantId"/>.</summary>
    [SuppressMessage("Naming", "CA1720:Identifier contains type name",
        Justification = "Nombre canónico del diseño (ADR-0057 / HU #12321): Single | Group | All.")]
    public static TenantScope Single(Guid tenantId)
    {
        RejectEmpty(tenantId, nameof(tenantId));
        return new TenantScope(tenantId, new HashSet<Guid> { tenantId }, isAll: false, isGroup: false, groupKind: null);
    }

    /// <summary>
    /// Cabeza de grupo: lee {<paramref name="parentTenantId"/>} ∪ <paramref name="childTenantIds"/>,
    /// escribe solo sobre el padre y expone la clase <paramref name="kind"/> de la cabeza (HU #12406).
    /// Si la colección de hijos está vacía, degrada a <see cref="Single"/> (una cabeza sin hijos no
    /// es un grupo) y la clase no se expone.
    /// </summary>
    public static TenantScope Group(Guid parentTenantId, IReadOnlyCollection<Guid> childTenantIds, GroupKind kind)
    {
        RejectEmpty(parentTenantId, nameof(parentTenantId));
        ArgumentNullException.ThrowIfNull(childTenantIds);
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Clase de cabeza de grupo desconocida.");

        var read = new HashSet<Guid> { parentTenantId };
        foreach (var child in childTenantIds)
        {
            RejectEmpty(child, nameof(childTenantIds));
            read.Add(child);
        }

        return read.Count == 1
            ? Single(parentTenantId)
            : new TenantScope(parentTenantId, read, isAll: false, isGroup: true, kind);
    }

    /// <summary><c>true</c> si el alcance permite leer datos de <paramref name="tenantId"/>.</summary>
    public bool CanRead(Guid tenantId) => IsAll || ReadTenantIds.Contains(tenantId);

    /// <summary>
    /// <c>true</c> si el alcance permite escribir sobre <paramref name="tenantId"/>. Para un grupo,
    /// SOLO el padre: la lectura consolidada no otorga escritura sobre los hijos.
    /// </summary>
    public bool CanWrite(Guid tenantId) => IsAll || (WriteTenantId.HasValue && WriteTenantId.Value == tenantId);

    private static void RejectEmpty(Guid value, string paramName)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("El identificador de tenant no puede ser Guid.Empty.", paramName);
    }

    public override string ToString() => IsAll
        ? "TenantScope.All"
        : IsGroup
            ? $"TenantScope.Group(write={WriteTenantId}, read={ReadTenantIds.Count}, kind={GroupKind})"
            : $"TenantScope.Single({WriteTenantId})";
}
