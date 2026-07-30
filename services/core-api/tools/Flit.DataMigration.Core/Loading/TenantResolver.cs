using Flit.DataMigration.V1.Mapping;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Microsoft.EntityFrameworkCore;

namespace Flit.DataMigration.V1.Loading;

/// <summary>Resultado de intentar cruzar la compañía de V1 con un tenant de V2.</summary>
public sealed class TenantResolution
{
    public Guid? TenantId { get; private init; }
    public string? Problem { get; private init; }
    public bool Resolved => TenantId is not null;

    public static TenantResolution Ok(Guid tenantId) => new() { TenantId = tenantId };
    public static TenantResolution Fail(string problem) => new() { Problem = problem };
}

/// <summary>
/// Cruza <c>company_registered</c> (NIT en V1) con <c>identity.tenants.tax_id</c> (V2).
/// <para>
/// Es el punto más peligroso de toda la migración: <c>tenant_id</c> es NOT NULL pero
/// <b>no tiene foreign key</b>, así que la base NO frena un id inventado. Un NIT mal
/// resuelto mete los trámites de una empresa dentro de otra, y RLS los haría invisibles
/// para su dueño real. Por eso aquí se prefiere fallar antes que adivinar.
/// </para>
/// </summary>
public sealed class TenantResolver
{
    private readonly Dictionary<string, List<Tenant>> _byKey = new(StringComparer.Ordinal);

    private TenantResolver() { }

    public static async Task<TenantResolver> LoadAsync(FlitDbContext db, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);

        var resolver = new TenantResolver();
        var tenants = await db.Tenants.AsNoTracking().ToListAsync(cancellationToken);

        foreach (var tenant in tenants)
        {
            resolver.Register(tenant);
        }

        return resolver;
    }

    /// <summary>
    /// Indexa un tenant bajo TODAS sus formas plausibles de NIT ("811222333-8" se busca
    /// tanto por 8112223338 como por 811222333).
    /// <para>
    /// También hay que llamarlo cuando el laboratorio crea un tenant en mitad del lote: el
    /// índice se arma una sola vez al arrancar, y sin re-indexarlo el siguiente trámite de esa
    /// misma compañía volvería a intentar crearla y reventaría contra <c>uq_tenants_code</c>.
    /// </para>
    /// </summary>
    public void Register(Tenant tenant)
    {
        ArgumentNullException.ThrowIfNull(tenant);

        foreach (var key in NitNormalizer.Keys(tenant.TaxId))
        {
            if (!_byKey.TryGetValue(key, out var bucket))
            {
                bucket = [];
                _byKey[key] = bucket;
            }

            bucket.Add(tenant);
        }
    }

    public TenantResolution Resolve(string? companyNit)
    {
        if (string.IsNullOrWhiteSpace(companyNit))
        {
            return TenantResolution.Fail("El trámite no tiene company_registered en V1.");
        }

        var matches = NitNormalizer.Keys(companyNit)
            .SelectMany(key => _byKey.TryGetValue(key, out var bucket) ? bucket : [])
            .DistinctBy(tenant => tenant.Id)
            .ToList();

        return matches.Count switch
        {
            1 => TenantResolution.Ok(matches[0].Id),
            0 => TenantResolution.Fail(
                $"NIT '{companyNit}' no existe como tenant en V2."),
            // Ambigüedad = cuarentena. Elegir "el primero" sería asignar trámites al azar.
            _ => TenantResolution.Fail(
                $"NIT '{companyNit}' resuelve a {matches.Count} tenants distintos " +
                $"({string.Join(", ", matches.Select(t => $"{t.Code}/{t.TaxId}"))}). " +
                "Ambigüedad: requiere decisión manual."),
        };
    }
}
