using FluentAssertions;
using Flit.Consultas.Api.Impuesto;
using Xunit;

namespace Flit.Consultas.Tests.Impuesto;

/// <summary>C3-2 — la frase gana sobre el número. Lo desconocido queda en NO_CONFIRMADO.</summary>
public sealed class ClasificadorRespuestaImpuestoTests
{
    [Theory]
    [InlineData("Timeout de page.goto", 1, "TIMEOUT")]
    [InlineData("", 2, "HTTP_5XX")]
    [InlineData("Error procesando la solicitud", 1, "HTTP_5XX")]
    [InlineData("Error seleccionando modelo", 1, "MODELO_NO_ACEPTADO")]
    [InlineData("El documento no corresponde al propietario", 3, "DOCUMENTO_NO_COINCIDE")]
    [InlineData("Datos o vehículo No Encontrado", 3, "VEHICULO_NO_ENCONTRADO")]
    [InlineData("Vehículo CANCELADO", 3, "VEHICULO_NO_CONSULTABLE")]
    [InlineData("TRASLADO DE CUENTA", 3, "VEHICULO_NO_CONSULTABLE")]
    [InlineData("Actualice sus datos", 1, "REQUIERE_ACTUALIZACION")]
    [InlineData("REQUIERE_ACTUALIZACION", 1, "REQUIERE_ACTUALIZACION")]
    [InlineData("Credenciales faltantes TaxRequestDto.credentials", 3, "AUTENTICACION")]
    [InlineData("situacion no catalogada", 1, "NO_CONFIRMADO")]
    public void La_frase_define_el_codigo(string texto, int typeError, string codigo) =>
        ClasificadorRespuestaImpuesto.Clasificar(typeError, texto, "", "").Should().Be(codigo);

    [Fact]
    public void Una_frase_mejor_gana_al_typeError_2() =>
        ClasificadorRespuestaImpuesto.Clasificar(2, "Error seleccionando modelo", "", "").Should().Be("MODELO_NO_ACEPTADO");
}
