using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Saneamiento;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Saneamiento;

/// <summary>
/// Elegibilidad para solicitar (plan §3.1, S3, S5, S26; HU #14576 AC1, AC2 y AC3 en el dominio): vehículo terminado (todas sus
/// celdas en COMPLETADO o NA) y sin saneamiento no anulado. No mira modo, veredicto ni trámite.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~ElegibilidadSaneamientoTests
/// </code>
/// </example>
public sealed class ElegibilidadSaneamientoTests
{
    [Fact]
    public void HU14576_AC1_terminado_sin_saneamiento_es_elegible()
    {
        ElegibilidadSaneamiento.MotivoNoElegible([EstadoCelda.Completado, EstadoCelda.Na], null).Should().BeNull();
        ElegibilidadSaneamiento.MotivoNoElegible(true, EstadoSaneamiento.Anulado).Should().BeNull("tras ANULADO se puede volver a marcar (S26)");
    }

    [Theory]
    [InlineData(EstadoCelda.Pendiente)]
    [InlineData(EstadoCelda.EnCurso)]
    [InlineData(EstadoCelda.Error)]
    public void HU14576_AC2_una_celda_abierta_o_en_error_es_NO_TERMINADO(EstadoCelda abierta) =>
        ElegibilidadSaneamiento.MotivoNoElegible([EstadoCelda.Completado, abierta], null).Should().Be(ElegibilidadSaneamiento.NoTerminado);

    [Fact]
    public void HU14576_AC2_sin_celdas_no_esta_terminado() =>
        ElegibilidadSaneamiento.MotivoNoElegible(Array.Empty<EstadoCelda>(), null).Should().Be(ElegibilidadSaneamiento.NoTerminado);

    [Theory]
    [InlineData(EstadoSaneamiento.Solicitado)]
    [InlineData(EstadoSaneamiento.EnGestion)]
    [InlineData(EstadoSaneamiento.Saneado)]
    [InlineData(EstadoSaneamiento.NoProcede)]
    public void HU14576_AC3_con_saneamiento_no_anulado_es_YA_SOLICITADO_aunque_no_este_terminado(EstadoSaneamiento vigente)
    {
        ElegibilidadSaneamiento.MotivoNoElegible(true, vigente).Should().Be(ElegibilidadSaneamiento.YaSolicitado);
        ElegibilidadSaneamiento.MotivoNoElegible(false, vigente).Should().Be(ElegibilidadSaneamiento.YaSolicitado, "el saneamiento existente manda sobre el avance");
    }
}
