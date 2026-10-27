using System.Text.Json;
using Flit.Consultas.Api;
using Flit.Consultas.Api.Bus;
using Flit.Consultas.Api.Simulacion;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Flit.Consultas.Tests.Bus;

/// <summary>
/// HU #13727 (ADR-0075, D1, Q3, Q5) — configuración de los dos consumidores de core-consultas: lotes en
/// <c>consultas.diagnostico</c> con Concurrencia 8 / Prefetch 16, Express en <c>consultas.diagnostico-express</c> con 4 / 8, ambos con
/// <c>ProductoresPermitidos = ["diagnostico"]</c> (no se puede vaciar por configuración), y la simulación que no arranca fuera de
/// LOCAL o DEV.
/// <para>Uso de ejemplo: <c>OpcionesConsumidoresDiagnostico.Desde(configuration).Lote</c>.</para>
/// </summary>
public sealed class ConfiguracionConsumidoresTests
{
    [Fact]
    public void Por_defecto_lote_8_16_express_4_8_tiempo_maximo_90s_y_solo_diagnostico()
    {
        var o = OpcionesConsumidoresDiagnostico.Desde(Config([]));

        (o.Lote, o.Express, o.TiempoMaximo).Should().Be((new ColaConsumidor(8, 16), new ColaConsumidor(4, 8), TimeSpan.FromSeconds(90)));
        o.ProductoresPermitidos.Should().Equal("diagnostico");
    }

    [Fact]
    public void Se_ajustan_por_configuracion_y_la_lista_de_productores_nunca_queda_vacia()
    {
        var o = OpcionesConsumidoresDiagnostico.Desde(Config(new()
        {
            ["Consultas:Diagnostico:Lote:Concurrencia"] = "2",
            ["Consultas:Diagnostico:Lote:Prefetch"] = "4",
            ["Consultas:Diagnostico:Express:Concurrencia"] = "1",
            ["Consultas:Diagnostico:Express:Prefetch"] = "1",
            ["Consultas:Diagnostico:TiempoMaximoSegundos"] = "30",
            ["Consultas:Diagnostico:ProductoresPermitidos:0"] = "",
        }));

        (o.Lote, o.Express, o.TiempoMaximo).Should().Be((new ColaConsumidor(2, 4), new ColaConsumidor(1, 1), TimeSpan.FromSeconds(30)));
        o.ProductoresPermitidos.Should().Equal("diagnostico");
    }

    [Theory]
    [InlineData("Consultas:Diagnostico:Lote:Concurrencia", "0")]
    [InlineData("Consultas:Diagnostico:Express:Prefetch", "2")]
    [InlineData("Consultas:Diagnostico:Lote:Prefetch", "70000")]
    [InlineData("Consultas:Diagnostico:TiempoMaximoSegundos", "0")]
    public void Un_valor_invalido_impide_arrancar_y_dice_la_variable(string clave, string valor) =>
        FluentActions.Invoking(() => OpcionesConsumidoresDiagnostico.Desde(Config(new() { [clave] = valor })))
            .Should().Throw<InvalidOperationException>().WithMessage($"*{clave.Replace(":", "__", StringComparison.Ordinal)}*");

    [Fact]
    public void El_appsettings_del_servicio_declara_las_colas_del_diseno_y_la_simulacion_apagada()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "appsettings.json")), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
        var consultas = json.RootElement.GetProperty("Consultas");
        var diagnostico = consultas.GetProperty("Diagnostico");

        (Numero(diagnostico, "Lote", "Concurrencia"), Numero(diagnostico, "Lote", "Prefetch")).Should().Be((8, 16));
        (Numero(diagnostico, "Express", "Concurrencia"), Numero(diagnostico, "Express", "Prefetch")).Should().Be((4, 8));
        diagnostico.GetProperty("ProductoresPermitidos").EnumerateArray().Select(p => p.GetString()).Should().Equal("diagnostico");
        consultas.GetProperty("Simulacion").GetProperty("Habilitada").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public void El_servicio_registra_un_consumidor_por_cola_con_su_tipo_de_efecto()
    {
        using var app = Program.Build([], b => Base(b));

        var consumidores = app.Services.GetServices<IHostedService>().Select(s => s.GetType())
            .Where(t => t.IsGenericType && t.Name.StartsWith("PlatformConsumer", StringComparison.Ordinal)).ToList();

        consumidores.Select(t => t.GetGenericArguments()[1]).Should().BeEquivalentTo([typeof(ConsumidorTrabajosLote), typeof(ConsumidorTrabajosExpress)]);
        consumidores.Should().OnlyContain(t => t.GetGenericArguments()[2] == typeof(TrabajoConsultaDiagnostico));
    }

    // ADR-0072 R-2 (Habeas Data): consultas.outbox lleva documentos de personas; las filas publicadas se purgan a las 24 h.
    [Fact]
    public void La_outbox_purga_las_filas_publicadas_a_las_24_horas()
    {
        using var app = Program.Build([], b => Base(b));

        app.Configuration["Platform:Messaging:RetencionPublicados"].Should().NotBeNull("appsettings.json enciende la retención");
        TimeSpan.Parse(app.Configuration["Platform:Messaging:RetencionPublicados"]!, System.Globalization.CultureInfo.InvariantCulture)
            .Should().Be(TimeSpan.FromHours(24));
        app.Services.GetServices<IHostedService>().Select(s => s.GetType().Name)
            .Should().ContainSingle(n => n.StartsWith("OutboxPurgeService", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("QA")]
    [InlineData("PDN")]
    public void Con_la_simulacion_habilitada_en_QA_o_PDN_el_servicio_no_arranca(string ambiente) =>
        FluentActions.Invoking(() => Program.Build([], b =>
        {
            Base(b);
            b.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [OpcionesSimulacion.ClaveHabilitada] = "true",
                [OpcionesSimulacion.ClaveAmbiente] = ambiente,
            });
        })).Should().Throw<InvalidOperationException>().WithMessage("*Consultas__Simulacion__Habilitada*");

    private static void Base(Microsoft.AspNetCore.Builder.WebApplicationBuilder b)
    {
        b.WebHost.UseTestServer();
        b.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Servicio"] = "Host=127.0.0.1;Database=sin_uso",
            ["Platform:ServiceClient:ClientSecret"] = "secreto",
            ["Platform:ServiceClient:TokenEndpoint"] = "http://core-identity/connect/token",
            ["Platform:Auth:JwksUri"] = "http://core-identity/.well-known/jwks.json",
            ["Platform:Auth:Issuers:0"] = "https://hub.prueba/",
            ["Platform:Messaging:ConnectionString"] = "amqp://flit:x@127.0.0.1:1/",
            ["ImprontaRunt:ApiKey"] = "llave-de-prueba",
            ["Verifik:BearerToken"] = "llave-verifik-de-prueba",
            [ServicioSettings.GrpcPortKey] = "5993",
        });
    }

    private static int Numero(JsonElement e, string cola, string clave) => e.GetProperty(cola).GetProperty(clave).GetInt32();

    private static IConfiguration Config(Dictionary<string, string?> valores) => new ConfigurationBuilder().AddInMemoryCollection(valores).Build();
}
