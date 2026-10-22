using System.Net;
using Flit.Diagnostico.Api;
using Flit.Diagnostico.Tests.Api;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Diagnostico.Tests.Aislamiento;

/// <summary>
/// HU #13722 (F00.1-H2), escenarios 2 y 3 — prueba de fuga entre empresas por endpoint de lectura. Cada GET del servicio
/// tiene un escenario en <see cref="EscenariosDeFuga"/>: se siembran datos de A y de B en la misma base y, con el token de
/// A, ninguna respuesta contiene un identificador de B. Un GET sin escenario hace fallar la suite y queda nombrado.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/Flit.Diagnostico.slnx --filter FullyQualifiedName~Aislamiento
/// </code>
/// </example>
public sealed class FugaEntreEmpresasTests
{
    private const string RutaFantasma = Program.Prefijo + "/fantasma-sin-escenario";

    public static TheoryData<string> Escenarios => new(EscenariosDeFuga.Todos.Select(e => e.Ruta));

    // Escenario 2 — prueba de fuga por endpoint de lectura.
    [Theory]
    [MemberData(nameof(Escenarios))]
    public async Task Ningun_endpoint_de_lectura_devuelve_datos_de_otra_empresa(string ruta)
    {
        var escenario = EscenariosDeFuga.Buscar(ruta);
        await using var api = await ApiDePrueba.ConBaseAsync(EscenariosDeFuga.MapearSondas);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresas = DosEmpresas.En(api);

        var deA = await escenario.Sembrar(empresas.A, ct);
        var deB = await escenario.Sembrar(empresas.B, ct);

        var propia = await empresas.A.Cliente().GetAsync(deA.Url, ct);
        var cuerpoPropio = await escenario.TextoAsync(propia, ct);
        propia.StatusCode.Should().Be(HttpStatusCode.OK, $"A lee su propio {ruta}");
        cuerpoPropio.Should().ContainAll(deA.Identificadores, "sin los datos propios la prueba de fuga no prueba nada");
        cuerpoPropio.Should().NotContainAny(deB.QueNoCruzan, $"{ruta} con el token de A no devuelve nada de B");

        var ajena = await empresas.A.Cliente().GetAsync(deB.Url, ct);
        (await escenario.TextoAsync(ajena, ct)).Should().NotContainAny(deB.QueNoCruzan,
            $"{deB.Url} con el token de A no devuelve nada de B");
    }

    // Escenario 3 — todo GET registrado del servicio tiene escenario de fuga.
    [Fact]
    public async Task Todo_endpoint_GET_tiene_escenario_de_fuga()
    {
        await using var api = await ApiDePrueba.SinBaseAsync();

        EscenariosDeFuga.EndpointsSinEscenario(Fuentes(api.App)).Should().BeEmpty("cada GET suma su línea en EscenariosDeFuga.Todos");
    }

    // Escenario 3 — un GET nuevo sin escenario hace fallar la suite y queda nombrado.
    [Fact]
    public async Task Endpoint_GET_sin_escenario_de_fuga_hace_fallar_la_prueba()
    {
        await using var api = await ApiDePrueba.SinBaseAsync(app => app.MapGet(RutaFantasma, () => Results.Ok()).RequireAuthorization());

        var faltantes = EscenariosDeFuga.EndpointsSinEscenario(Fuentes(api.App));

        faltantes.Should().Equal(RutaFantasma);
        var verificacion = () => faltantes.Should().BeEmpty("endpoint sin escenario de fuga");
        verificacion.Should().Throw<Exception>().WithMessage($"*{RutaFantasma}*");
    }

    // Las rutas de salud y las de backoffice (lectura entre empresas auditada, F00.1-H5) no llevan escenario.
    [Fact]
    public async Task Salud_y_backoffice_no_exigen_escenario()
    {
        await using var api = await ApiDePrueba.SinBaseAsync(app =>
        {
            app.MapGet("/health/extra", () => Results.Ok());
            app.MapGet(Program.Prefijo + "/backoffice/empresas/{empresa}/resumen", () => Results.Ok());
        });

        EscenariosDeFuga.EndpointsSinEscenario(Fuentes(api.App)).Should().BeEmpty();
    }

    private static IEnumerable<EndpointDataSource> Fuentes(WebApplication app) => app.Services.GetServices<EndpointDataSource>();
}
