using Flit.Admin.Application.Plataforma.Ai;
using FluentAssertions;
using Xunit;

namespace Flit.Admin.Tests.Plataforma.Ai;

public sealed class AiApiKeyMaskTests
{
    [Fact]
    public void FromKey_enmascara_sin_revelar_el_medio()
    {
        var mask = AiApiKeyMask.FromKey("sk-ant-api03-abcdefghijklmnop");
        mask.Should().Be("sk-***mnop");
    }

    [Fact]
    public void FromKey_null_devuelve_null()
    {
        AiApiKeyMask.FromKey(null).Should().BeNull();
    }
}
