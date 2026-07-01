using Flit.Admin.Application.DocumentTypes;
using FluentAssertions;
using Xunit;

namespace Flit.Admin.Tests.DocumentTypes;

/// <summary>
/// Validación de caracteres por campo del catálogo de tipos de documento (QA): el código
/// es alfanumérico+guion, el nombre admite letras/números/espacios y puntuación básica, y
/// la descripción no permite &lt; ni &gt; (anti-XSS). Antes ningún campo validaba caracteres.
/// </summary>
public sealed class DocumentTypeValidatorTests
{
    [Theory]
    [InlineData("RUT")]
    [InlineData("CC-01")]
    [InlineData("DOC123")]
    public void ValidCode_ReturnsNull(string code) =>
        DocumentTypeValidator.Validate(code, "Nombre válido", null).Should().BeNull();

    [Theory]
    [InlineData("RUT 01")]   // espacio
    [InlineData("RUT/01")]   // barra
    [InlineData("RUT@")]      // arroba
    public void InvalidCodeCharacters_ReturnsError(string code) =>
        DocumentTypeValidator.Validate(code, "Nombre válido", null).Should().NotBeNull();

    [Theory]
    [InlineData("Registro Único Tributario")]
    [InlineData("Cédula (frente y reverso)")]
    [InlineData("Poder & Mandato N° 1")]
    public void ValidName_ReturnsNull(string name) =>
        DocumentTypeValidator.Validate("COD", name, null).Should().BeNull();

    [Theory]
    [InlineData("Nombre <script>")]
    [InlineData("Doc #1 {x}")]
    [InlineData("Doc | pipe")]
    public void InvalidNameCharacters_ReturnsError(string name) =>
        DocumentTypeValidator.Validate("COD", name, null).Should().NotBeNull();

    [Theory]
    [InlineData(".-.")]            // pura puntuación
    [InlineData("''")]
    [InlineData("1234")]            // solo dígitos (sin letra)
    [InlineData("Doc&&Tipo")]      // puntuación repetida (&&)
    [InlineData("(Cédula)")]       // empieza con símbolo
    public void NameWeakOrPunctuation_ReturnsError(string name) =>
        DocumentTypeValidator.Validate("COD", name, null).Should().NotBeNull();

    [Fact]
    public void CodeOnlyPunctuation_ReturnsError() =>
        DocumentTypeValidator.Validate("---", "Nombre válido", null).Should().NotBeNull();

    [Theory]
    [InlineData("Descripción normal, con acentos y . , puntuación.")]
    [InlineData(null)]
    [InlineData("")]
    public void ValidOrEmptyDescription_ReturnsNull(string? description) =>
        DocumentTypeValidator.Validate("COD", "Nombre válido", description).Should().BeNull();

    [Theory]
    [InlineData("texto con <b>html</b>")]
    [InlineData("a > b")]
    public void DescriptionWithAngleBrackets_ReturnsError(string description) =>
        DocumentTypeValidator.Validate("COD", "Nombre válido", description).Should().NotBeNull();
}
