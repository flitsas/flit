using System.Diagnostics.CodeAnalysis;

namespace Flit.Queries.Domain.Tenancy;

/// <summary>
/// Alcance de lectura/escritura del cliente (tenant) de una petición (HU #12321, Feature #12254,
/// ADR-0057). Value object inmutable y cerrado por defecto: nunca representa "todos" salvo por la
/// fábrica <see cref="All"/>, que es <c>internal</c> y solo la usa el middleware para SuperAdmin.
/// <list type="bullet">
///   <item><see cref="Single"/>: cliente sin jerarquía — lee y escribe sobre sí mismo.</item>
///   <item><see cref="Group"/>: cabeza de grupo — lee {padre} ∪ hijos, escribe SOLO sobre el padre.</item>
///   <item><see cref="All"/>: SuperAdmin — sin filtro (<see cref="ReadTenantIds"/> vacío y
///   <see cref="WriteTenantId"/> <c>null</c> únicamente aquí).</item>
/// </list>
/// El alcance NUNCA viaja en la petición (headers/body/token): se calcula en el servidor desde la
/// base de datos (<see cref="ITenantScopeResolver"/>).
/// </summary>
public sealed class TenantScope
{
    private static readonly IReadOnlySet<Guid> EmptySet = new HashSet<Guid>();

    private TenantScope(Guid? writeTenantId, IReadOnlySet<Guid> readTenantIds, bool isAll, bool isGroup)
    {
        WriteTenantId = writeTenantId;
        ReadTenantIds = readTenantIds;
        IsAll = isAll;
        IsGroup = isGroup;
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
    /// Alcance total (SuperAdmin). <c>internal</c> a propósito: ningún resolver ni capa de aplicación
    /// puede fabricarlo; solo el middleware (<c>InternalsVisibleTo Flit.Api</c>).
    /// </summary>
    internal static TenantScope All() => new(writeTenantId: null, EmptySet, isAll: true, isGroup: false);

    /// <summary>Cliente sin jerarquía: lee y escribe únicamente sobre <paramref name="tenantId"/>.</summary>
    [SuppressMessage("Naming", "CA1720:Identifier contains type name",
        Justification = "Nombre canónico del diseño (ADR-0057 / HU #12321): Single | Group | All.")]
    public static TenantScope Single(Guid tenantId)
    {
        RejectEmpty(tenantId, nameof(tenantId));
        return new TenantScope(tenantId, new HashSet<Guid> { tenantId }, isAll: false, isGroup: false);
    }

    /// <summary>
    /// Cabeza de grupo: lee {<paramref name="parentTenantId"/>} ∪ <paramref name="childTenantIds"/>,
    /// escribe solo sobre el padre. Si la colección de hijos está vacía, degrada a
    /// <see cref="Single"/> (una cabeza sin hijos no es un grupo).
    /// </summary>
    public static TenantScope Group(Guid parentTenantId, IReadOnlyCollection<Guid> childTenantIds)
    {
        RejectEmpty(parentTenantId, nameof(parentTenantId));
        ArgumentNullException.ThrowIfNull(childTenantIds);

        var read = new HashSet<Guid> { parentTenantId };
        foreach (var child in childTenantIds)
        {
            RejectEmpty(child, nameof(childTenantIds));
            read.Add(child);
        }

        return read.Count == 1
            ? Single(parentTenantId)
            : new TenantScope(parentTenantId, read, isAll: false, isGroup: true);
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
            ? $"TenantScope.Group(write={WriteTenantId}, read={ReadTenantIds.Count})"
            : $"TenantScope.Single({WriteTenantId})";
}
