using System.Net;
using System.Text;
using Flit.Ict.Infrastructure;
using Flit.Ict.Infrastructure.Security;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Flit.Ict.Application.Tests.Auth;

/// <summary>
/// HU #13335 (Epic #13316) — core-ict pide su token de servicio a Identidad (client credentials, svc-ict,
/// platform.tramites.ict) en lugar de firmar el HMAC compartido con core-api.
/// </summary>
public sealed class IdentityServiceTokenProviderTests : IDisposable
{
    private readonly StubHandler _handler = new();
    private readonly ManualClock _clock = new(new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero));
    private readonly IdentityServiceTokenProvider _provider;

    public IdentityServiceTokenProviderTests()
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(IdentityServiceTokenProvider.HttpClientName).Returns(_ => new HttpClient(_handler, disposeHandler: false));
        _provider = new IdentityServiceTokenProvider(factory, Options.Create(new IctServiceTokenOptions
        {
            UseIdentity = true,
            TokenEndpoint = "http://gateway:4002/connect/token",
            ClientSecret = "secreto-svc-ict",
        }), _clock);
    }

    [Fact]
    public async Task PideElTokenConClientCredentialsDeSvcIct()
    {
        _handler.Respond(HttpStatusCode.OK, """{"access_token":"tok-1","expires_in":900}""");

        (await _provider.GetTokenAsync(TestContext.Current.CancellationToken)).Should().Be("tok-1");

        _handler.Requests.Should().ContainSingle();
        _handler.LastUri.Should().Be(new Uri("http://gateway:4002/connect/token"));
        _handler.LastBody.Should().Contain("grant_type=client_credentials")
            .And.Contain("client_id=svc-ict")
            .And.Contain("scope=platform.tramites.ict");
    }

    [Fact]
    public async Task LoGuardaHastaUnMinutoAntesDeVencer_YLuegoLoRenueva()
    {
        var ct = TestContext.Current.CancellationToken;
        _handler.Respond(HttpStatusCode.OK, """{"access_token":"tok-1","expires_in":900}""");
        await _provider.GetTokenAsync(ct);

        _clock.Advance(TimeSpan.FromSeconds(839));
        (await _provider.GetTokenAsync(ct)).Should().Be("tok-1");
        _handler.Requests.Should().HaveCount(1);

        _handler.Respond(HttpStatusCode.OK, """{"access_token":"tok-2","expires_in":900}""");
        _clock.Advance(TimeSpan.FromSeconds(1));
        (await _provider.GetTokenAsync(ct)).Should().Be("tok-2");
        _handler.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task VariasLlamadasALaVez_PidenUnSoloToken()
    {
        var ct = TestContext.Current.CancellationToken;
        _handler.Respond(HttpStatusCode.OK, """{"access_token":"tok-1","expires_in":900}""");

        var tokens = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => _provider.GetTokenAsync(ct)));

        tokens.Should().OnlyContain(t => t == "tok-1");
        _handler.Requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task IdentidadRechaza_FallaSinMostrarElSecreto()
    {
        _handler.Respond(HttpStatusCode.BadRequest, """{"error":"invalid_client"}""");

        var act = () => _provider.GetTokenAsync(TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message
            .Should().Contain("400").And.NotContain("secreto-svc-ict");
    }

    [Fact]
    public void SinBandera_NoRegistraNada_YConBanderaSinSecreto_NoArranca()
    {
        var services = new ServiceCollection();
        IctInfrastructureExtensions.AddIdentityServiceToken(services, Config()).Should().BeFalse();
        services.Should().NotContain(d => d.ServiceType == typeof(IdentityServiceTokenProvider));

        var act = () => IctInfrastructureExtensions.AddIdentityServiceToken(new ServiceCollection(), Config(
            ("Ict:ServiceToken:UseIdentity", "true"), ("Ict:ServiceToken:TokenEndpoint", "http://gateway:4002/connect/token")));
        act.Should().Throw<InvalidOperationException>().WithMessage("*ClientSecret*");

        IctInfrastructureExtensions.AddIdentityServiceToken(services, Config(
            ("Ict:ServiceToken:UseIdentity", "true"),
            ("Ict:ServiceToken:TokenEndpoint", "http://gateway:4002/connect/token"),
            ("Ict:ServiceToken:ClientSecret", "x"))).Should().BeTrue();
        services.Should().Contain(d => d.ServiceType == typeof(IdentityServiceTokenProvider));
    }

    public void Dispose()
    {
        _provider.Dispose();
        _handler.Dispose();
    }

    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value))).Build();

    private sealed class ManualClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private HttpStatusCode _status = HttpStatusCode.OK;
        private string _body = "{}";

        public List<HttpRequestMessage> Requests { get; } = [];

        public Uri? LastUri { get; private set; }

        public string LastBody { get; private set; } = string.Empty;

        public void Respond(HttpStatusCode status, string body) => (_status, _body) = (status, body);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            LastUri = request.RequestUri;
            LastBody = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            await Task.Yield();
            return new HttpResponseMessage(_status) { Content = new StringContent(_body, Encoding.UTF8, "application/json") };
        }
    }
}
