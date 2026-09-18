using System.Net;
using System.Net.Http.Json;
using Flit.Integration.Tests.Postgres;
using FluentAssertions;
using Xunit;

namespace Flit.Integration.Tests.MarcaBlanca;

/// <summary>
/// HU #12429 AC2 — <c>POST /api/v1/auth/login</c> por el dominio de la red A
/// (<c>X-Flit-Domain: <see cref="MarcaBlancaScenario.HostA"/></c>), el test HTTP end-to-end con sello
/// que quedó pendiente en #12422 (delta-hechos #31, patrón <c>AuthLoginAndMeEndpointsTests</c>): tres
/// sujetos — credencial incorrecta de un usuario de A, credencial CORRECTA de un usuario de B (otra
/// red) y un correo inexistente — deben devolver el MISMO cuerpo, el MISMO código 401 y un tiempo de
/// respuesta dentro de la tolerancia fija (<see cref="ResponseTimingSampler"/>).
/// </summary>
[Trait("Category", "Timing")]
public sealed class LoginAntiEnumerationTests(PostgresDatabaseFixture fixture) : MarcaBlancaHttpTestBase(fixture)
{
    [PostgresFact]
    public async Task AC2_TresSujetos_MismoCuerpoYMismoCodigo()
    {
        var wrongPassword = await LoginAsync(MarcaBlancaScenario.EmailOf(MarcaBlancaScenario.UserHeadA), "no-es-la-clave");
        var otherNetworkUser = await LoginAsync(MarcaBlancaScenario.EmailOf(MarcaBlancaScenario.UserHeadB), MarcaBlancaScenario.Password);
        var nonexistentEmail = await LoginAsync("no-existe-jamas@flit.test", MarcaBlancaScenario.Password);

        wrongPassword.Status.Should().Be(HttpStatusCode.Unauthorized);
        otherNetworkUser.Status.Should().Be(HttpStatusCode.Unauthorized);
        nonexistentEmail.Status.Should().Be(HttpStatusCode.Unauthorized);

        otherNetworkUser.Body.Should().Be(wrongPassword.Body, "un usuario de otra red debe fallar exactamente igual que una credencial incorrecta");
        nonexistentEmail.Body.Should().Be(wrongPassword.Body, "un correo inexistente debe fallar exactamente igual que una credencial incorrecta");
    }

    [PostgresFact]
    public async Task AC2_DiferenciaDeTiempoEntreLosTresCasos_QuedaDentroDeLaTolerancia()
    {
        var medianWrongPassword = await ResponseTimingSampler.MedianMsAsync(
            () => LoginRawAsync(MarcaBlancaScenario.EmailOf(MarcaBlancaScenario.UserHeadA), "no-es-la-clave"));
        var medianOtherNetwork = await ResponseTimingSampler.MedianMsAsync(
            () => LoginRawAsync(MarcaBlancaScenario.EmailOf(MarcaBlancaScenario.UserHeadB), MarcaBlancaScenario.Password));
        var medianNonexistent = await ResponseTimingSampler.MedianMsAsync(
            () => LoginRawAsync("no-existe-jamas@flit.test", MarcaBlancaScenario.Password));

        Math.Abs(medianOtherNetwork - medianWrongPassword).Should().BeLessOrEqualTo(ResponseTimingSampler.ToleranceMs,
            $"medianas: sin-red={medianWrongPassword:F1}ms otra-red={medianOtherNetwork:F1}ms");
        Math.Abs(medianNonexistent - medianWrongPassword).Should().BeLessOrEqualTo(ResponseTimingSampler.ToleranceMs,
            $"medianas: sin-red={medianWrongPassword:F1}ms inexistente={medianNonexistent:F1}ms");
    }

    private async Task<(HttpStatusCode Status, string Body)> LoginAsync(string email, string password)
    {
        var response = await LoginRawAsync(email, password);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        return (response.StatusCode, body);
    }

    private async Task<HttpResponseMessage> LoginRawAsync(string email, string password)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new { email, password }),
        };
        request.Headers.Add("X-Flit-Domain", MarcaBlancaScenario.HostA);
        return await Client.SendAsync(request, TestContext.Current.CancellationToken).ConfigureAwait(false);
    }
}
