namespace Flit.Admin.Application.Companies.Branding;

/// <summary>
/// Invalidación explícita de la caché de resolución pública/sesión de marca (HU #12418 AC7).
/// Implementación en Infrastructure, sobre el MISMO <see cref="Microsoft.Extensions.Caching.Memory.IMemoryCache"/>
/// singleton que consume <c>ResolvePublicBrandingHandler</c> (vía <c>IPublicBrandingCache</c>) — la
/// clave es el <c>tenantId</c> de la cabeza, no el host (un host resuelve siempre al mismo tenant,
/// ADR-0060 D2, así que invalidar por tenant cubre el mismo caso e independiza esta invalidación de
/// tener que buscar el host actual). Se invoca desde:
/// <list type="bullet">
///   <item><c>PublishBrandingHandler</c> / <c>RetireBrandingHandler</c> (#12412) — la marca cambió.</item>
///   <item><c>CompanyWriteRepository</c> al cambiar <c>tenant_type</c> — la cabeza dejó (o empezó) de
///   ser MARCA_BLANCA/cabeza de grupo/activa (AC7 "apagar la clase invalida la caché").</item>
/// </list>
/// </summary>
public interface IBrandingCacheInvalidator
{
    void InvalidateTenant(Guid tenantId);
}

/// <summary>
/// Objeto nulo (mismo patrón que <c>NullAuditContextAccessor</c>): sitios de llamada que no reciben
/// invalidador por DI (tests, o compilación antes de que Infrastructure registre el real) no rompen —
/// simplemente no invalidan nada.
/// </summary>
public sealed class NullBrandingCacheInvalidator : IBrandingCacheInvalidator
{
    public static readonly NullBrandingCacheInvalidator Instance = new();

    private NullBrandingCacheInvalidator()
    {
    }

    public void InvalidateTenant(Guid tenantId)
    {
    }
}
