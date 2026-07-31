using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Documents.Fur;

/// <summary>
/// HU #10919 — guardia del seed embebido del catálogo clasificación → plantilla FUR (sin BD): valida que
/// el DDL trae las 96 clasificaciones y el mapeo literal clave (MAQ. CONSTRUCCION O MINERA → REMOLQUES).
/// </summary>
public sealed class VehicleClassificationFurSeedTests
{
    private static string LoadDdl()
    {
        var asm = typeof(Flit.Infrastructure.InfrastructureExtensions).Assembly;
        const string name = "Flit.Infrastructure.Persistence.Sql.Ddl.39-HU10919-vehicle-classification-fur.sql";
        using var stream = asm.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"No se encontró el recurso embebido: {name}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    [Fact]
    public void Seed_Has96Rows()
    {
        // Solo las tuplas de VALUES (a inicio de línea); excluye el CHECK IN (...) que va mid-línea.
        var rx = new Regex(@"^\s*\('[^']+',\s*'(AUTOMOTOR|MAQUINARIA|REMOLQUES)'\)", RegexOptions.Multiline);
        rx.Count(LoadDdl()).Should().Be(96);
    }

    [Fact]
    public void Seed_MapsConstruccionOMineraToRemolques() =>
        LoadDdl().Should().Contain("('MAQ. CONSTRUCCION O MINERA', 'REMOLQUES')");

    [Fact]
    public void Seed_IsIdempotent() =>
        LoadDdl().Should().Contain("ON CONFLICT (classification) DO NOTHING");
}
