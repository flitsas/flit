using Flit.Modules.Security.Domain.PermissionOverrides;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace Flit.Api.Authorization;

/// <summary>
/// Fuente adicional de permisos exigidos detrás de un flag (extensión de <see cref="PermissionEnforcementState"/>).
/// <para>
/// Cómo registrar una (p. ej. F-F para <c>matriculas.*</c>, <c>traspasos.*</c>, <c>otros-tramites.*</c>): crear una clase que
/// implemente esta interfaz, devolver los slugs que solo se exigen con su flag y leer el flag con
/// <c>IOptionsMonitor&lt;T&gt;</c> (lectura viva), y registrarla como singleton:
/// <c>builder.Services.AddSingleton&lt;IFlaggedPermissionSource, MiFuente&gt;();</c>. No hay que tocar el handler ni el estado.
/// </para>
/// </summary>
public interface IFlaggedPermissionSource
{
    /// <summary>Slugs que esta fuente protege con su flag.</summary>
    IReadOnlyCollection<string> Slugs { get; }

    /// <summary>Estado actual del flag (se consulta en cada evaluación).</summary>
    bool IsEnabled { get; }
}

/// <summary>
/// Implementación de <see cref="IPermissionEnforcementState"/>:
/// <list type="bullet">
/// <item><c>ot.*</c> (valores de <see cref="OtPermissionMap"/>) depende de <see cref="OtPermissionEnforcementOptions.Enabled"/>.</item>
/// <item>Slugs exigidos por endpoints con <see cref="ModulePermissionRequirement"/> o <see cref="ModuleAnyPermissionRequirement"/>
/// dependen de <see cref="ModulePermissionEnforcementOptions.Enabled"/>. Se descubren de la metadata de los endpoints
/// registrados (<see cref="EndpointDataSource"/>), de forma perezosa y una sola vez; no hay lista a mano que se desincronice.</item>
/// <item>Los slugs de cada <see cref="IFlaggedPermissionSource"/> dependen del flag de esa fuente.</item>
/// <item>Cualquier otro slug se considera siempre exigido.</item>
/// </list>
/// Si un slug está en varias fuentes, basta que alguna esté encendida.
/// </summary>
public sealed class PermissionEnforcementState(
    IServiceProvider services,
    IOptionsMonitor<OtPermissionEnforcementOptions> otOptions,
    IOptionsMonitor<ModulePermissionEnforcementOptions> moduleOptions,
    IEnumerable<IFlaggedPermissionSource> extraSources) : IPermissionEnforcementState
{
    private static readonly HashSet<string> OtSlugs = [.. OtPermissionMap.BySignature.Values];

    private readonly Lazy<HashSet<string>> _moduleSlugs = new(() => Discover(services), LazyThreadSafetyMode.ExecutionAndPublication);

    public bool IsEffectivelyEnforced(string slug)
    {
        ArgumentNullException.ThrowIfNull(slug);
        var flagged = false;
        if (OtSlugs.Contains(slug))
        {
            flagged = true;
            if (otOptions.CurrentValue.Enabled) return true;
        }

        if (_moduleSlugs.Value.Contains(slug))
        {
            flagged = true;
            if (moduleOptions.CurrentValue.Enabled) return true;
        }

        foreach (var source in extraSources)
        {
            if (!source.Slugs.Contains(slug, StringComparer.Ordinal)) continue;
            flagged = true;
            if (source.IsEnabled) return true;
        }

        return !flagged;
    }

    private static HashSet<string> Discover(IServiceProvider services)
    {
        var slugs = new HashSet<string>(StringComparer.Ordinal);
        var dataSource = services.GetService(typeof(EndpointDataSource)) as EndpointDataSource;
        if (dataSource is null) return slugs;
        foreach (var endpoint in dataSource.Endpoints)
        {
            foreach (var policy in endpoint.Metadata.OfType<AuthorizationPolicy>())
            {
                foreach (var requirement in policy.Requirements)
                {
                    switch (requirement)
                    {
                        case ModulePermissionRequirement single:
                            slugs.Add(single.Slug);
                            break;
                        case ModuleAnyPermissionRequirement any:
                            slugs.UnionWith(any.Slugs);
                            break;
                    }
                }
            }
        }

        return slugs;
    }
}
