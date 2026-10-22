using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Modulos.Reportes;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Diagnosticos;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Reportes;

/// <summary>
/// <see cref="EmisionPdf"/> (R-7, decisión Q7; regla única del PDF, el ZIP y la grilla, encargada a la HU #13847 por las notas de
/// la HU #13846): vehículo terminado → se emite aunque el lote siga EN_PROCESO; lote EN_PROCESO con el vehículo sin terminar → 409
/// <c>VEHICULO_NO_TERMINADO</c>; lote INCOMPLETO → se emite también con celdas en ERROR, pero no con celdas aún en curso.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~EmisionPdfTests
/// </code>
/// </example>
public sealed class EmisionPdfTests
{
    [Theory]
    [InlineData(EstadoDiagnostico.EnProceso, new[] { EstadoCelda.Completado, EstadoCelda.Na }, true)]
    [InlineData(EstadoDiagnostico.Completado, new[] { EstadoCelda.Completado }, true)]
    [InlineData(EstadoDiagnostico.EnProceso, new[] { EstadoCelda.Completado, EstadoCelda.Pendiente }, false)]
    [InlineData(EstadoDiagnostico.EnProceso, new[] { EstadoCelda.Completado, EstadoCelda.Error }, false)]
    [InlineData(EstadoDiagnostico.Incompleto, new[] { EstadoCelda.Completado, EstadoCelda.Error, EstadoCelda.Na }, true)]
    [InlineData(EstadoDiagnostico.Incompleto, new[] { EstadoCelda.Error }, true)]
    [InlineData(EstadoDiagnostico.Incompleto, new[] { EstadoCelda.Completado, EstadoCelda.EnCurso }, false)]
    [InlineData(EstadoDiagnostico.Incompleto, new EstadoCelda[0], false)]
    [InlineData(EstadoDiagnostico.Creado, new EstadoCelda[0], false)]
    public void Puede_emitir_segun_el_lote_y_las_celdas(EstadoDiagnostico lote, EstadoCelda[] celdas, bool puede)
    {
        EmisionPdf.Puede(lote, celdas).Should().Be(puede);
        EmisionPdf.Puede(Cabecera(lote), celdas).Should().Be(puede);
    }

    [Fact]
    public void Lote_en_proceso_con_el_vehiculo_sin_terminar_responde_409()
    {
        var exigir = () => EmisionPdf.Exigir(Cabecera(EstadoDiagnostico.EnProceso), [EstadoCelda.Completado, EstadoCelda.Pendiente]);

        var error = exigir.Should().Throw<ReglaException>().Which;
        error.Codigo.Should().Be(EmisionPdf.CodigoVehiculoNoTerminado).And.Be("VEHICULO_NO_TERMINADO");
        error.Status.Should().Be(409);
        EmisionPdf.Exigir(Cabecera(EstadoDiagnostico.Incompleto), [EstadoCelda.Error]);
    }

    private static CabeceraDiagnostico Cabecera(EstadoDiagnostico estado) => new() { Nombre = "Lote TST", Configuracion = "{}", Estado = estado };
}
