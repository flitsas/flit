using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;
using Flit.Diagnostico.Api.Persistence.Configuraciones;
using Flit.Diagnostico.Domain.Consultas;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Dominio;

/// <summary>
/// HU #13737 (F03-H6), D-12 — clasificación de los errores de una fuente por su código (<c>tipo_error</c>): Timeout, Red y
/// HTTP 5xx son transitorios (se reintentan); RespuestaInvalida, Autenticacion, EstadoNoOk y NoConectado son definitivos (manda
/// lo que devuelve core-consultas). Un código desconocido, la celda bloqueada por RUNT o la falta de código son definitivos: no
/// se gasta consumo en lo que no se sabe si se resuelve solo.
/// </summary>
/// <example>
/// <code>
/// ClasificadorErrores.Clasificar("TIMEOUT"); // ClaseError.Transitorio
/// </code>
/// </example>
public sealed class ClasificadorErroresTests
{
    // D-12 — tabla de códigos de core-consultas y del producto.
    [Theory]
    [InlineData("TIMEOUT", ClaseError.Transitorio)]
    [InlineData("RED", ClaseError.Transitorio)]
    [InlineData("HTTP_5XX", ClaseError.Transitorio)]
    [InlineData("RESPUESTA_INVALIDA", ClaseError.Definitivo)]
    [InlineData("AUTENTICACION", ClaseError.Definitivo)]
    [InlineData("ESTADO_NO_OK", ClaseError.Definitivo)]
    [InlineData("NO_CONECTADO", ClaseError.Definitivo)]
    [InlineData(OrquestadorVehiculo.BloqueadaPorRunt, ClaseError.Definitivo)]
    [InlineData("CODIGO_QUE_NO_EXISTE", ClaseError.Definitivo)]
    [InlineData(null, ClaseError.Definitivo)]
    public void Clasifica_por_tabla_de_codigos(string? codigo, ClaseError esperada) =>
        ClasificadorErrores.Clasificar(codigo).Should().Be(esperada);

    // D-12 — cada valor de TipoErrorFuente tiene clase explícita: Timeout y Red transitorios, el resto definitivos.
    [Fact]
    public void Todo_TipoErrorFuente_tiene_clase_explicita()
    {
        foreach (var tipo in Enum.GetValues<TipoErrorFuente>())
        {
            var codigo = CodigosDeEnum.Codigo(tipo);
            ClasificadorErrores.EsConocido(codigo).Should().BeTrue($"{codigo} debe estar en la tabla del clasificador");
            ClasificadorErrores.Clasificar(codigo).Should().Be(
                tipo is TipoErrorFuente.Timeout or TipoErrorFuente.Red ? ClaseError.Transitorio : ClaseError.Definitivo, codigo);
        }

        ClasificadorErrores.EsConocido("CODIGO_QUE_NO_EXISTE").Should().BeFalse();
    }
}
