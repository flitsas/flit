using Flit.Admin.Domain.Companies.Domains;
using FluentAssertions;
using Xunit;

namespace Flit.Admin.Tests.Companies.Domains;

/// <summary>
/// Uso de ejemplo: <c>HostNormalizer.TryNormalize("Red.Example.com", out var host, out _)</c> → <c>true</c>,
/// <c>host == "red.example.com"</c>. HU #12416 AC1 — misma expresión RFC 1123 que
/// <c>ck_tenant_domains_host_format</c> (DDL 116); ver <c>TenantDomainsSchemaTests</c> para el
/// contraste directo contra el CHECK y <c>TenantDomainConstraintsTests</c> para el motor real.
/// </summary>
public sealed class HostNormalizerTests
{
    [Theory]
    [InlineData("red.example.com", "red.example.com")]
    [InlineData("Red.Example.com", "red.example.com")]
    [InlineData("  red.example.com  ", "red.example.com")]
    [InlineData("a-b.red.example.com", "a-b.red.example.com")]
    [InlineData("xn--espaa-rta.com", "xn--espaa-rta.com")]
    [InlineData("portal.xn--p1ai", "portal.xn--p1ai")]
    public void AC1_HostValido_SeNormalizaAMinusculasSinTocarSuEstructura(string raw, string expected)
    {
        HostNormalizer.TryNormalize(raw, out var normalized, out var errorCode).Should().BeTrue();
        normalized.Should().Be(expected);
        errorCode.Should().BeNull();
    }

    [Fact]
    public void AC1_DominioInternacionalizadoBienFormado_SeConvierteAPunycode()
    {
        HostNormalizer.TryNormalize("españa.com", out var normalized, out _).Should().BeTrue();
        normalized.Should().Be("xn--espaa-rta.com");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("https://red.example.com")]
    [InlineData("red.example.com:443")]
    [InlineData("red.example.com/login")]
    [InlineData("red.example.com?x=1")]
    [InlineData("*.example.com")]
    [InlineData("red..example.com")]
    [InlineData("-red.example.com")]
    [InlineData("red-.example.com")]
    [InlineData("localhost")]
    [InlineData("red.example.com.")]
    [InlineData("a.b")]
    [InlineData("1.2.3.4")]
    public void AC1_FormatoInvalido_SeRechazaConCodigoIdentificable(string? raw)
    {
        HostNormalizer.TryNormalize(raw, out var normalized, out var errorCode).Should().BeFalse();
        normalized.Should().BeEmpty();
        errorCode.Should().Be(DomainErrors.HostInvalid);
    }
}
