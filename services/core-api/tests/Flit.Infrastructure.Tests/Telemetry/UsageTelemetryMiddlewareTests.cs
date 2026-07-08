using System.Security.Claims;
using FluentAssertions;
using Flit.Api.Middleware;
using Flit.Infrastructure.Telemetry;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Xunit;

namespace Flit.Infrastructure.Tests.Telemetry;

/// <summary>
/// Reportes2 HU-A — middleware de telemetría server-side: mapeo ruta→módulo del contrato §7,
/// muestreo de <c>api_module_access</c> (1 por usuario+módulo+minuto), evento
/// <c>wizard_server_view</c> en <c>GET /instances/{id}/wizard</c> y silencio total sin
/// autenticación o sin tenant. Nunca lanza ni altera la respuesta.
/// </summary>
public sealed class UsageTelemetryMiddlewareTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    [Theory]
    [InlineData("/api/v1/tramites/instances", "tramites")]
    [InlineData("/api/v1/analytics/overview", "reportes")]
    [InlineData("/api/v1/security/roles", "usuarios")]
    [InlineData("/api/v1/admin/companies", "admin")]
    [InlineData("/api/v1/tramites/biometric-validations", "validaciones")]
    public async Task Mapea_la_ruta_al_modulo_del_contrato(string path, string expectedModule)
    {
        var queue = new CapturingQueue();
        var middleware = NewMiddleware(queue);

        await middleware.InvokeAsync(NewContext(path));

        queue.Events.Should().ContainSingle();
        var evt = queue.Events[0];
        evt.EventType.Should().Be("api_module_access");
        evt.Module.Should().Be(expectedModule);
        evt.TenantId.Should().Be(TenantId);
        evt.UserId.Should().Be(UserId);
    }

    [Fact]
    public async Task Ruta_fuera_del_mapa_no_genera_evento()
    {
        var queue = new CapturingQueue();
        var middleware = NewMiddleware(queue);

        await middleware.InvokeAsync(NewContext("/api/v1/health"));

        queue.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Muestrea_un_evento_por_usuario_modulo_y_minuto()
    {
        var queue = new CapturingQueue();
        var middleware = NewMiddleware(queue);

        // Tres requests seguidos del mismo usuario al mismo módulo (mismo minuto).
        await middleware.InvokeAsync(NewContext("/api/v1/tramites/instances"));
        await middleware.InvokeAsync(NewContext("/api/v1/tramites/instances/otros"));
        await middleware.InvokeAsync(NewContext("/api/v1/tramites/transit-offices"));
        // Otro módulo del mismo usuario: SÍ cuenta (clave usuario+módulo).
        await middleware.InvokeAsync(NewContext("/api/v1/analytics/overview"));

        queue.Events.Should().HaveCount(2);
        queue.Events.Select(e => e.Module).Should().BeEquivalentTo(["tramites", "reportes"]);
    }

    [Fact]
    public async Task Get_wizard_emite_wizard_server_view_con_el_instance_id()
    {
        var queue = new CapturingQueue();
        var middleware = NewMiddleware(queue);
        var instanceId = Guid.NewGuid();

        await middleware.InvokeAsync(NewContext($"/api/v1/tramites/instances/{instanceId}/wizard"));

        var wizardView = queue.Events.Should()
            .ContainSingle(e => e.EventType == "wizard_server_view").Subject;
        wizardView.Module.Should().Be("tramites");
        wizardView.ProcedureInstanceId.Should().Be(instanceId);
        // Además del wizard_server_view se registra el api_module_access muestreado del módulo.
        queue.Events.Should().ContainSingle(e => e.EventType == "api_module_access");
    }

    [Fact]
    public async Task Sin_autenticacion_no_registra_nada()
    {
        var queue = new CapturingQueue();
        var middleware = NewMiddleware(queue);
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = "/api/v1/tramites/instances";

        await middleware.InvokeAsync(context);

        queue.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Autenticado_sin_tenant_no_registra_nada()
    {
        var queue = new CapturingQueue();
        var middleware = NewMiddleware(queue);
        var context = NewContext("/api/v1/tramites/instances", includeTenant: false);

        await middleware.InvokeAsync(context);

        queue.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Deshabilitado_no_registra_nada()
    {
        var queue = new CapturingQueue();
        var middleware = NewMiddleware(queue, enabled: false);

        await middleware.InvokeAsync(NewContext("/api/v1/tramites/instances"));

        queue.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Un_fallo_de_la_cola_no_rompe_el_request()
    {
        var middleware = new UsageTelemetryMiddleware(
            _ => Task.CompletedTask,
            new ThrowingQueue(),
            Options.Create(new AnalyticsTelemetryOptions()));

        var act = () => middleware.InvokeAsync(NewContext("/api/v1/tramites/instances"));

        await act.Should().NotThrowAsync();
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static UsageTelemetryMiddleware NewMiddleware(IUsageEventQueue queue, bool enabled = true) =>
        new(_ => Task.CompletedTask, queue,
            Options.Create(new AnalyticsTelemetryOptions { Enabled = enabled }));

    private static DefaultHttpContext NewContext(string path, bool includeTenant = true)
    {
        var claims = new List<Claim> { new("sub", UserId.ToString()), new("role", "AdminCompany") };
        if (includeTenant)
            claims.Add(new Claim("tenant_id", TenantId.ToString()));

        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "TestAuth")),
        };
        context.Request.Method = "GET";
        context.Request.Path = path;
        return context;
    }

    private sealed class CapturingQueue : IUsageEventQueue
    {
        public List<UsageEventRecord> Events { get; } = [];

        public bool TryEnqueue(UsageEventRecord evt)
        {
            Events.Add(evt);
            return true;
        }
    }

    private sealed class ThrowingQueue : IUsageEventQueue
    {
        public bool TryEnqueue(UsageEventRecord evt) =>
            throw new InvalidOperationException("Cola rota (simulada).");
    }
}
