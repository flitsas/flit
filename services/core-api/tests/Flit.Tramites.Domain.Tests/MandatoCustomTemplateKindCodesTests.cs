using Flit.Tramites.Domain.Documents;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests;

public sealed class MandatoCustomTemplateKindCodesTests
{
    [Theory]
    [InlineData(null, MandatoCustomTemplateKindCodes.None)]
    [InlineData("none", MandatoCustomTemplateKindCodes.None)]
    [InlineData("pdf", MandatoCustomTemplateKindCodes.Pdf)]
    [InlineData("EDITOR", MandatoCustomTemplateKindCodes.Editor)]
    [InlineData("otro", MandatoCustomTemplateKindCodes.None)]
    public void Resolve_Normalizes(string? input, string expected)
    {
        MandatoCustomTemplateKindCodes.Resolve(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("pdf", true)]
    [InlineData("editor", true)]
    [InlineData("none", false)]
    [InlineData(null, false)]
    public void HasCustom_OnlyPdfOrEditor(string? kind, bool expected)
    {
        MandatoCustomTemplateKindCodes.HasCustom(kind).Should().Be(expected);
    }
}
