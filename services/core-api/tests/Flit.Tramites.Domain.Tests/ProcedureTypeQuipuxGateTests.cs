using FluentAssertions;
using Flit.Tramites.Domain.Tramites.Quipux;
using Xunit;

namespace Flit.Tramites.Domain.Tests;

public sealed class ProcedureTypeQuipuxGateTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("{}", false)]
    [InlineData("""{"otro":{}}""", false)]
    [InlineData("""{"quipux":"texto"}""", false)]
    [InlineData("""{"quipux":{}}""", true)]
    [InlineData("""{"quipux":{"tipoTramite":"X"}}""", true)]
    [InlineData("no-json", false)]
    public void EstaParametrizadoParaQuipux_SegunExternalRefs(string? json, bool esperado) =>
        ProcedureTypeQuipuxGate.EstaParametrizadoParaQuipux(json).Should().Be(esperado);
}
