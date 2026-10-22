using Flit.Tramites.Domain.Documents;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests.Documents;

/// <summary>
/// HU #13168 (Feature #13118, Épica #13090) — el catálogo de formatos de mandato es la única fuente de los
/// códigos válidos. Uso de ejemplo: <c>MandatoFormatCatalog.IsRedaction("bello")</c> es true y
/// <c>MandatoFormatCatalog.IsRedaction("auto")</c> es false (delega en la plantilla de sistema del organismo).
/// </summary>
public sealed class MandatoFormatCatalogTests
{
    [Fact]
    public void AC1_Catalogo_ListaLosCincoFormatos_ConNombreTipoYRedaccionBase()
    {
        MandatoFormatCatalog.All.Select(f => f.Code)
            .Should().Equal("auto", "generico", "sabaneta", "bello", "municipio");
        MandatoFormatCatalog.All.Should().OnlyContain(f => !string.IsNullOrWhiteSpace(f.DefaultName));
        MandatoFormatCatalog.Find("sabaneta")!.DefaultAssignmentMode.Should().Be("institutional");
        MandatoFormatCatalog.Find("bello")!.DefaultAssignmentMode.Should().Be("signer");
        MandatoFormatCatalog.Find("bello")!.BaseRedaction.Should().Be("bello");
    }

    [Fact]
    public void AC4_Auto_EsDelegacion_NoRedaccion()
    {
        var auto = MandatoFormatCatalog.Find("auto")!;

        auto.IsRedaction.Should().BeFalse();
        auto.BaseRedaction.Should().BeNull();
        MandatoFormatCatalog.RedactionCodes.Should().Equal("generico", "sabaneta", "bello", "municipio");
        MandatoFormatCatalog.Codes.Should().Contain("auto");
    }

    [Theory]
    [InlineData("  BELLO ", true)]
    [InlineData("desconocido", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void AC3_CodigoDesconocido_NoEstaEnElCatalogo(string? code, bool expected) =>
        MandatoFormatCatalog.Contains(code).Should().Be(expected);

    [Theory]
    [InlineData("generico", MandatoVariante.Generico)]
    [InlineData("sabaneta", MandatoVariante.Sabaneta)]
    [InlineData("bello", MandatoVariante.Bello)]
    [InlineData("municipio", MandatoVariante.Municipio)]
    [InlineData("auto", MandatoVariante.Generico)]
    [InlineData("otro", MandatoVariante.Generico)]
    public void AC7_SinRegresion_LaRedaccionResueltaEsLaMismaDeAntes(string code, MandatoVariante expected) =>
        MandatoTemplateResolver.Resolve(code).Should().Be(expected);

    [Fact]
    public void AC2_NingunaOtraClaseDeclaraUnaListaPropiaDeCodigos()
    {
        var src = FindSourceRoot();
        var files = new[]
        {
            "Flit.Infrastructure/OtRules/MandateConfigAdminService.cs",
            "Flit.Api/Endpoints/AdminPlataformaMandatosEndpoints.cs",
            "Flit.Api/Endpoints/AdminOtMandatosEndpoints.cs",
        };
        string[] codeConstants =
        [
            "MandatoTemplateResolver.Generico", "MandatoTemplateResolver.Sabaneta",
            "MandatoTemplateResolver.Bello", "MandatoTemplateResolver.Municipio",
        ];

        foreach (var file in files)
        {
            var text = File.ReadAllText(Path.Combine(src, file));
            text.Should().NotContain("AllowedTemplates", $"{file} no debe tener su propia lista");
            text.Should().NotContain("HashSet<string> Templates", $"{file} no debe tener su propia lista");
            text.Should().Contain("MandatoFormat", $"{file} consulta el catálogo único");

            // MandateConfigAdminService conserva constantes en la inferencia del OCR por nombre (no es una lista
            // de códigos válidos); las otras dos clases no pueden nombrar ningún código.
            if (!file.Contains("MandateConfigAdminService", StringComparison.Ordinal))
            {
                codeConstants.Where(c => text.Contains(c, StringComparison.Ordinal))
                    .Should().BeEmpty($"{file} consulta MandatoFormatCatalog en lugar de listar códigos");
            }
        }
    }

    private static string FindSourceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "Flit.Tramites.Domain");
            if (Directory.Exists(candidate))
                return Path.Combine(dir.FullName, "src");
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("No se encontró services/core-api/src desde el directorio de pruebas.");
    }
}
