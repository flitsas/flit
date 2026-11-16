using Flit.Diagnostico.Domain.Personas;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Dominio;

/// <summary>
/// HU #14582 AC2 (contrato §1.4, SP-3) — homologación del tipo de documento a un código canónico antes de comparar el propietario
/// declarado con el del RUNT: los tipos llegan abreviados de formas distintas (CC o C, NIT o N…) desde la captura y desde Intempo.
/// Sin tildes, puntos ni mayúsculas; lo que no está en la tabla es null, nunca un canónico por defecto.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~HomologacionTipoDocumentoTests
/// </code>
/// </example>
public sealed class HomologacionTipoDocumentoTests
{
    // AC2 — cada sinónimo de la tabla del contrato da su canónico (lo que declara el cliente y lo que devuelve Intempo).
    [Theory]
    [InlineData("CC", "CC")]
    [InlineData("C", "CC")]
    [InlineData("cc", "CC")]
    [InlineData(" c ", "CC")]
    [InlineData("C.C.", "CC")]
    [InlineData("Cédula de ciudadanía", "CC")]
    [InlineData("CEDULA", "CC")]
    [InlineData("CE", "CE")]
    [InlineData("E", "CE")]
    [InlineData("CX", "CE")]
    [InlineData("Cédula de extranjería", "CE")]
    [InlineData("NIT", "NIT")]
    [InlineData("N", "NIT")]
    [InlineData("n.i.t.", "NIT")]
    [InlineData("TI", "TI")]
    [InlineData("T", "TI")]
    [InlineData("Tarjeta de identidad", "TI")]
    [InlineData("PAS", "PAS")]
    [InlineData("PA", "PAS")]
    [InlineData("PP", "PAS")]
    [InlineData("P", "PAS")]
    [InlineData("Pasaporte", "PAS")]
    [InlineData("RC", "RC")]
    [InlineData("R", "RC")]
    [InlineData("Registro civil", "RC")]
    [InlineData("CD", "CD")]
    [InlineData("D", "CD")]
    [InlineData("Carné diplomático", "CD")]
    [InlineData("PPT", "PPT")]
    [InlineData("PT", "PPT")]
    [InlineData("PEP", "PEP")]
    public void HU14582_AC2_sinonimo_da_el_canonico(string tipo, string canonico) =>
        HomologacionTipoDocumento.Canonico(tipo).Should().Be(canonico);

    // AC2 / AC4 (SP-3) — fuera de la tabla, vacío o null: no homologable (null), sin códigos numéricos de Quipux.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("7")]
    [InlineData("13")]
    [InlineData("XX")]
    [InlineData("CEDULA DE OTRO PAIS")]
    public void HU14582_AC2_tipo_fuera_de_la_tabla_no_es_homologable(string? tipo) =>
        HomologacionTipoDocumento.Canonico(tipo).Should().BeNull();

    // ADR-0082 — el permiso por protección temporal nunca colapsa a pasaporte.
    [Fact]
    public void HU14582_AC2_PPT_no_colapsa_a_pasaporte()
    {
        HomologacionTipoDocumento.Canonico("PPT").Should().NotBe(HomologacionTipoDocumento.Canonico("PAS"));
        HomologacionTipoDocumento.Canonico("PT").Should().Be("PPT");
    }
}
