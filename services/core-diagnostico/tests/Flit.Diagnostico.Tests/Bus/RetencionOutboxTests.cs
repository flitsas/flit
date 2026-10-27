using System.Globalization;
using Flit.Diagnostico.Api;
using Flit.Diagnostico.Tests.Api;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Flit.Diagnostico.Tests.Bus;

/// <summary>
/// HU #13727 (ADR-0072 R-2, Habeas Data) — <c>diagnostico.outbox</c> lleva en claro los trabajos de consulta (documentos de
/// propietarios y locatarios): el <c>appsettings.json</c> del servicio enciende la retención del SDK en 24 h y el host
/// registra la purga de filas publicadas. Sin base (no arranca el host).
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~RetencionOutboxTests
/// </code>
/// </example>
public sealed class RetencionOutboxTests
{
    [Fact]
    public void La_outbox_purga_las_filas_publicadas_a_las_24_horas()
    {
        using var app = Program.Build([], b =>
        {
            b.WebHost.UseTestServer();
            b.Configuration.AddInMemoryCollection(ApiDePrueba.Configuracion(ApiDePrueba.BaseInalcanzable));
        });

        app.Configuration["Platform:Messaging:RetencionPublicados"].Should().NotBeNull("appsettings.json enciende la retención");
        TimeSpan.Parse(app.Configuration["Platform:Messaging:RetencionPublicados"]!, CultureInfo.InvariantCulture)
            .Should().Be(TimeSpan.FromHours(24));
        app.Services.GetServices<IHostedService>().Select(s => s.GetType().Name)
            .Should().ContainSingle(n => n.StartsWith("OutboxPurgeService", StringComparison.Ordinal));
    }
}
