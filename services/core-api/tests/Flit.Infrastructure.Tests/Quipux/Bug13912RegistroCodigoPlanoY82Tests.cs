using System.Net;
using System.Text;
using Flit.Infrastructure.Quipux;
using Flit.Modules.Quipux.Domain.Codigos;
using Flit.Modules.Quipux.Domain.Configuracion;
using Flit.Modules.Quipux.Domain.Puertos;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Flit.Infrastructure.Tests.Quipux;

/// <summary>
/// Bug #13912 — PDN responde <c>registroDocumento</c> con cuerpo plano
/// <c>{"codigo":82,"descripcion":"…ya se encuentra almacenado…"}</c> (catálogo proveedor).
/// Sin leer raíz, se trataba como "Respuesta inválida" y se reintentaba.
/// </summary>
public sealed class Bug13912RegistroCodigoPlanoY82Tests
{
    [Fact]
    public async Task Register_cuerpo_plano_codigo_82_lee_codigo()
    {
        var ct = TestContext.Current.CancellationToken;
        const string body =
            """{"codigo":82,"descripcion":"El documento a registrar ya se encuentra almacenado en el sistema"}""";

        var client = CreateClient(body);
        var result = await client.RegisterDocumentAsync(SampleRegisterRequest(), ct);

        result.Codigo.Should().Be(QuipuxCodigos.RegistroYaExiste);
        QuipuxCodigos.EsRegistroExitoso(result.Codigo).Should().BeTrue();
        QuipuxCodigos.EsRegistroYaExistente(result.Codigo).Should().BeTrue();
        result.Descripcion.Should().Contain("almacenado");
    }

    [Fact]
    public async Task Register_cuerpo_plano_codigo_81_lee_codigo()
    {
        var ct = TestContext.Current.CancellationToken;
        const string body =
            """{"codigo":81,"descripcion":"Los datos se almacenaron correctamente"}""";

        var client = CreateClient(body);
        var result = await client.RegisterDocumentAsync(SampleRegisterRequest(), ct);

        result.Codigo.Should().Be(QuipuxCodigos.RegistroOk);
        QuipuxCodigos.EsRegistroExitoso(result.Codigo).Should().BeTrue();
    }

    [Theory]
    [InlineData(81, QuipuxRegistroClasificacion.Exitoso)]
    [InlineData(82, QuipuxRegistroClasificacion.YaRegistrado)]
    [InlineData(71, QuipuxRegistroClasificacion.Reintentable)]
    [InlineData(72, QuipuxRegistroClasificacion.Reintentable)]
    [InlineData(85, QuipuxRegistroClasificacion.Reintentable)]
    [InlineData(67, QuipuxRegistroClasificacion.Definitivo)]
    [InlineData(83, QuipuxRegistroClasificacion.Definitivo)]
    [InlineData(84, QuipuxRegistroClasificacion.Definitivo)]
    public void Catalogo_clasifica_codigos_proveedor(int codigo, QuipuxRegistroClasificacion expected)
    {
        QuipuxRegistroDocumentoRespuesta.Clasificar(codigo).Should().Be(expected);
    }

    private static QuipuxApiClient CreateClient(string registerBody)
    {
        var http = new FakeHandler(registerBody);
        var settings = Substitute.For<IQuipuxSettingsRepository>();
        settings.GetAsync(Arg.Any<CancellationToken>()).Returns(new QuipuxSettings
        {
            Enabled = true,
            UrlLogin = "https://quipux.test/login",
            UrlRegisterDocument = "https://quipux.test/register",
            UrlValidateStatus = "https://quipux.test/status",
            Username = "u",
            Password = "p",
            ConsumerCode = "1003",
            Bucket = "b",
            AwsAccessKeyId = "k",
            AwsSecretAccessKey = "s",
            OfficerDocumentNumber = "900000000",
        });

        return new QuipuxApiClient(new HttpClient(http), settings, NullLogger<QuipuxApiClient>.Instance);
    }

    private static QuipuxRegisterRequest SampleRegisterRequest() => new()
    {
        Placa = "GTR553",
        Vin = "",
        CodigoDivipo = "17001",
        Consumidor = "1003",
        TipoTramite = 16,
        TipoRequisito = 51,
        Documento = "ICT_DAG_TR_20261026_1531_GTR553",
        TipoDocumentoPropietario = 2,
        NroDocumentoPropietario = "123",
        TipoDocumentoFuncionario = 3,
        NroDocumentoFuncionario = "900000000",
        ContenidoDocumento = "FLIT/ICT_DAG_TR_20261026_1531_GTR553.pdf",
    };

    private sealed class FakeHandler(string businessBody) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var json = path.Contains("/login", StringComparison.Ordinal)
                ? """{"token":"tok-sin-exp"}"""
                : path.EndsWith("/register", StringComparison.Ordinal)
                    ? businessBody
                    : """{"data":{}}""";

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }
}
