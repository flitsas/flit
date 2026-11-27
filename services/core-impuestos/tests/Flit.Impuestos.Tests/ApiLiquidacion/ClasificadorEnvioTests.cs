using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Authentication;
using Flit.Impuestos.Api.Modulos.ApiLiquidacion;
using FluentAssertions;
using Xunit;

namespace Flit.Impuestos.Tests.ApiLiquidacion;

/// <summary>
/// HU #14694 (M06, plan §5.6, D-ALTA-REINTENTOS) — <see cref="ClasificadorEnvio"/>: solo hay «no salió» (reintento automático)
/// con conexión rechazada, host no encontrado, error de resolución de nombre o fallo del handshake TLS; timeout, corte tras
/// enviar, cualquier otra excepción y todo 5xx son «incierto»; el resto de estados es respuesta con su <c>error.code</c>. La
/// cancelación del llamador no se clasifica.
/// </summary>
/// <example>
/// <code>
/// ClasificadorEnvio.DeExcepcion&lt;object&gt;(new TaskCanceledException(), ct).Clase; // Incierto
/// </code>
/// </example>
public sealed class ClasificadorEnvioTests
{
    /// <summary>Excepciones de prueba por nombre (xUnit v3 no admite <c>TheoryData&lt;Exception&gt;</c> con inicializador).</summary>
    private static Exception Excepcion(string caso) => caso switch
    {
        "rechazada" => new HttpRequestException("rechazada", new SocketException((int)SocketError.ConnectionRefused)),
        "host" => new HttpRequestException("dns", new SocketException((int)SocketError.HostNotFound)),
        "dns" => new HttpRequestException(HttpRequestError.NameResolutionError, "dns"),
        "tls" => new HttpRequestException("tls", new AuthenticationException("handshake")),
        "socket" => new SocketException((int)SocketError.ConnectionRefused),
        "timeout" => new TaskCanceledException("timeout", new TimeoutException()),
        "corte" => new HttpIOException(HttpRequestError.ResponseEnded, "corte"),
        "reset" => new HttpRequestException("reset", new SocketException((int)SocketError.ConnectionReset)),
        "otro" => new HttpRequestException("otro"),
        "io" => new IOException("corte tras escribir"),
        _ => throw new ArgumentOutOfRangeException(nameof(caso)),
    };

    [Theory]
    [InlineData("rechazada")]
    [InlineData("host")]
    [InlineData("dns")]
    [InlineData("tls")]
    [InlineData("socket")]
    public void Hay_certeza_de_que_no_salio(string caso)
    {
        var ex = Excepcion(caso);
        ClasificadorEnvio.NoSalio(ex).Should().BeTrue();
        ClasificadorEnvio.DeExcepcion<object>(ex, CancellationToken.None).Clase.Should().Be(ClaseEnvio.NoSalio);
    }

    [Theory]
    [InlineData("timeout")]
    [InlineData("corte")]
    [InlineData("reset")]
    [InlineData("otro")]
    [InlineData("io")]
    public void Lo_demas_es_incierto(string caso)
    {
        var ex = Excepcion(caso);
        ClasificadorEnvio.NoSalio(ex).Should().BeFalse();
        var r = ClasificadorEnvio.DeExcepcion<object>(ex, CancellationToken.None);
        r.Clase.Should().Be(ClaseEnvio.Incierto);
        r.Motivo.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void La_cancelacion_del_llamador_sigue_su_curso()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var accion = () => ClasificadorEnvio.DeExcepcion<object>(new TaskCanceledException("cancelada"), cts.Token);

        accion.Should().Throw<OperationCanceledException>();
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public void Un_5xx_es_incierto_con_su_estado(HttpStatusCode status)
    {
        var r = ClasificadorEnvio.DeEstado<object>(status, "{\"error\":{\"code\":\"storage_unavailable\",\"message\":\"x\"}}");

        r.Clase.Should().Be(ClaseEnvio.Incierto);
        r.Status.Should().Be((int)status);
        r.CodigoError.Should().Be(ErroresApi.StorageUnavailable);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "{\"error\":{\"code\":\"bad_request\",\"message\":\"legal_name\"}}", "bad_request")]
    [InlineData(HttpStatusCode.Unauthorized, "{\"error\":{\"code\":\"unauthorized\",\"message\":\"x\"}}", "unauthorized")]
    [InlineData(HttpStatusCode.NotFound, "{\"error\":{\"code\":\"not_found\",\"message\":\"x\"}}", "not_found")]
    [InlineData(HttpStatusCode.Conflict, "{\"error\":{\"code\":\"tax_id_taken\",\"message\":\"x\"}}", "tax_id_taken")]
    [InlineData(HttpStatusCode.UnsupportedMediaType, "{\"error\":{\"code\":\"unsupported_media_type\",\"message\":\"x\"}}", "unsupported_media_type")]
    [InlineData(HttpStatusCode.UnprocessableEntity, "{\"error\":{\"code\":\"webhook_not_configured\",\"message\":\"x\"}}", "webhook_not_configured")]
    [InlineData(HttpStatusCode.Unauthorized, "", null)]
    [InlineData(HttpStatusCode.Conflict, "<html>", null)]
    public void Un_4xx_es_respuesta_con_su_codigo(HttpStatusCode status, string cuerpo, string? codigo)
    {
        var r = ClasificadorEnvio.DeEstado<object>(status, cuerpo);

        r.Clase.Should().Be(ClaseEnvio.Respuesta);
        r.Status.Should().Be((int)status);
        r.CodigoError.Should().Be(codigo);
        r.Exitoso.Should().BeFalse();
    }

    [Theory]
    [InlineData("{\"error\":{\"code\":\"tax_id_taken\",\"message\":\"ya\"}}", "tax_id_taken")]
    [InlineData("{\"error\":{\"message\":\"sin codigo\"}}", null)]
    [InlineData("{\"error\":\"texto\"}", null)]
    [InlineData("[]", null)]
    [InlineData("no json", null)]
    [InlineData(null, null)]
    public void ErroresApi_lee_solo_el_codigo(string? cuerpo, string? codigo)
    {
        ErroresApi.LeerCodigo(cuerpo).Should().Be(codigo);
    }

    [Fact]
    public void Solo_clasifica_excepciones_de_la_llamada()
    {
        ClasificadorEnvio.Clasificable(new HttpRequestException("x")).Should().BeTrue();
        ClasificadorEnvio.Clasificable(new TaskCanceledException()).Should().BeTrue();
        ClasificadorEnvio.Clasificable(new IOException()).Should().BeTrue();
        ClasificadorEnvio.Clasificable(new ArgumentException()).Should().BeFalse("un error de programación no es un resultado del envío");
    }
}
