namespace Flit.Admin.Domain.Companies.MandateSigners;

/// <summary>
/// HU #13178 (Feature #13119 F7) — compañía de FLIT a la que se puede asociar un mandatario. Proyección
/// MÍNIMA (Ley 1581): solo id (tenant), nombre (razón social) y NIT; nunca contactos ni otros datos.
/// </summary>
public sealed record AssociableCompany(Guid Id, string Name, string Nit);

/// <summary>
/// HU #13178 — página de compañías asociables. <see cref="AplicaSoloASuCompania"/> es verdadero cuando la
/// compañía que consulta no tiene red (sin hijas): no hay lista y el mandatario aplica solo a ella misma.
/// Siempre falso en la consulta del OT / Super Admin.
/// </summary>
public sealed record AssociableCompaniesPage(
    IReadOnlyList<AssociableCompany> Items,
    int Total,
    int Page,
    int PageSize,
    bool AplicaSoloASuCompania);

/// <summary>Resultado de la consulta: página o error de validación de la búsqueda (422).</summary>
public sealed record AssociableCompaniesResult(AssociableCompaniesPage? Page, string? SearchError)
{
    public bool IsValid => SearchError is null;
}

/// <summary>Motivo por el que una compañía no puede asociarse (422 por elemento en el guardado).</summary>
public static class AssociableCompanyRejections
{
    /// <summary>Es la propia compañía del mandatario.</summary>
    public const string CompaniaPropia = "compania_propia";

    /// <summary>La compañía existe pero está inactiva o bloqueada.</summary>
    public const string CompaniaInactiva = "compania_inactiva";

    /// <summary>El id no corresponde a ninguna compañía gestora de FLIT.</summary>
    public const string CompaniaInexistente = "compania_inexistente";

    /// <summary>Existe y está activa, pero el perfil del actor no puede asociarla (403 al guardar).</summary>
    public const string FueraDeAlcance = "compania_asociada_fuera_de_alcance";
}

/// <summary>
/// HU #13178 — servicio ÚNICO de compañías asociables a un mandatario según el perfil; lo reutiliza la
/// validación del guardado (HU #13179). OT y Super Admin ven TODAS las compañías gestoras activas; el Admin de
/// Compañía solo sus hijas directas activas. La lista completa nunca sale por la ruta de la compañía.
/// No reemplaza ni modifica la visibilidad de la bandeja de trámites (<c>OtVisibleCompanies</c>, Bug #12912).
/// </summary>
public interface IMandatarioAssociableCompanies
{
    /// <summary>
    /// Compañías gestoras activas de FLIT para el OT / Super Admin, sin duplicados por NIT, con búsqueda por
    /// nombre o NIT (mínimo 2 caracteres; texto literal; el NIT ignora puntos y guion de verificación).
    /// </summary>
    Task<AssociableCompaniesResult> ListForOtAsync(
        string? search, int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>
    /// Hijas directas activas de <paramref name="companyTenantId"/> (jerarquía <c>parent_tenant_id</c>). Sin hijas:
    /// lista vacía y <see cref="AssociableCompaniesPage.AplicaSoloASuCompania"/> verdadero.
    /// </summary>
    Task<AssociableCompaniesResult> ListForCompanyAsync(
        Guid companyTenantId, string? search, int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>
    /// Evalúa ids candidatos (HU #13179) contra el mismo universo que las listas. Con
    /// <paramref name="scopeCompanyTenantId"/> nulo (OT / Super Admin) vale cualquier compañía gestora activa;
    /// con valor (Admin de Compañía) solo sus hijas directas activas. <paramref name="ownerCompanyTenantIds"/> son
    /// las compañías propietarias del mandatario: asociarse a una propia se rechaza. Devuelve, por id que NO pasa,
    /// el motivo (<see cref="AssociableCompanyRejections"/>); los ids válidos no aparecen.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, string>> RejectionsAsync(
        Guid? scopeCompanyTenantId,
        IReadOnlyCollection<Guid> ownerCompanyTenantIds,
        IReadOnlyCollection<Guid> candidateTenantIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Evalúa las compañías PROPIETARIAS de un mandatario cuando lo registra el OT o el Super Admin (HU #13182b). A
    /// diferencia de <see cref="RejectionsAsync"/>, el tenant de la propia plataforma (tipo <c>FLIT</c>) SÍ puede tener
    /// mandatarios (decisión del PO, 02-oct-2026): basta con que exista, esté activo y no sea un organismo de tránsito.
    /// Devuelve, por id que NO pasa, <see cref="AssociableCompanyRejections.CompaniaInexistente"/> o
    /// <see cref="AssociableCompanyRejections.CompaniaInactiva"/>; los ids válidos no aparecen.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, string>> OwnerRejectionsAsync(
        IReadOnlyCollection<Guid> ownerTenantIds, CancellationToken cancellationToken = default);
}

/// <summary>Compañía gestora activa, con el dato de creación para deduplicar por NIT de forma estable.</summary>
public sealed record ManagingCompanyRow(Guid Id, string Name, string Nit, DateTimeOffset CreatedAt, bool IsActive);

/// <summary>
/// HU #13178 — puerto de lectura cross-tenant de las compañías gestoras de FLIT (excluye organismos de tránsito y el
/// tenant de la plataforma). Proyección mínima; lo implementa Infraestructura.
/// </summary>
public interface IManagingCompanyDirectory
{
    /// <summary>Todas las compañías gestoras (activas e inactivas: el servicio distingue el motivo de rechazo).</summary>
    Task<IReadOnlyList<ManagingCompanyRow>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Solo las compañías indicadas, en UNA consulta (sin N+1): lo usa la lectura del mandatario para mostrar nombre y
    /// NIT de sus compañías asociadas. Los ids que no son compañías gestoras no aparecen.
    /// </summary>
    Task<IReadOnlyList<ManagingCompanyRow>> ListByIdsAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);

    /// <summary>
    /// Como <see cref="ListByIdsAsync"/> pero INCLUYE el tenant de la plataforma (tipo <c>FLIT</c>): lo usa solo la
    /// validación de las compañías propietarias de un mandatario (HU #13182b). Sigue excluyendo los organismos de tránsito.
    /// </summary>
    Task<IReadOnlyList<ManagingCompanyRow>> ListOwnerCandidatesByIdsAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);
}

/// <summary>
/// HU #13179 (AC2) — un Admin de Compañía envió una compañía asociada que no es su hija: la API responde
/// 403 <c>compania_asociada_fuera_de_alcance</c> y no guarda nada. El mensaje no lleva datos de la compañía ajena.
/// </summary>
public sealed class AssociatedCompanyOutOfScopeException : Exception
{
    public const string Code = "compania_asociada_fuera_de_alcance";

    public const string DefaultMessage = "Solo puede asociar compañías de su propia red.";

    public AssociatedCompanyOutOfScopeException()
        : base(DefaultMessage)
    {
    }

    public AssociatedCompanyOutOfScopeException(string message)
        : base(message)
    {
    }

    public AssociatedCompanyOutOfScopeException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
