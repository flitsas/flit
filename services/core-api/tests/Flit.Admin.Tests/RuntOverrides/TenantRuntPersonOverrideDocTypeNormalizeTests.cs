using Flit.Admin.Application.Companies.RuntPersonOverrides;
using FluentAssertions;
using Xunit;

namespace Flit.Admin.Tests.RuntOverrides;

/// <summary>
/// Bug #14721 — el lookup de conductor puede traer códigos de proveedor (PA, E, …)
/// tras <c>MapDocumentType</c>; el override se guarda en códigos FLIT (PAS, CE, …).
/// </summary>
public sealed class TenantRuntPersonOverrideDocTypeNormalizeTests
{
    [Theory]
    [InlineData("PAS", "PAS")]
    [InlineData("PA", "PAS")]
    [InlineData("P", "PAS")]
    [InlineData("pas", "PAS")]
    [InlineData("pa", "PAS")]
    [InlineData("CE", "CE")]
    [InlineData("E", "CE")]
    [InlineData("CC", "CC")]
    [InlineData("C", "CC")]
    [InlineData("TI", "TI")]
    [InlineData("T", "TI")]
    [InlineData("PPT", "PPT")]
    [InlineData("  PAS  ", "PAS")]
    public void Normalize_CanonizaAliasDeProveedor(string input, string expected)
    {
        TenantRuntPersonOverrideDocTypes.Normalize(input).Should().Be(expected);
    }

    [Fact]
    public void Normalize_Desconocido_DevuelveUpperTrim()
    {
        TenantRuntPersonOverrideDocTypes.Normalize(" xx ").Should().Be("XX");
    }
}
