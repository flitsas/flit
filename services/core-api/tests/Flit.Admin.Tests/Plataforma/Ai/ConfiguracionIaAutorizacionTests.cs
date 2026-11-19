using System.Net;
using System.Security.Claims;
using Flit.Api.Authorization;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Flit.Admin.Tests.Plataforma.Ai;

/// <summary>
/// Review HU-6 · CR-2 / CR-6 (ADR-0091, SP-15/SP-20) — <c>ObtenerConfiguracionIa</c> exige la policy
/// <c>DiagnosticoIaServicio</c> (metadata del endpoint registrado por el host real) y, cuando esa policy rechaza la llamada
/// antes de llegar al servicio gRPC, el handler de resultado de autorización audita <c>ia.llave.denegada</c> con sub, jti, IP
/// y motivo <c>POLICY_RECHAZADA</c>, sin token, y delega la respuesta. Cualquier otra policy fallida no deja auditoría de IA.
/// </summary>
/// <remarks>Uso de ejemplo: <c>await handler.HandleAsync(next, httpContext, policy, PolicyAuthorizationResult.Forbid(failure))</c>.</remarks>
public sealed class ConfiguracionIaAutorizacionTests(ConfiguracionIaAutorizacionTests.Host host)
    : IClassFixture<ConfiguracionIaAutorizacionTests.Host>
{
    private const string RutaObtener = "/flit.platform.v1.ConfiguracionIaService/ObtenerConfiguracionIa";
    private const string TokenCentinela = "eyJhbGciOiJSUzI1NiJ9.CENTINELA-TOKEN.firma"; // gitleaks:allow — valor de prueba
    private const string Jti = "jti-0123456789abcdef";

    /// <summary>Un host para la clase, con un proveedor de logs que captura los eventos.</summary>
    public sealed class Host : HostPorClase
    {
        public LogsCapturados Logs { get; } = new();

        protected override WebApplicationFactory<Program> Configurar(WebApplicationFactory<Program> factory) =>
            factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<ILoggerProvider>(Logs)));
    }

    private IServiceProvider Servicios => host.Factory.Services;

    [Fact]
    public void CR6_ObtenerConfiguracionIa_EstaMapeado_YExigeLaPolicyDeIa()
    {
        var endpoint = Servicios.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText == RutaObtener)
            .ToList();

        endpoint.Should().ContainSingle("el método gRPC queda mapeado una sola vez");
        endpoint[0].Metadata.GetOrderedMetadata<IAuthorizeData>().Select(a => a.Policy)
            .Should().Contain(DiagnosticoIaServicioAuthorization.Policy);
    }

    [Fact]
    public void CR2_ElHandlerDeResultadoDeAutorizacion_EsElQueAuditaLaPolicyDeIa() =>
        Servicios.GetRequiredService<IAuthorizationMiddlewareResultHandler>().GetType().Name
            .Should().Be("DiagnosticoIaAuthorizationResultHandler");

    [Theory]
    [InlineData("svc-ict", "platform.ia.diagnostico", "otro svc-* con aud=tramites")]
    [InlineData("svc-diagnostico", "platform.usuarios.diagnostico", "svc-diagnostico sin el scope de IA")]
    public async Task CR2_PolicyDeIaRechazada_AuditaIaLlaveDenegada_SinSecretos_YRechaza(string sub, string scope, string caso)
    {
        host.Logs.Limpiar();
        var (contexto, siguiente) = await Autorizar(DiagnosticoIaServicioAuthorization.Policy, sub, scope);

        siguiente.Should().BeFalse(caso);
        contexto.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden, caso);
        var auditoria = host.Logs.Entradas.Where(e => e.Evento == "ia.llave.denegada").ToList();
        auditoria.Should().ContainSingle(caso);
        var e = auditoria[0];
        e.Nivel.Should().Be(LogLevel.Warning);
        e.Texto.Should().Contain($"sub={sub}").And.Contain($"jti={Jti}").And.Contain("reason=POLICY_RECHAZADA")
            .And.Contain("ip=ipv4:10.1.2.3:5555");
        host.Logs.Entradas.Should().NotContain(x => x.Texto.Contains("CENTINELA", StringComparison.Ordinal), "ni el token ni partes de él");
    }

    [Fact]
    public async Task CR2_OtraPolicyFallida_NoAuditaIa()
    {
        host.Logs.Limpiar();
        var (contexto, siguiente) = await Autorizar(DiagnosticoUsuariosServicioAuthorization.Policy, "svc-ict", "platform.usuarios.diagnostico");

        siguiente.Should().BeFalse();
        contexto.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        host.Logs.Entradas.Should().NotContain(e => e.Evento == "ia.llave.denegada");
    }

    private async Task<(HttpContext Contexto, bool Siguiente)> Autorizar(string nombrePolicy, string sub, string scope)
    {
        var policy = await Servicios.GetRequiredService<IAuthorizationPolicyProvider>().GetPolicyAsync(nombrePolicy);
        policy.Should().NotBeNull();
        var usuario = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", sub), new Claim("aud", "tramites"), new Claim("scope", scope), new Claim("jti", Jti)], "Bearer"));
        // IAuthorizationService depende de handlers Scoped: se resuelve desde un scope, como en una petición.
        var scopeDi = Servicios.CreateScope();
        var resultado = await scopeDi.ServiceProvider.GetRequiredService<IAuthorizationService>().AuthorizeAsync(usuario, null, policy!);
        resultado.Succeeded.Should().BeFalse("la policy debe rechazar este token");

        var contexto = new DefaultHttpContext { RequestServices = scopeDi.ServiceProvider, User = usuario };
        contexto.Request.Headers.Authorization = "Bearer " + TokenCentinela;
        contexto.Connection.RemoteIpAddress = IPAddress.Parse("10.1.2.3");
        contexto.Connection.RemotePort = 5555;
        contexto.Response.Body = new MemoryStream();
        contexto.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(new AuthorizeAttribute(nombrePolicy)), "prueba"));

        var siguiente = false;
        var handler = Servicios.GetRequiredService<IAuthorizationMiddlewareResultHandler>();
        await handler.HandleAsync(_ =>
        {
            siguiente = true;
            return Task.CompletedTask;
        }, contexto, policy!, PolicyAuthorizationResult.Forbid(resultado.Failure));
        return (contexto, siguiente);
    }

    /// <summary>Proveedor de logs de prueba: guarda nivel, nombre del evento y texto formateado.</summary>
    public sealed class LogsCapturados : ILoggerProvider
    {
        private readonly List<(LogLevel Nivel, string? Evento, string Texto)> _entradas = [];

        public IReadOnlyList<(LogLevel Nivel, string? Evento, string Texto)> Entradas
        {
            get
            {
                lock (_entradas)
                    return [.. _entradas];
            }
        }

        public void Limpiar()
        {
            lock (_entradas)
                _entradas.Clear();
        }

        public ILogger CreateLogger(string categoryName) => new Capturador(this);

        public void Dispose()
        {
        }

        private sealed class Capturador(LogsCapturados dueno) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                var texto = formatter(state, exception) + (exception?.ToString() ?? string.Empty);
                lock (dueno._entradas)
                    dueno._entradas.Add((logLevel, eventId.Name, texto));
            }
        }
    }
}
