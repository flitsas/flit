namespace Flit.Modules.Security.Application.Auth;

/// <summary>
/// Tipo de dominio de la petición actual (ADR-0060 D2, HU #12417 AC2). Lo puebla
/// <c>DomainContextMiddleware</c> (Flit.Api) leyendo EXCLUSIVAMENTE el sello <c>X-Flit-Domain</c>
/// que fija <c>Flit.Gateway</c> — nunca un valor que traiga el cliente sin sellar.
/// </summary>
public enum DomainKind
{
    /// <summary>Dominio de FLIT (o sin sello — fail-closed a FLIT, AC2).</summary>
    Flit,

    /// <summary>Dominio dedicado de una red MARCA_BLANCA activa.</summary>
    Network,
}

/// <summary>
/// Punto único de lectura del dominio resuelto de la petición actual (HU #12417 AC2). El Feature
/// #12369 (HU #12422) lo consumirá para ligar login/recuperación a la red sin acoplar Application
/// a <c>HttpContext</c>. Implementación HTTP en <c>Flit.Api.Authorization.HttpDomainContextAccessor</c>,
/// sobre lo que deja <c>Flit.Api.Middleware.DomainContextMiddleware</c> en
/// <see cref="Microsoft.AspNetCore.Http.HttpContext.Items"/>.
/// </summary>
public interface IDomainContextAccessor
{
    DomainKind Kind { get; }

    /// <summary>Host normalizado del sello (minúsculas, sin puerto), o <c>null</c> sin sello.</summary>
    string? Host { get; }

    /// <summary><see cref="DomainKind.Network"/> ⇒ tenant de la cabeza MARCA_BLANCA dueña del dominio.</summary>
    Guid? HeadTenantId { get; }
}
