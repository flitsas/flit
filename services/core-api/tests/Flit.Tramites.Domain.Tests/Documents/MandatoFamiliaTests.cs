using Flit.Tramites.Domain.Documents;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests.Documents;

/// <summary>
/// HU #11204 — familia del mandatario. La familia dice QUIÉN firma como mandatario (una persona o el
/// propio organismo); la redacción la sigue eligiendo el <c>template_code</c>, porque dos organismos de
/// la misma familia pueden tener textos legales distintos.
/// </summary>
public sealed class MandatoFamiliaTests
{
    [Theory]
    [InlineData("organismo_transito")]
    [InlineData("ORGANISMO_TRANSITO")]
    [InlineData("  organismo_transito  ")]
    public void OrganismoTransito_SeReconoceSinImportarFormato(string codigo) =>
        MandatoFamiliaCodes.Resolve(codigo).Should().Be(MandatoFamilia.OrganismoTransito);

    [Theory]
    [InlineData("individuo")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("desconocida")]
    public void SinFamiliaConocida_CaeAIndividuo(string? codigo) =>
        // AC5: un organismo sin configuración se comporta como hasta ahora — el mandatario es la persona
        // firmante del OT, que es el caso genérico.
        MandatoFamiliaCodes.Resolve(codigo).Should().Be(MandatoFamilia.Individuo);

    [Fact]
    public void LaFamiliaNoDecideLaRedaccion_BelloYSabanetaSonLaMismaFamiliaConTextosDistintos()
    {
        // Invariante del diseño: las plantillas del PO marcan a los dos con
        // familia_mandatario: organismo_transito, pero Bello nombra al REPRESENTANTE LEGAL de la unión
        // temporal y Sabaneta nombra a la unión temporal directamente. Si alguien colapsara las dos
        // redacciones en una, cambiaría el contrato legal que hoy se emite para Bello.
        MandatoTemplateResolver.Resolve(MandatoTemplateResolver.Bello)
            .Should().Be(MandatoVariante.Bello);
        MandatoTemplateResolver.Resolve(MandatoTemplateResolver.Sabaneta)
            .Should().Be(MandatoVariante.Sabaneta);
        MandatoVariante.Bello.Should().NotBe(MandatoVariante.Sabaneta);
    }

    [Fact]
    public void AC3_UnOrganismoNuevoPuedeReutilizarUnaRedaccionYaSoportada()
    {
        // El template_code dejó de ser un catálogo cerrado: cualquier organismo puede apuntar a una
        // redacción existente y aportar sus propios datos, sin desplegar código.
        MandatoTemplateResolver.Resolve("sabaneta").Should().Be(MandatoVariante.Sabaneta);
        MandatoTemplateResolver.Resolve("generico").Should().Be(MandatoVariante.Generico);
        // Un código que no corresponde a ninguna redacción portada cae al genérico en vez de reventar.
        MandatoTemplateResolver.Resolve("funza").Should().Be(MandatoVariante.Generico);
    }
}
