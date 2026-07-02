using Flit.Tramites.Application.Ocr;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.Ocr;

public sealed class MockDocumentOcrAnalyzerTests
{
    private static readonly byte[] AnyBytes = [0x25, 0x50, 0x44, 0x46];
    private readonly MockDocumentOcrAnalyzer _mock = new();

    [Fact]
    public async Task Factura_devuelve_es_factura_valida_true()
    {
        var r = await _mock.AnalyzeAsync("factura", AnyBytes, "application/pdf", TestContext.Current.CancellationToken);

        r.Ok.Should().BeTrue();
        r.Data.Should().NotBeNull();
        r.Data!["es_factura_valida"]!.GetValue<bool>().Should().BeTrue();
        r.Data["tipo_documento"]!.GetValue<string>().Should().Be("factura_electronica");
    }

    [Theory]
    [InlineData("aduana")]
    [InlineData("impronta")]
    [InlineData("soat")]
    public async Task Tipos_no_factura_devuelven_es_valido_true(string tipo)
    {
        var r = await _mock.AnalyzeAsync(tipo, AnyBytes, "application/pdf", TestContext.Current.CancellationToken);

        r.Ok.Should().BeTrue();
        r.Data!["es_valido"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public async Task Tipo_desconocido_devuelve_mock_generico()
    {
        var r = await _mock.AnalyzeAsync("otro", AnyBytes, "application/pdf", TestContext.Current.CancellationToken);

        r.Ok.Should().BeTrue();
        r.Data!["es_valido"]!.GetValue<bool>().Should().BeTrue();
    }
}
