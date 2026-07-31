using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Flit.DataMigration.Tests.Api;

/// <summary>
/// Las dos cerraduras del API de migración, y que ninguna ruta pueda saltárselas.
/// <para>
/// Este endpoint puede reescribir trámites de producción y el gateway de V2 no exige JWT en
/// ningún ambiente (<c>JwtRequired</c> es <c>RequireAssertion(_ =&gt; true)</c>), así que la
/// cerradura de este host es la única que hay. Merece tests propios.
/// </para>
/// </summary>
public sealed class MigracionAutorizacionTests
{
    private const string Url = "/api/v1/migracion/registration/26350?dryRun=true";

    private static WebApplicationFactory<Program> Host(bool enabled, string apiKey) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseSetting("MigracionApi:Enabled", enabled ? "true" : "false")
                   .UseSetting("MigracionApi:ApiKey", apiKey));

    [Fact]
    public async Task Post_SinCabecera_Devuelve401()
    {
        using var factory = Host(enabled: true, TestEnvironment.LlaveValida);
        using var client = factory.CreateClient();

        var response = await client.PostAsync(Url, null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.Should().Contain("migracion.llave_invalida");
    }

    [Fact]
    public async Task Post_ConLlaveIncorrecta_Devuelve401()
    {
        using var factory = Host(enabled: true, TestEnvironment.LlaveValida);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Migration-Key", "no-es-la-llave");

        var response = await client.PostAsync(Url, null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// La comparación es sobre el SHA-256, no sobre la cadena: una llave de otra longitud no debe
    /// reventar ni responder más rápido, que es como se filtra el tamaño del secreto.
    /// </summary>
    [Theory]
    [InlineData("x")]
    [InlineData("")]
    [InlineData("llave-de-prueba-suficientemente-larga-y-todavia-mas-larga")]
    public async Task Post_ConLlaveDeOtraLongitud_Devuelve401(string llave)
    {
        using var factory = Host(enabled: true, TestEnvironment.LlaveValida);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Migration-Key", llave);

        var response = await client.PostAsync(Url, null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Fail-closed: sin llave configurada el host levanta, pero NINGUNA llave vale. Lo contrario
    /// —abrir la puerta cuando falta la configuración— es exactamente el fallo que no queremos.
    /// </summary>
    [Fact]
    public async Task Post_SinLlaveConfigurada_NoValidaNinguna()
    {
        using var factory = Host(enabled: true, apiKey: "");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Migration-Key", "cualquier-cosa");

        var response = await client.PostAsync(Url, null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>404 y no 403: un 403 confirmaría que el endpoint existe en este ambiente.</summary>
    [Fact]
    public async Task Post_ConApiApagada_Devuelve404()
    {
        using var factory = Host(enabled: false, TestEnvironment.LlaveValida);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Migration-Key", TestEnvironment.LlaveValida);

        var response = await client.PostAsync(Url, null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Health_NoExigeLlave()
    {
        using var factory = Host(enabled: true, TestEnvironment.LlaveValida);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ------------------------------------------- validación, ya pasada la cerradura

    [Fact]
    public async Task Post_TramiteDesconocido_Devuelve400()
    {
        using var factory = Host(enabled: true, TestEnvironment.LlaveValida);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Migration-Key", TestEnvironment.LlaveValida);

        var response = await client.PostAsync(
            "/api/v1/migracion/traspaso/26350", null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.Should().Contain("migracion.tramite_desconocido");
    }

    [Fact]
    public async Task Post_InstanciaDesconocida_Devuelve400()
    {
        using var factory = Host(enabled: true, TestEnvironment.LlaveValida);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Migration-Key", TestEnvironment.LlaveValida);

        var response = await client.PostAsync(
            "/api/v1/migracion/registration/26350?instancias=fotos", null,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.Should().Contain("migracion.instancia_desconocida");
    }

    /// <summary>
    /// `force` no es un parámetro: pasarlo no debe habilitar nada. Si algún día alguien lo
    /// añadiera sin pensarlo, este test se cae.
    /// </summary>
    [Fact]
    public async Task Post_ConForceEnElQueryString_NoLoAcepta()
    {
        using var factory = Host(enabled: true, TestEnvironment.LlaveValida);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Migration-Key", TestEnvironment.LlaveValida);

        var response = await client.PostAsync(
            "/api/v1/migracion/bogus/26350?force=true", null, TestContext.Current.CancellationToken);

        // Llega a la validación de trámite, no a un 200: `force` se ignora por completo.
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
