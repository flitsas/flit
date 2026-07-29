using Flit.Infrastructure.Documents.Fur;
using Flit.Tramites.Application.Documents;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Documents;

/// <summary>
/// HU #10922/#10923 (Feature #10918) — guardia estructural de los manifests de FUR de los formatos nuevos
/// (MAQUINARIA/REMOLQUES): existen, cargan, ids únicos y todos los campos dentro de la página. La
/// alineación fina se verifica por render; la geometría de AUTOMOTOR sigue congelada en FurManifestGuardTests.
/// </summary>
public sealed class FurMultiFormatManifestTests
{
    [Theory]
    [InlineData(FurTemplateFormat.Maquinaria)]
    [InlineData(FurTemplateFormat.Remolques)]
    public void Manifest_LoadsWithUniqueIdsWithinBounds(FurTemplateFormat format)
    {
        FurFieldManifestLoader.HasEmbedded(format).Should().BeTrue($"debe existir el manifest de {format}");

        var m = FurFieldManifestLoader.LoadEmbedded(format);
        m.Fields.Should().NotBeEmpty();
        m.Fields.Select(f => f.Id).Should().OnlyHaveUniqueItems();

        var outOfBounds = m.Fields.Where(f =>
        {
            var right = f.Type == FurFieldType.Checkbox ? f.X + f.Size : f.X + f.W;
            var bottom = f.Type == FurFieldType.Checkbox ? f.Y + f.Size : f.Y + f.H;
            return f.X < 0 || f.Y < 0 || right > m.PageWidth || bottom > m.PageHeight;
        }).Select(f => f.Id).ToList();

        outOfBounds.Should().BeEmpty("todo campo debe caer dentro de {0}x{1}", m.PageWidth, m.PageHeight);
    }
}
