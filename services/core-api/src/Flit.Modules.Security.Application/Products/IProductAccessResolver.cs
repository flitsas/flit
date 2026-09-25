namespace Flit.Modules.Security.Application.Products;

/// <summary>
/// Resuelve si un usuario puede entrar a un producto de la suite y con qué roles y permisos
/// (contrato de plataforma v1, §4).
/// </summary>
/// <remarks>
/// HU #12898 (B-00) — la interfaz la publica el frente B para que el frente A la consuma al
/// emitir el token por producto (A-07). La implementación real vive en
/// <c>Flit.Modules.Platform</c> (B-03 y B-05), que referencia a este proyecto; nunca al revés.
/// Mientras no exista, el frente A usa <c>StubProductAccessResolver</c> (<c>tramites</c>
/// habilitado para todos y los roles actuales del usuario).
/// <para>
/// El SuperAdmin no pasa por aquí: conserva el bypass en todos los productos (contrato §2.1),
/// así que quien emite el token lo detecta antes de llamar a este resolver.
/// </para>
/// </remarks>
public interface IProductAccessResolver
{
    /// <param name="userId">Usuario que pide el token.</param>
    /// <param name="tenantId">Empresa del usuario.</param>
    /// <param name="productCode">Uno de <see cref="ProductCodes"/>.</param>
    Task<ProductAccess> ResolveAsync(Guid userId, Guid tenantId, string productCode, CancellationToken ct);
}

/// <summary>
/// Acceso de un usuario a un producto.
/// </summary>
/// <param name="ProductEnabled">
/// El producto está encendido para la empresa y, si tiene cabeza de jerarquía, también para
/// ella (regla fail-closed de ADR-0057). No es una suscripción comercial: no hay fechas ni estados.
/// </param>
/// <param name="Roles">Roles del usuario en ESTE producto. Vacío si no tiene ninguno.</param>
/// <param name="Permissions">Slugs de permiso del usuario en ESTE producto.</param>
/// <remarks>
/// Con <paramref name="ProductEnabled"/> en <c>false</c> el hub responde
/// <c>PRODUCT_NOT_ENABLED</c>; con <paramref name="Roles"/> vacío, <c>PRODUCT_ROLE_REQUIRED</c>
/// (contrato §10).
/// </remarks>
public sealed record ProductAccess(
    bool ProductEnabled,
    IReadOnlyList<RoleRef> Roles,
    IReadOnlyList<string> Permissions);

/// <summary>Referencia a un rol, con los mismos campos que el claim <c>roles</c> del token.</summary>
public sealed record RoleRef(Guid Id, string Code);
