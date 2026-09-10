using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Flit.Infrastructure.Persistence;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence;

/// <summary>
/// HU sin ADO 2026-08-11 (segunda tanda) — blinda contra la duplicación de los 6 códigos de tipo de
/// servicio del vehículo (casilla 18 del FUR). Viven en DOS sitios que nada más obliga a mantener
/// sincronizados: las constantes de <see cref="VehicleServiceTypeCode"/> (Domain, consumidas por
/// <c>FurFieldMapper.MarkServicio</c>) y la semilla SQL del catálogo
/// (<c>69-vehicle-service-types-catalog-seed.sql</c>, Infrastructure, consumida por
/// <c>DbVehicleServiceTypeCatalog</c> para el selector del wizard). Si alguien agrega/renombra un
/// tipo solo en un lado, el otro lo ignora en silencio: el selector podría ofrecer un código que el
/// FUR no reconoce (cae a ninguna casilla), o el FUR sabría marcar una casilla que el selector nunca
/// ofrece.
/// </summary>
public sealed class VehicleServiceTypeCodeCatalogParityTests
{
    private const string ResourceName =
        "Flit.Infrastructure.Persistence.Sql.Ddl.69-vehicle-service-types-catalog-seed.sql";

    // Ejemplo de fila real: ('0198f1b0-...'::uuid, 'PARTICULAR',  'Particular',   1),
    private static readonly Regex RowPattern = new(
        @"'[0-9a-fA-F-]+'::uuid,\s*'(?<code>[A-Z]+)',\s*'[^']*',\s*(?<order>\d+)\)",
        RegexOptions.Compiled);

    private static string LoadSeedSql()
    {
        var assembly = typeof(FlitDbContext).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName);
        stream.Should().NotBeNull($"la semilla embebida {ResourceName} debe existir");
        using var reader = new StreamReader(stream!, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static Dictionary<string, int> ParseSeedCodes(string sql)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (Match m in RowPattern.Matches(sql))
        {
            result[m.Groups["code"].Value] = int.Parse(m.Groups["order"].Value, CultureInfo.InvariantCulture);
        }

        return result;
    }

    [Fact]
    public void LaSemillaSeParseaConLosSeisCodigosEsperados()
    {
        // Guardia de la propia guardia: si el regex dejara de matchear (p. ej. cambia el formato de
        // fila del INSERT), este test falla explícito en vez de que la comparación de abajo pase en
        // falso positivo por comparar dos conjuntos vacíos.
        var seed = ParseSeedCodes(LoadSeedSql());

        seed.Should().HaveCount(6,
            "la semilla debe declarar exactamente 6 filas parseables; si esto falla revisa primero " +
            "si el formato de la fila SQL cambió y el regex de este test quedó desactualizado");
    }

    [Fact]
    public void LosCodigosYElOrdenDeLaSemillaCoincidenConVehicleServiceTypeCode()
    {
        var seed = ParseSeedCodes(LoadSeedSql());

        // VehicleServiceTypeCode.All está ordenado con el mismo sort_order normativo 1-6 que la
        // semilla (Particular, Público, Diplomático, Oficial, Especial, Otros).
        var esperado = VehicleServiceTypeCode.All
            .Select((code, i) => (code, order: i + 1))
            .ToDictionary(x => x.code, x => x.order, StringComparer.Ordinal);

        var faltanEnSemilla = esperado.Keys.Except(seed.Keys)
            .OrderBy(x => x, StringComparer.Ordinal).ToList();
        var sobranEnSemilla = seed.Keys.Except(esperado.Keys)
            .OrderBy(x => x, StringComparer.Ordinal).ToList();
        var ordenDistinto = esperado.Keys.Intersect(seed.Keys)
            .Where(code => esperado[code] != seed[code])
            .Select(code => $"{code}: VehicleServiceTypeCode={esperado[code]} vs semilla={seed[code]}")
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        faltanEnSemilla.Should().BeEmpty(
            "estos códigos están en VehicleServiceTypeCode (Domain) pero NO en la semilla SQL " +
            "(69-vehicle-service-types-catalog-seed.sql): {0}. El FUR sabría marcarlos pero el " +
            "catálogo de BD nunca los ofrecería en el selector.",
            string.Join(", ", faltanEnSemilla));
        sobranEnSemilla.Should().BeEmpty(
            "estos códigos están en la semilla SQL (69-vehicle-service-types-catalog-seed.sql) pero " +
            "NO en VehicleServiceTypeCode (Domain): {0}. El selector podría ofrecerlos, pero " +
            "FurFieldMapper.MarkServicio no sabría marcar ninguna casilla para ellos.",
            string.Join(", ", sobranEnSemilla));
        ordenDistinto.Should().BeEmpty(
            "el sort_order de estos códigos difiere entre la semilla SQL y el orden declarado en " +
            "VehicleServiceTypeCode.All: {0}",
            string.Join(" | ", ordenDistinto));
    }
}
