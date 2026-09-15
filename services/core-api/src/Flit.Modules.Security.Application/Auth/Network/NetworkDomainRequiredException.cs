namespace Flit.Modules.Security.Application.Auth.Network;

/// <summary>
/// Login válido por el dominio de FLIT de un usuario cuyo tenant pertenece a una red MARCA_BLANCA
/// CON dominio activo (HU #12422 AC3, ADR-0060 D3). Se lanza SOLO tras verificar la credencial
/// (nunca antes): con credencial inválida el caller recibe el 401 genérico, sin mención de ningún
/// dominio. <c>Flit.Api.Endpoints.AuthEndpoints</c> la traduce a <c>403 NETWORK_DOMAIN_REQUIRED</c>
/// (aditivo, AC7 — ruta y verbo intactos).
/// </summary>
public sealed class NetworkDomainRequiredException(string networkDomain) : Exception
{
    public string NetworkDomain { get; } = networkDomain;
}
