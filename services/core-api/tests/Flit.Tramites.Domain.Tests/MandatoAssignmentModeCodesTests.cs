using Flit.Tramites.Domain.Documents;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests;

public sealed class MandatoAssignmentModeCodesTests
{
    [Theory]
    [InlineData(null, MandatoAssignmentModeCodes.Signer)]
    [InlineData("", MandatoAssignmentModeCodes.Signer)]
    [InlineData("signer", MandatoAssignmentModeCodes.Signer)]
    [InlineData("SIGNER", MandatoAssignmentModeCodes.Signer)]
    [InlineData("institutional", MandatoAssignmentModeCodes.Institutional)]
    [InlineData("open", MandatoAssignmentModeCodes.Open)]
    [InlineData("desconocido", MandatoAssignmentModeCodes.Signer)]
    public void Resolve_NormalizesOrDefaultsToSigner(string? input, string expected)
    {
        MandatoAssignmentModeCodes.Resolve(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("open", true)]
    [InlineData("institutional", true)]
    [InlineData("signer", false)]
    [InlineData(null, false)]
    public void SkipsPersonSigner_ForOpenAndInstitutional(string? mode, bool expected)
    {
        MandatoAssignmentModeCodes.SkipsPersonSigner(mode).Should().Be(expected);
    }
}
