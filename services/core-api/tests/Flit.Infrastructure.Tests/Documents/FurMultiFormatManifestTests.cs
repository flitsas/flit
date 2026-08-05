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

    /// <summary>
    /// HU #11257 (Feature #11254, H4) — guardia explícita de que las cuatro casillas de
    /// <c>MarkTramite</c> (tipo de trámite + modalidad de prenda) están declaradas en LOS TRES
    /// manifiestos. Antes de esta HU, <c>requested_process_11</c> solo existía en AUTOMOTOR y
    /// <c>requested_process_12</c> en ninguno: un trámite con prenda en maquinaria o remolques no
    /// marcaba nada, y ningún test lo detectaba (<see cref="Mapper_EmitsOnlyTokensDefinedInManifest"/>-
    /// style de <c>FurManifestGuardTests</c> solo cubre AUTOMOTOR). Un olvido futuro en cualquiera de
    /// los tres formatos debe fallar aquí, no pasar en silencio.
    /// </summary>
    [Theory]
    [InlineData(FurTemplateFormat.Automotor)]
    [InlineData(FurTemplateFormat.Maquinaria)]
    [InlineData(FurTemplateFormat.Remolques)]
    public void Manifest_DeclaresRequestedProcessCheckboxes_1_2_11_12(FurTemplateFormat format)
    {
        var ids = new HashSet<string>(
            FurFieldManifestLoader.LoadEmbedded(format).Fields.Select(f => f.Id),
            StringComparer.OrdinalIgnoreCase);

        var esperados = new[]
        {
            "requested_process_1", "requested_process_2", "requested_process_11", "requested_process_12",
        };

        var faltantes = esperados.Where(id => !ids.Contains(id)).ToList();
        faltantes.Should().BeEmpty(
            "el manifest de {0} debe declarar tipo de trámite (1/2) y modalidad de prenda (11/12); faltan: {1}",
            format, string.Join(", ", faltantes));
    }
}
