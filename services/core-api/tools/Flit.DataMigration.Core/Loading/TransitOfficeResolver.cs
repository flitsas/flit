using Flit.DataMigration.V1.Mapping;
using Flit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Flit.DataMigration.V1.Loading;

/// <summary>
/// Cruza <c>traffic_secretary_code</c> (código RUNT del organismo en V1) con
/// <c>catalogs.transit_offices.code</c> (V2). Se carga una vez por corrida: el catálogo tiene
/// ~300 filas y cada trámite lo consulta.
/// <para>
/// Existe porque en V2 el organismo <b>no es un texto</b>: <c>ProcedureInstance.TransitOfficeId</c>
/// gobierna la bandeja del organismo, el grant OT↔empresa, el motor de reglas y la aprobación
/// (ver <c>TransitOfficeFieldKeys</c>). Un trámite migrado solo con el código como texto es
/// invisible para el organismo que lo aprobó. Verificado contra la copia de pdn del 2026-09-14:
/// 24 de los 25 códigos de V1 existen en el catálogo tal cual (mismo código RUNT).
/// </para>
/// </summary>
public sealed class TransitOfficeResolver
{
    private readonly Dictionary<string, TransitOfficeRef> _byCode = new(StringComparer.Ordinal);

    private TransitOfficeResolver() { }

    public static async Task<TransitOfficeResolver> LoadAsync(FlitDbContext db, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);

        var resolver = new TransitOfficeResolver();
        var offices = await db.TransitOffices.AsNoTracking().ToListAsync(cancellationToken);
        foreach (var office in offices)
        {
            var code = Normalize(office.Code);
            if (code.Length == 0)
            {
                continue;
            }

            // El catálogo tiene el código como clave natural; si hubiera dos filas con el mismo
            // código se queda la activa, que es la que la aplicación usaría.
            if (!resolver._byCode.TryGetValue(code, out var existing) || (!existing.IsActive && office.IsActive))
            {
                resolver._byCode[code] = new TransitOfficeRef(
                    office.Id, office.Code, office.Name, office.CityCode, office.CityName, office.IsActive);
            }
        }

        return resolver;
    }

    /// <summary><c>null</c> cuando V1 no trae código o el catálogo de V2 no lo conoce.</summary>
    public TransitOfficeRef? Resolve(string? v1Code)
    {
        var code = Normalize(v1Code);
        return code.Length > 0 && _byCode.TryGetValue(code, out var office) ? office : null;
    }

    /// <summary>V1 guarda el código con ceros y espacios inconsistentes: "05001000" y "5001000" son el mismo.</summary>
    private static string Normalize(string? code)
    {
        var trimmed = (code ?? string.Empty).Trim();
        var sinCeros = trimmed.TrimStart('0');
        return sinCeros.Length > 0 ? sinCeros : trimmed;
    }
}
