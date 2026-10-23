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
/// Bug #13912 — <c>validarEstadoDocumento</c> sin <c>data.codigo</c> (documento inexistente)
/// no debe lanzar; el registrador interpreta «no está en Quipux» y puede continuar al POST.
/// </summary>
public sealed class Bug13912ValidateStatusSinCodigoTests
{
    [Theory]
    [InlineData("""{"data":{}}""")]
    [InlineData("""{"data":{"descripcion":"no encontrado"}}""")]
    [InlineData("""{}""")]
    public async Task ValidateStatus_sin_codigo_devuelve_RegistroError_sin_lanzar(string jsonBody)
    {
        var ct = TestContext.Current.CancellationToken;
        var urlLogin = $"https://quipux.test/login-{Guid.NewGuid():N}";
        var http = new FakeHandler(jsonBody);
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

        var client = new QuipuxApiClient(new HttpClient(http), settings, NullLogger<QuipuxApiClient>.Instance);

        var result = await client.ValidateStatusAsync(new QuipuxValidateStatusRequest
        {
            Documento = "ICT_DAG_TR_20261023_1801_GTR553",
            CodigoDivipo = "17001",
            Consumidor = "1003",
        }, ct);

        result.Codigo.Should().Be(QuipuxCodigos.RegistroError);
        result.EstadoTramiteCodigo.Should().BeNull();
        QuipuxCodigos.EsRegistroExitoso(result.Codigo).Should().BeFalse();
    }

    private sealed class FakeHandler(string validateBody) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var json = path.EndsWith("/status", StringComparison.Ordinal)
                ? validateBody
                : """{"token":"tok-sin-exp"}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }
}
