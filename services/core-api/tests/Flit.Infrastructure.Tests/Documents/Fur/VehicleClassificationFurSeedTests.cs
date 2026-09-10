using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Documents.Fur;

/// <summary>
/// Guardia del seed embebido del catálogo clasificación → plantilla FUR + field_to_fill (sin BD).
/// </summary>
public sealed class VehicleClassificationFurSeedTests
{
    private static string LoadDdl(string resource)
    {
        var asm = typeof(Flit.Infrastructure.InfrastructureExtensions).Assembly;
        var name = $"Flit.Infrastructure.Persistence.Sql.Ddl.{resource}";
        using var stream = asm.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"No se encontró el recurso embebido: {name}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static readonly Regex SeedRow = new(
        @"^\s*\('[^']+',\s*'(AUTOMOTOR|MAQUINARIA|REMOLQUES)',\s*'[^']+'\)",
        RegexOptions.Multiline);

    [Theory]
    [InlineData("39-HU10919-vehicle-classification-fur.sql")]
    [InlineData("97-vehicle-classification-fur-field-to-fill.sql")]
    public void Seed_Has96Rows(string resource) =>
        SeedRow.Count(LoadDdl(resource)).Should().Be(96);

    [Fact]
    public void Seed_MapsConstruccionOMineraToRemolquesSimilar() =>
        LoadDdl("97-vehicle-classification-fur-field-to-fill.sql")
            .Should().Contain("('MAQ. CONSTRUCCION O MINERA', 'REMOLQUES', 'SIMILAR')");

    [Fact]
    public void Seed_MapsCiclomotorFieldToMototriciclo() =>
        LoadDdl("97-vehicle-classification-fur-field-to-fill.sql")
            .Should().Contain("('CICLOMOTOR', 'AUTOMOTOR', 'MOTOTRICICLO')");

    [Fact]
    public void Seed39_IsIdempotentUpsert() =>
        LoadDdl("39-HU10919-vehicle-classification-fur.sql")
            .Should().Contain("ON CONFLICT (classification) DO UPDATE");

    [Fact]
    public void CreateTable_DeclaresFieldToFillText() =>
        LoadDdl("39-HU10919-vehicle-classification-fur.sql")
            .Should().Contain("field_to_fill   text NULL");
}
