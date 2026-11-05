using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;
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

    // D-12 — cada valor de TipoErrorFuente tiene clase explícita: Timeout, Red y Http5xx transitorios, el resto definitivos.
    // HU #13877: el código sale del mapa explícito TipoErrorFuenteCodigos (CodigosDeEnum no da HTTP_5XX para Http5xx).
    [Fact]
    public void Todo_TipoErrorFuente_tiene_clase_explicita()
    {
        foreach (var tipo in Enum.GetValues<TipoErrorFuente>())
        {
            var codigo = TipoErrorFuenteCodigos.Codigo(tipo);
            ClasificadorErrores.EsConocido(codigo).Should().BeTrue($"{codigo} debe estar en la tabla del clasificador");
            ClasificadorErrores.Clasificar(codigo).Should().Be(
                tipo is TipoErrorFuente.Timeout or TipoErrorFuente.Red or TipoErrorFuente.Http5xx ? ClaseError.Transitorio : ClaseError.Definitivo, codigo);
        }

        ClasificadorErrores.EsConocido("CODIGO_QUE_NO_EXISTE").Should().BeFalse();
    }

    [Theory]
    [InlineData("NO_CONFIRMADO")]
    [InlineData("MODELO_NO_ACEPTADO")]
    [InlineData("DOCUMENTO_NO_COINCIDE")]
    [InlineData("DOCUMENTO_NO_HOMOLOGADO")]
    [InlineData("VEHICULO_NO_ENCONTRADO")]
    [InlineData("VEHICULO_NO_CONSULTABLE")]
    [InlineData("REQUIERE_ACTUALIZACION")]
    [InlineData("SIN_PLACA")]
    [InlineData("SIN_DOCUMENTO")]
    [InlineData("SIN_MODELO")]
    [InlineData("ORGANISMO_INCOMPLETO")]
    [InlineData("SIN_NOMBRE_PROPIETARIO")]
    public void Codigos_del_impuesto_son_definitivos_y_conocidos(string codigo)
    {
        ClasificadorErrores.EsConocido(codigo).Should().BeTrue();
        ClasificadorErrores.Clasificar(codigo).Should().Be(ClaseError.Definitivo);
        new PoliticaReintentos(1).Decidir(codigo, 0, DateTimeOffset.UnixEpoch).Reintentar.Should().BeFalse();
    }

    [Fact]
    public void Timeout_de_impuesto_si_se_reintenta_y_no_confirmado_no()
    {
        var politica = new PoliticaReintentos(1);
        politica.Decidir("TIMEOUT", 0, DateTimeOffset.UnixEpoch).Reintentar.Should().BeTrue();
        politica.Decidir("NO_CONFIRMADO", 0, DateTimeOffset.UnixEpoch).Reintentar.Should().BeFalse();
    }
}
