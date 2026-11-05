using Flit.Consultas.Grpc.V1;
using Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;
using Flit.Diagnostico.Domain.Consultas;
using FluentAssertions;
using Grpc.Core;
using Xunit;

namespace Flit.Diagnostico.Tests.Consultas;

/// <summary>
/// HU #13791 (F05-H2) — <see cref="TraductorErroresConsultas"/>: los errores de core-consultas se traducen a códigos de
/// <c>tipo_error</c> de la tabla de <see cref="ClasificadorErrores"/>, así que los reintentos de #13737
/// (<see cref="PoliticaReintentos"/>) los tratan sin cambios: <c>Unavailable</c>, <c>DeadlineExceeded</c>,
/// <c>ErrorProveedor.transitorio</c> y un chequeo en ERROR (el proveedor no respondió) son transitorios; <c>InvalidArgument</c>,
/// <c>PermissionDenied</c> y el resto, definitivos. El mensaje nunca copia el detalle del proveedor (puede traer documentos).
/// </summary>
/// <example>
/// <code>
/// dotnet test tests/Flit.Diagnostico.Tests/Flit.Diagnostico.Tests.csproj --no-build --filter "FullyQualifiedName~TraductorErroresConsultasTests"
/// </code>
/// </example>
public sealed class TraductorErroresConsultasTests
{
    private const string DetalleConDocumento = "documento 9999912345 no encontrado";

    [Fact]
    public void Unavailable_y_deadline_son_transitorios_y_InvalidArgument_definitivo()
    {
        var caido = TraductorErroresConsultas.Traducir(Rpc(StatusCode.Unavailable));
        var tarde = TraductorErroresConsultas.Traducir(Rpc(StatusCode.DeadlineExceeded));
        var invalido = TraductorErroresConsultas.Traducir(Rpc(StatusCode.InvalidArgument));
        var sinPermiso = TraductorErroresConsultas.Traducir(Rpc(StatusCode.PermissionDenied));
        var sinToken = TraductorErroresConsultas.Traducir(Rpc(StatusCode.Unauthenticated));
        var interno = TraductorErroresConsultas.Traducir(Rpc(StatusCode.Internal));

        caido.TipoError.Should().Be("RED");
        caido.EsTransitorio.Should().BeTrue();
        tarde.TipoError.Should().Be("TIMEOUT");
        tarde.EsTransitorio.Should().BeTrue();
        invalido.EsTransitorio.Should().BeFalse();
        invalido.TipoError.Should().Be("ESTADO_NO_OK");
        sinPermiso.TipoError.Should().Be("AUTENTICACION");
        sinToken.TipoError.Should().Be("AUTENTICACION");
        interno.EsTransitorio.Should().BeFalse("lo que no está en la tabla es definitivo: no se gasta consumo");

        ErrorConsulta[] todos = [caido, tarde, invalido, sinPermiso, sinToken, interno];
        todos.Should().OnlyContain(e => ClasificadorErrores.EsConocido(e.TipoError), "solo códigos de la tabla de #13737");
        todos.Should().OnlyContain(e => !e.Mensaje.Contains("9999912345"), "el detalle del proveedor no se copia");
        var politica = new PoliticaReintentos(1);
        politica.Decidir(caido.TipoError, 0, DateTimeOffset.UnixEpoch).Reintentar.Should().BeTrue();
        politica.Decidir(tarde.TipoError, 0, DateTimeOffset.UnixEpoch).Reintentar.Should().BeTrue();
        politica.Decidir(invalido.TipoError, 0, DateTimeOffset.UnixEpoch).Reintentar.Should().BeFalse();
    }

    [Fact]
    public void ErrorProveedor_y_chequeo_en_error_respetan_su_clase()
    {
        TraductorErroresConsultas.Traducir((ErrorProveedor?)null).Should().BeNull();
        TraductorErroresConsultas.Traducir(new ErrorProveedor()).Should().BeNull("un mensaje vacío sin transitorio es la ausencia de error en proto3");
        var transitorio = TraductorErroresConsultas.Traducir(new ErrorProveedor { Mensaje = DetalleConDocumento, Transitorio = true })!;
        var definitivo = TraductorErroresConsultas.Traducir(new ErrorProveedor { Mensaje = DetalleConDocumento, Transitorio = false })!;
        transitorio.EsTransitorio.Should().BeTrue();
        definitivo.EsTransitorio.Should().BeFalse();
        definitivo.TipoError.Should().Be("ESTADO_NO_OK");
        transitorio.Mensaje.Should().NotContain("9999912345");
        definitivo.Mensaje.Should().NotContain("9999912345");

        var ok = new ResultadoConsulta { Proveedor = "verifik", Semaforo = Semaforo.Verde };
        ok.Chequeos.Add(new Chequeo { Clave = "multas", Estado = EstadoChequeo.Ok });
        TraductorErroresConsultas.Traducir(ok).Should().BeNull();
        TraductorErroresConsultas.Traducir((ResultadoConsulta?)null)!.TipoError.Should().Be("RESPUESTA_INVALIDA");
        var sinRespuesta = new ResultadoConsulta { Proveedor = "verifik", Semaforo = Semaforo.Rojo };
        sinRespuesta.Chequeos.Add(new Chequeo { Clave = "provider", Estado = EstadoChequeo.Error, Mensaje = DetalleConDocumento });
        var caido = TraductorErroresConsultas.Traducir(sinRespuesta)!;
        caido.EsTransitorio.Should().BeTrue("el proveedor no respondió: se reintenta con backoff");
        caido.Mensaje.Should().NotContain("9999912345");
    }

    [Theory]
    [InlineData("verifik", ServicioConsulta.Verifik)]
    [InlineData("verifik_rues", ServicioConsulta.Verifik)]
    [InlineData("kyverum_runt", ServicioConsulta.Kyverum)]
    [InlineData("intempo", ServicioConsulta.Intempo)]
    [InlineData("fasecolda", ServicioConsulta.Fasecolda)]
    [InlineData("mercado_libre", ServicioConsulta.Mercadolibre)]
    [InlineData("base_gravable", ServicioConsulta.Basegravable)]
    [InlineData("proveedor_nuevo", ServicioConsulta.Otro)]
    public void Proveedor_se_traduce_a_ServicioConsulta(string proveedor, ServicioConsulta esperado)
    {
        TraductorErroresConsultas.Servicio(proveedor).Should().Be(esperado);
        TraductorErroresConsultas.Servicio(" ").Should().BeNull("sin proveedor no hay servicio que registrar");
    }

    private static RpcException Rpc(StatusCode codigo) => new(new Status(codigo, DetalleConDocumento));
}
