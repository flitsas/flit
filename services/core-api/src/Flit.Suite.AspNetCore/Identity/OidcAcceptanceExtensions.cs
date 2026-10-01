using Microsoft.EntityFrameworkCore;

namespace Flit.Api.Identity;

/// <summary>
/// Epic #13217 (HU #13232) — lo que necesita un proceso que <b>acepta</b> tokens del hub sin emitirlos: los almacenes de
/// OpenIddict (para saber si la sesión de un token se cerró) y la aceptación del emisor y la audiencia. core-api lo usa
/// sobre <c>FlitDbContext</c>; core-identity, sobre su propio contexto, junto con el servidor OIDC.
/// </summary>
public static class OidcAcceptanceExtensions
{
    /// <summary>Con <c>Suite:Oidc:Enabled</c> apagada solo liga las opciones: nada más cambia.</summary>
    public static IServiceCollection AddFlitOidcAcceptance<TContext>(this IServiceCollection services, IConfiguration configuration)
        where TContext : DbContext
    {
        var section = configuration.GetSection(OidcOptions.SectionName);
        services.Configure<OidcOptions>(section);
        if (!(section.Get<OidcOptions>() ?? new OidcOptions()).Enabled)
            return services;

        // Sin este núcleo, OidcSessionCheck no encuentra el gestor de autorizaciones y deja pasar tokens de sesiones
        // ya cerradas: por eso va siempre junto con la aceptación.
        services.AddOpenIddict()
            .AddCore(core => core.UseEntityFrameworkCore().UseDbContext<TContext>().ReplaceDefaultEntities<Guid>());
        OidcTokenAcceptance.Register(services); // HU #12992 (A-07): la API acepta los tokens del hub
        return services;
    }
}
