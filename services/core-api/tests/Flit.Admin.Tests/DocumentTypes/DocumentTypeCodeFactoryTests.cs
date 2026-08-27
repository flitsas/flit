using Flit.Admin.Application.DocumentTypes;
using FluentAssertions;
using Xunit;

namespace Flit.Admin.Tests.DocumentTypes;

public sealed class DocumentTypeCodeFactoryTests
{
    [Theory]
    [InlineData("Registro Único Tributario", "REGISTRO-UNICO-TRIBUTARIO")]
    [InlineData("Cédula de extranjería", "CEDULA-DE-EXTRANJERIA")]
    [InlineData("SOAT", "SOAT")]
    [InlineData("  mandato  ", "MANDATO")]
    public void FromName_SlugsAndUppercases(string name, string expected) =>
        DocumentTypeCodeFactory.FromName(name).Should().Be(expected);

    [Fact]
    public void FromName_Empty_UsesFallback() =>
        DocumentTypeCodeFactory.FromName("   ").Should().Be("DOC");

    [Fact]
    public async Task AllocateUnique_AppendsSuffixWhenTaken()
    {
        var taken = new HashSet<string>(StringComparer.Ordinal) { "MANDATO" };
        var code = await DocumentTypeCodeFactory.AllocateUniqueAsync(
            "Mandato",
            (candidate, _) => Task.FromResult(taken.Contains(candidate)),
            TestContext.Current.CancellationToken);
        code.Should().Be("MANDATO-2");
    }
}
