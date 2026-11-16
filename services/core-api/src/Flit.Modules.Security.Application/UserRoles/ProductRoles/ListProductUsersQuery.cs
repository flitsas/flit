using System.Globalization;
using System.Text;
using Flit.Modules.Security.Domain.UserRoles.ProductRoles;

namespace Flit.Modules.Security.Application.UserRoles.ProductRoles;

/// <param name="Page">1 en adelante; 0 = 1.</param>
/// <param name="PageSize">1..100; 0 = 25. Además Page × PageSize ≤ 10 000.</param>
public sealed record ListProductUsersQuery(
    Guid TenantId,
    ProductActor Actor,
    string ProductCode,
    string? Search,
    IReadOnlyList<string> Roles,
    IReadOnlyList<ProductUserState> States,
    int Page,
    int PageSize);

public sealed record ProductUsersPage(IReadOnlyList<ProductUserRow> Users, int Total, int Page, int PageSize, IReadOnlyList<string> AssignableRoles);

/// <summary>
/// ADR-0089 — listado de usuarios del producto de una empresa: miembros con rol vigente o suspendido en el producto e
/// invitaciones pendientes con rol del producto (SP-18: solo id, nombre, correo, rol del producto y estado). Las acciones
/// se calculan para el actor. Orden: activos, invitados, suspendidos; luego nombre.
/// <para>El filtro y la página se aplican en memoria: el universo es el de UNA empresa en UN producto (decenas o cientos
/// de filas) y así la búsqueda sin tildes no depende de <c>unaccent</c> en la base.</para>
/// </summary>
public sealed class ListProductUsersHandler(IProductRoleRepository repo)
{
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 100;
    public const int MaxWindow = 10_000;
    public const int MaxSearch = 100;

    public async Task<ProductUsersPage> HandleAsync(ListProductUsersQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var page = query.Page == 0 ? 1 : query.Page;
        var size = query.PageSize == 0 ? DefaultPageSize : query.PageSize;
        if (page < 1 || size < 1 || size > MaxPageSize || (long)page * size > MaxWindow)
            throw ProductRoleRules.Fail(ProductRoleErrorCodes.PaginacionInvalida, ProductRoleErrorKind.InvalidArgument, "Paginación fuera de rango.");

        var search = query.Search?.Trim();
        if (search is { Length: > MaxSearch })
            throw ProductRoleRules.Fail(ProductRoleErrorCodes.FiltroInvalido, ProductRoleErrorKind.InvalidArgument, "La búsqueda admite hasta 100 caracteres.");
        var roles = query.Roles ?? [];
        if (roles.Any(r => !ProductRoleRules.IsAllowedRole(query.ProductCode, r)))
            throw ProductRoleRules.Fail(ProductRoleErrorCodes.FiltroInvalido, ProductRoleErrorKind.InvalidArgument, "Rol desconocido en el filtro.");
        var states = query.States ?? [];

        var members = await repo.ListMembersAsync(query.TenantId, query.ProductCode, ct).ConfigureAwait(false);
        var invitations = await repo.ListPendingInvitationsAsync(query.TenantId, query.ProductCode, ct).ConfigureAwait(false);

        var needle = string.IsNullOrEmpty(search) ? null : Fold(search);
        var rows = members.Select(m => ProductRoleRules.RowFor(query.Actor, query.ProductCode, m))
            .Concat(invitations.Select(ProductRoleRules.RowFor))
            .Where(r => needle is null || Fold(r.Name).Contains(needle, StringComparison.Ordinal) || Fold(r.Email).Contains(needle, StringComparison.Ordinal))
            .Where(r => roles.Count == 0 || roles.Contains(r.RoleCode, StringComparer.Ordinal))
            .Where(r => states.Count == 0 || states.Contains(r.State))
            .OrderBy(r => Rank(r.State))
            .ThenBy(r => Fold(r.Name), StringComparer.Ordinal)
            .ThenBy(r => r.Id)
            .ToList();

        var slice = rows.Skip((page - 1) * size).Take(size).ToList();
        return new ProductUsersPage(slice, rows.Count, page, size, ProductRoleRules.AssignableRoles(query.Actor, query.ProductCode));
    }

    /// <summary>Conteos por empresa del producto, para la tabla de compañías del SuperAdmin.</summary>
    public Task<IReadOnlyList<ProductTenantCount>> CountByTenantAsync(string productCode, CancellationToken ct) =>
        repo.CountByTenantAsync(productCode, ProductRoleRules.AdminRole(productCode), ct);

    private static int Rank(ProductUserState state) => state switch
    {
        ProductUserState.Active => 0,
        ProductUserState.Invited => 1,
        _ => 2,
    };

    /// <summary>
    /// Minúsculas sin tildes (á→a, ñ→n) para «contiene» insensible a mayúsculas y diacríticos. Las vocales y letras del
    /// español se mapean a mano: con globalización invariante <c>Normalize(FormD)</c> no descompone y la tilde quedaría.
    /// </summary>
    internal static string Fold(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(SinTilde(char.ToLowerInvariant(c)));
        }

        return sb.ToString();
    }

    private static char SinTilde(char c) => c switch
    {
        'á' or 'à' or 'ä' or 'â' or 'ã' => 'a',
        'é' or 'è' or 'ë' or 'ê' => 'e',
        'í' or 'ì' or 'ï' or 'î' => 'i',
        'ó' or 'ò' or 'ö' or 'ô' or 'õ' => 'o',
        'ú' or 'ù' or 'ü' or 'û' => 'u',
        'ñ' => 'n',
        'ç' => 'c',
        _ => c,
    };
}
