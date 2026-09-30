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
}
