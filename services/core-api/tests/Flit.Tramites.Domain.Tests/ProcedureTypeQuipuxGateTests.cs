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

    [Theory]
    [InlineData("""{"quipux":{"familia":"MATRICULA"}}""", "MATRICULA")]
    [InlineData("""{"quipux":{"familia":"TRASPASO"}}""", "TRASPASO")]
    [InlineData("""{"quipux":{"familia":"OTROS"}}""", "OTROS")]
    [InlineData("""{"quipux":{"familia":"OTRA"}}""", null)]
    [InlineData("""{"quipux":{}}""", null)]
    [InlineData("{}", null)]
    public void FamiliaQuipux_SegunBloque(string? json, string? esperado) =>
        ProcedureTypeQuipuxGate.FamiliaQuipux(json).Should().Be(esperado);

    [Fact]
    public void CanalActivo_OtConDivipoYBanderaMatricula_EsTrue()
    {
        var refs = """{"quipux":{"familia":"MATRICULA"}}""";
        ProcedureTypeQuipuxGate
            .CanalActivoParaSecretaria(refs, "05001", quipuxRegistration: true, quipuxTransfer: false, quipuxOther: false)
            .Should().BeTrue();
    }

    [Fact]
    public void CanalActivo_SinDivipo_EsFalse_AunqueBanderaOn()
    {
        var refs = """{"quipux":{"familia":"MATRICULA"}}""";
        ProcedureTypeQuipuxGate
            .CanalActivoParaSecretaria(refs, null, quipuxRegistration: true, quipuxTransfer: false, quipuxOther: false)
            .Should().BeFalse();
        ProcedureTypeQuipuxGate
            .CanalActivoParaSecretaria(refs, "  ", quipuxRegistration: true, quipuxTransfer: false, quipuxOther: false)
            .Should().BeFalse();
    }

    [Fact]
    public void CanalActivo_ConDivipoPeroBanderaOff_EsFalse()
    {
        var refs = """{"quipux":{"familia":"MATRICULA"}}""";
        ProcedureTypeQuipuxGate
            .CanalActivoParaSecretaria(refs, "05001", quipuxRegistration: false, quipuxTransfer: false, quipuxOther: false)
            .Should().BeFalse();
    }

    [Fact]
    public void CanalActivo_FamiliaTraspasoUsaBanderaTransfer()
    {
        var refs = """{"quipux":{"familia":"TRASPASO"}}""";
        ProcedureTypeQuipuxGate
            .CanalActivoParaSecretaria(refs, "05001", quipuxRegistration: true, quipuxTransfer: false, quipuxOther: false)
            .Should().BeFalse();
        ProcedureTypeQuipuxGate
            .CanalActivoParaSecretaria(refs, "05001", quipuxRegistration: false, quipuxTransfer: true, quipuxOther: false)
            .Should().BeTrue();
    }
}
