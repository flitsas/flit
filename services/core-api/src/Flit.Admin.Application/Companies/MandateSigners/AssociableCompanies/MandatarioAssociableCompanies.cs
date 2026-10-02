using Flit.Admin.Domain.Companies;
using Flit.Admin.Domain.Companies.MandateSigners;

namespace Flit.Admin.Application.Companies.MandateSigners.AssociableCompanies;

/// <summary>
/// HU #13178 (Feature #13119 F7) — servicio único de compañías asociables a un mandatario según el perfil
/// (<see cref="IMandatarioAssociableCompanies"/>). La lectura cross-tenant vive en el puerto
/// <see cref="IManagingCompanyDirectory"/>; aquí se aplican los filtros, la deduplicación por NIT y la paginación,
/// que son reglas de negocio y se prueban sin base de datos.
///
/// <para><b>Datos mínimos (Ley 1581):</b> solo id, nombre y NIT salen de aquí. <b>No</b> usa ni modifica
/// <c>OtVisibleCompanies</c> / <c>OtCompanyVisibilityPolicy</c> (bandeja de trámites, Bug #12912).</para>
/// </summary>
public sealed class MandatarioAssociableCompanies : IMandatarioAssociableCompanies
{
    public const int MinSearchLength = 2;
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    /// <summary>
    /// Valor centinela de <c>pageSize</c> para pedir la lista completa (<c>?all=true</c>, selector con buscador).
    /// Tiene tope: <see cref="MaxAllItems"/>. Un <c>pageSize</c> normal sigue limitado a <see cref="MaxPageSize"/>.
    /// </summary>
    public const int AllItems = -1;

    public const int MaxAllItems = 1000;

    public const string SearchTooShortMessage = "La búsqueda debe tener al menos 2 caracteres.";

    private readonly IManagingCompanyDirectory _directory;
    private readonly ICompanyHierarchyRepository _hierarchy;

    public MandatarioAssociableCompanies(IManagingCompanyDirectory directory, ICompanyHierarchyRepository hierarchy)
    {
        _directory = directory ?? throw new ArgumentNullException(nameof(directory));
        _hierarchy = hierarchy ?? throw new ArgumentNullException(nameof(hierarchy));
    }

    public async Task<AssociableCompaniesResult> ListForOtAsync(
        string? search, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        if (!TryParseSearch(search, out var term))
        {
            return new AssociableCompaniesResult(null, SearchTooShortMessage);
        }

        var all = await _directory.ListAsync(cancellationToken).ConfigureAwait(false);
        var candidates = DeduplicateByNit(all.Where(c => c.IsActive))
            .Select(c => new AssociableCompany(c.Id, c.Name, c.Nit))
            .ToList();

        return new AssociableCompaniesResult(Paginate(Filter(candidates, term), page, pageSize, aplicaSolo: false), null);
    }

    public async Task<AssociableCompaniesResult> ListForCompanyAsync(
        Guid companyTenantId, string? search, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        if (!TryParseSearch(search, out var term))
        {
            return new AssociableCompaniesResult(null, SearchTooShortMessage);
        }

        var children = await ActiveChildrenAsync(companyTenantId, cancellationToken).ConfigureAwait(false);

        // «Sin red» se decide por las hijas activas, no por lo que deje la búsqueda.
        var page0 = Paginate(Filter(children, term), page, pageSize, aplicaSolo: children.Count == 0);
        return new AssociableCompaniesResult(page0, null);
    }

    public async Task<IReadOnlyDictionary<Guid, string>> RejectionsAsync(
        Guid? scopeCompanyTenantId,
        IReadOnlyCollection<Guid> ownerCompanyTenantIds,
        IReadOnlyCollection<Guid> candidateTenantIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ownerCompanyTenantIds);
        ArgumentNullException.ThrowIfNull(candidateTenantIds);

        var result = new Dictionary<Guid, string>();
        if (candidateTenantIds.Count == 0)
        {
            return result;
        }

        var all = (await _directory.ListAsync(cancellationToken).ConfigureAwait(false)).ToDictionary(c => c.Id);
        // El alcance del Admin de Compañía son sus hijas directas (activas o no): una hija inactiva se rechaza por
        // inactiva (422); solo lo que no es hija es «fuera de alcance» (403).
        var children = scopeCompanyTenantId is { } scope
            ? (await _hierarchy.ListChildrenAsync(scope, cancellationToken).ConfigureAwait(false)).Select(c => c.Id).ToHashSet()
            : null;

        foreach (var id in candidateTenantIds.Distinct())
        {
            if (!all.TryGetValue(id, out var company))
            {
                result[id] = AssociableCompanyRejections.CompaniaInexistente;
            }
            else if (ownerCompanyTenantIds.Contains(id))
            {
                result[id] = AssociableCompanyRejections.CompaniaPropia;
            }
            else if (children is not null && !children.Contains(id))
            {
                // Existe pero no es hija directa: el Admin de Compañía no puede asociarla.
                result[id] = AssociableCompanyRejections.FueraDeAlcance;
            }
            else if (!company.IsActive)
            {
                result[id] = AssociableCompanyRejections.CompaniaInactiva;
            }
        }

        return result;
    }

    private async Task<List<AssociableCompany>> ActiveChildrenAsync(Guid companyTenantId, CancellationToken ct)
    {
        var children = await _hierarchy.ListChildrenAsync(companyTenantId, ct).ConfigureAwait(false);
        return
        [
            .. children
                .Where(c => c.EstadoActivo)
                .Select(c => new AssociableCompany(c.Id, c.RazonSocial, c.Nit))
                .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase),
        ];
    }

    /// <summary>Texto ya recortado, o <c>null</c> si no hay búsqueda. <c>false</c> si trae menos de 2 caracteres.</summary>
    private static bool TryParseSearch(string? raw, out string? term)
    {
        term = string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
        return term is null || term.Length >= MinSearchLength;
    }

    private static List<AssociableCompany> Filter(List<AssociableCompany> companies, string? term)
    {
        if (term is null)
        {
            return companies;
        }

        // Todo es comparación en memoria con IndexOf ordinal: el texto se trata como literal (sin LIKE ni SQL).
        var nitTerm = NitDigits(term);
        return
        [
            .. companies.Where(c =>
                c.Name.Contains(term, StringComparison.CurrentCultureIgnoreCase)
                || (nitTerm.Length >= MinSearchLength
                    && NitDigits(c.Nit).Contains(nitTerm, StringComparison.Ordinal))),
        ];
    }

    private static AssociableCompaniesPage Paginate(
        List<AssociableCompany> companies, int page, int pageSize, bool aplicaSolo)
    {
        var size = pageSize == AllItems
            ? MaxAllItems
            : pageSize <= 0 ? DefaultPageSize : Math.Min(pageSize, MaxPageSize);
        var number = pageSize == AllItems ? 1 : Math.Max(1, page);
        var items = companies.Skip((number - 1) * size).Take(size).ToList();
        return new AssociableCompaniesPage(items, companies.Count, number, size, aplicaSolo);
    }

    /// <summary>Sin duplicados por NIT: se queda la compañía activa más antigua (desempate por id).</summary>
    private static IEnumerable<ManagingCompanyRow> DeduplicateByNit(IEnumerable<ManagingCompanyRow> rows) =>
        rows.GroupBy(r => NitKey(r.Nit) is { Length: > 0 } key ? key : "id:" + r.Id)
            .Select(g => g.OrderBy(r => r.CreatedAt).ThenBy(r => r.Id).First())
            .OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase);

    /// <summary>Solo dígitos del NIT, sin puntos, espacios ni el guion del dígito de verificación.</summary>
    internal static string NitDigits(string? nit) =>
        string.IsNullOrEmpty(nit) ? string.Empty : new string([.. nit.Where(char.IsDigit)]);

    /// <summary>Clave de deduplicación: el NIT sin dígito de verificación (lo que va antes del guion).</summary>
    internal static string NitKey(string? nit)
    {
        if (string.IsNullOrWhiteSpace(nit))
        {
            return string.Empty;
        }

        var dash = nit.IndexOf('-', StringComparison.Ordinal);
        return NitDigits(dash >= 0 ? nit[..dash] : nit);
    }
}
