using System.Net;
using System.Text;
using Flit.Infrastructure.Quipux;
using Flit.Modules.Quipux.Domain.Configuracion;
using Flit.Modules.Quipux.Domain.Puertos;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Flit.Infrastructure.Tests.Quipux;

/// <summary>
/// Bug #13912 — PDN responde <c>registroDocumento</c> con envelope
/// <c>{ "responseData": { "data": { "codigo": 81 }, "status": 200 } }</c>.
/// Sin unwrap, HTTP 200 + <c>data.codigo</c> null ⇒ "Respuesta inválida" y no radica.
/// </summary>
public sealed class Bug13912RegistroResponseDataEnvelopeTests
{
    [Fact]
    public async Task Register_con_envelope_responseData_lee_codigo_81()
    {
        var ct = TestContext.Current.CancellationToken;
        const string body = """
            {
              "responseData": {
                "data": {
                  "codigo": 81,
                  "descripcion": "Los datos se almacenaron correctamente"
                },
                "status": 200
              }
            }
            """;

        var client = CreateClient(body);
        var result = await client.RegisterDocumentAsync(SampleRegisterRequest(), ct);

        result.Codigo.Should().Be(81);
        result.Descripcion.Should().Contain("almacenaron");
    }

    [Fact]
    public async Task Register_con_data_en_raiz_sigue_funcionando()
    {
        var ct = TestContext.Current.CancellationToken;
        const string body = """{"data":{"codigo":81,"descripcion":"ok"}}""";

        var client = CreateClient(body);
        var result = await client.RegisterDocumentAsync(SampleRegisterRequest(), ct);

        result.Codigo.Should().Be(81);
    }

    [Fact]
    public async Task Register_sin_codigo_lanza_definitivo()
    {
        var ct = TestContext.Current.CancellationToken;
        const string body = """{"responseData":{"data":{"descripcion":"sin codigo"},"status":200}}""";

        var client = CreateClient(body);
        var act = async () => await client.RegisterDocumentAsync(SampleRegisterRequest(), ct);

        var ex = await act.Should().ThrowAsync<QuipuxException>();
        ex.Which.IsTransient.Should().BeFalse();
        ex.Which.Message.Should().Contain("Respuesta inválida");
    }

    [Fact]
    public async Task ValidateStatus_con_envelope_responseData_lee_codigo_y_estado()
    {
        var ct = TestContext.Current.CancellationToken;
        const string body = """
            {
              "responseData": {
                "data": {
                  "codigo": 81,
                  "descripcion": "Los datos se almacenaron correctamente",
                  "estadoTramite": { "codigo": 1, "descripcion": null }
                },
                "status": 200
              }
            }
            """;

        var client = CreateClient(body, forValidate: true);
        var result = await client.ValidateStatusAsync(new QuipuxValidateStatusRequest
        {
            Documento = "ICT_DAG_TR_20261023_2140_GTR553",
            CodigoDivipo = "17001",
            Consumidor = "1003",
        }, ct);

        result.Codigo.Should().Be(81);
        result.EstadoTramiteCodigo.Should().Be(1);
    }

    private static QuipuxApiClient CreateClient(string registerOrValidateBody, bool forValidate = false)
    {
        var urlLogin = $"https://quipux.test/login-{Guid.NewGuid():N}";
        var http = new FakeHandler(registerOrValidateBody, forValidate);
        var settings = Substitute.For<IQuipuxSettingsRepository>();
        settings.GetAsync(Arg.Any<CancellationToken>()).Returns(new QuipuxSettings
        {
            Enabled = true,
            UrlLogin = urlLogin,
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
        Documento = "ICT_DAG_TR_20261023_2140_GTR553",
        TipoDocumentoPropietario = 3,
        NroDocumentoPropietario = "890903938",
        TipoDocumentoFuncionario = 3,
        NroDocumentoFuncionario = "901698038",
        ContenidoDocumento = "FLIT/ICT_DAG_TR_20261023_2140_GTR553.pdf",
    };

    private sealed class FakeHandler(string businessBody, bool forValidate) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            string json;
            if (path.Contains("/login", StringComparison.Ordinal))
                json = """{"token":"tok-sin-exp"}""";
            else if (forValidate && path.EndsWith("/status", StringComparison.Ordinal))
                json = businessBody;
            else if (!forValidate && path.EndsWith("/register", StringComparison.Ordinal))
                json = businessBody;
            else
                json = """{"data":{}}""";

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }
}
