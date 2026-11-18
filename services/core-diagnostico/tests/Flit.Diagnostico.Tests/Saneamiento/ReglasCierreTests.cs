using Flit.Diagnostico.Domain.Saneamiento;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Saneamiento;

/// <summary>
/// Reglas de cierre (plan §3.3, S27; HU #14576 AC21, AC22 y AC23 en el dominio): SANEADO exige al menos un renglón, ninguno
/// PENDIENTE y al menos uno SANEADO; NO_PROCEDE solo el motivo.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~ReglasCierreTests
/// </code>
/// </example>
public sealed class ReglasCierreTests
{
    private const ResultadoRenglonSaneamiento P = ResultadoRenglonSaneamiento.Pendiente;
    private const ResultadoRenglonSaneamiento S = ResultadoRenglonSaneamiento.Saneado;
    private const ResultadoRenglonSaneamiento N = ResultadoRenglonSaneamiento.NoProcede;

    [Fact]
    public void HU14576_AC21_saneado_y_no_procede_sin_pendientes_cierra() =>
        ReglasCierre.ErrorCierreSaneado([S, N]).Should().BeNull();

    [Fact]
    public void HU14576_AC23_un_OTRO_saneado_basta() =>
        ReglasCierre.ErrorCierreSaneado([S]).Should().BeNull();

    [Theory]
    [InlineData(new ResultadoRenglonSaneamiento[0])]
    [InlineData(new[] { S, P })]
    [InlineData(new[] { N, N })]
    public void HU14576_AC22_cierre_SANEADO_incompleto(ResultadoRenglonSaneamiento[] renglones) =>
        ReglasCierre.ErrorCierreSaneado(renglones).Should().Be(ReglasCierre.CierreIncompleto);

    [Fact]
    public void HU14576_AC23_NO_PROCEDE_solo_exige_motivo_valido()
    {
        ReglasCierre.ErrorCierreNoProcede("No procede: el propietario pagó por fuera.").Should().BeNull();
        ReglasCierre.ErrorCierreNoProcede(null).Should().Be(TextoLibreSaneamiento.MotivoRequerido);
        ReglasCierre.ErrorCierreNoProcede("corto").Should().Be(TextoLibreSaneamiento.MotivoRequerido);
    }
}
