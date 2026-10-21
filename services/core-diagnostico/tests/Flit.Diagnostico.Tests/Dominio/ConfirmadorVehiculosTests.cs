using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Estimacion;
using Flit.Diagnostico.Domain.Vehiculos;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Dominio;

/// <summary>
/// HU #13751 (F02-H5), escenarios 2 a 5 — <see cref="ConfirmadorVehiculos"/>, sin base: revalida cada vehículo con la regla
/// de la carga (#13749), descarta inválidos y duplicados (gana el primero), marca el límite del lote y calcula el estimado
/// (#13747) de los válidos. Es lo que reusa la captura Express (#13753) con un solo vehículo.
/// </summary>
/// <example>
/// <code>
/// var r = ConfirmadorVehiculos.Confirmar([new VehiculoCapturado("ABC123", null, null)], [Fuente.Runt], 500);
/// // r.Validos.Count == 1, r.Estimado == [EstimadoPorFuente(Runt, 1)]
/// </code>
/// </example>
public sealed class ConfirmadorVehiculosTests
{
    [Fact]
    public void Descarta_invalidos_y_duplicados_y_conserva_el_orden_de_la_captura()
    {
        var resultado = ConfirmadorVehiculos.Confirmar(
        [
            new("abc-123", null, null), new("MAL", null, null), new("ABC123", "CC", "1020304050"), new("9FBTEST1234567890", " nit ", "900111222"),
            new("AB1234567", null, null), new("XYZ98A", "CC", "12AB34"), new(null, null, null), new("R12345", null, null),
        ], [Fuente.Runt], 500);

        resultado.Validos.Select(v => (v.Orden, v.IdentificadorNorm, v.Tipo)).Should().Equal(
            (1, "ABC123", TipoIdentificador.PlacaCarro), (2, "9FBTEST1234567890", TipoIdentificador.Vin), (3, "R12345", TipoIdentificador.PlacaRemolque));
        resultado.Validos[0].IdentificadorOriginal.Should().Be("abc-123");
        resultado.Validos[0].DocumentoLocatario.Should().BeNull("el duplicado no aporta su documento: gana la primera fila");
        resultado.Validos[1].TipoDocumentoLocatario.Should().Be("NIT");
        resultado.Validos[1].DocumentoLocatario.Should().Be("900111222");
        resultado.Invalidos.Should().Be(4, "MAL, AB1234567 (demasiado largo), el documento con letras y el vacío");
        resultado.Duplicados.Should().Be(1);
        resultado.SinValidos.Should().BeFalse();
        resultado.SuperaLimite.Should().BeFalse();
    }

    [Fact]
    public void Sin_validos_o_sobre_el_limite_lo_marca()
    {
        ConfirmadorVehiculos.Confirmar([new("MAL", null, null)], [Fuente.Runt], 500).SinValidos.Should().BeTrue();
        ConfirmadorVehiculos.Confirmar([], [Fuente.Runt], 500).SinValidos.Should().BeTrue();

        var placas = Enumerable.Range(0, 3).Select(i => new VehiculoCapturado($"ABC{i:D3}", null, null)).ToList();
        var sobre = ConfirmadorVehiculos.Confirmar(placas, [Fuente.Runt], 2);
        var justo = ConfirmadorVehiculos.Confirmar(placas, [Fuente.Runt], 3);

        sobre.SuperaLimite.Should().BeTrue();
        sobre.LimiteVehiculosLote.Should().Be(2);
        justo.SuperaLimite.Should().BeFalse();
    }

    [Fact]
    public void Calcula_el_estimado_de_los_validos()
    {
        var resultado = ConfirmadorVehiculos.Confirmar(
            [new("ABC123", "CC", "1020304050"), new("XYZ987", null, null), new("MAL", "CC", "1")], [Fuente.Runt, Fuente.RuntLocatario], 500);

        resultado.Estimado.Should().Equal(new EstimadoPorFuente(Fuente.Runt, 2), new EstimadoPorFuente(Fuente.RuntLocatario, 1));
    }

    [Fact]
    public void ToString_no_revela_el_documento()
    {
        var capturado = new VehiculoCapturado("ABC123", "CC", "1020304050");
        var confirmado = ConfirmadorVehiculos.Confirmar([capturado], [Fuente.Runt], 500).Validos.Single();

        capturado.ToString().Should().NotContain("1020304050").And.Contain("ABC123");
        confirmado.ToString().Should().NotContain("1020304050").And.Contain("ABC123");
    }
}
