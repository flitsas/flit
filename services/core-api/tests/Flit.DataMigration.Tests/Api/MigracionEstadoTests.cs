using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Flit.DataMigration.Tests.Api;

/// <summary>
/// La consulta de estado: la ruta de solo lectura que alimenta la consola web.
/// <para>
/// Como el resto de tests de este host, corre SIN PostgreSQL: verifica la ruta, la cerradura y la
/// validación de entrada, que es todo lo que ocurre antes de tocar la base. Lo que devuelve la
/// libreta se valida a mano contra una copia, igual que el resto del migrador.
/// </para>
/// </summary>
public sealed class MigracionEstadoTests
{
    private static WebApplicationFactory<Program> Host(bool enabled = true) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseSetting("MigracionApi:Enabled", enabled ? "true" : "false")
                   .UseSetting("MigracionApi:ApiKey", TestEnvironment.LlaveValida));

    private static HttpClient Autenticado(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Migration-Key", TestEnvironment.LlaveValida);
        return client;
    }

    /// <summary>
    /// La cerradura está en el GRUPO, así que la ruta nueva la hereda sin haber tenido que
    /// acordarse de nada. Este test es el que lo demuestra.
    /// </summary>
    [Fact]
    public async Task Estado_SinCabecera_Devuelve401()
    {
        using var factory = Host();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            "/api/v1/migracion/estado/registration?ids=1,2,3", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.Should().Contain("migracion.llave_invalida");
    }

    [Fact]
    public async Task Estado_ConApiApagada_Devuelve404()
    {
        using var factory = Host(enabled: false);
        using var client = Autenticado(factory);

        var response = await client.GetAsync(
            "/api/v1/migracion/estado/registration?ids=1", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Estado_TramiteDesconocido_Devuelve400()
    {
        using var factory = Host();
        using var client = Autenticado(factory);

        var response = await client.GetAsync(
            "/api/v1/migracion/estado/traspaso?ids=1", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.Should().Contain("migracion.tramite_desconocido");
    }

    [Fact]
    public async Task Estado_SinIds_Devuelve400()
    {
        using var factory = Host();
        using var client = Autenticado(factory);

        var response = await client.GetAsync(
            "/api/v1/migracion/estado/registration", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.Should().Contain("migracion.sin_ids");
    }

    /// <summary>
    /// Un id que no es un número tiene que salir por la puerta del 400 y no reventar dentro: un CSV
    /// con una celda de texto es el error más probable de esta consola.
    /// </summary>
    [Theory]
    [InlineData("1,abc,3")]
    [InlineData("26350.5")]
    [InlineData("'; DROP TABLE migration.migration_map; --")]
    public async Task Estado_ConIdNoNumerico_Devuelve400(string ids)
    {
        using var factory = Host();
        using var client = Autenticado(factory);

        var response = await client.GetAsync(
            $"/api/v1/migracion/estado/registration?ids={Uri.EscapeDataString(ids)}",
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.Should().Contain("migracion.id_invalido");
    }

    [Fact]
    public async Task Estado_ConDemasiadosIds_Devuelve400()
    {
        using var factory = Host();
        using var client = Autenticado(factory);

        var ids = string.Join(',', Enumerable.Range(1, 201));

        var response = await client.GetAsync(
            $"/api/v1/migracion/estado/registration?ids={ids}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.Should().Contain("migracion.demasiados_ids");
    }

    /// <summary>
    /// Doscientos ids repetidos son doscientas filas de un CSV mal armado, no un abuso: se
    /// deduplican y pasan. El tope se mide sobre ids DISTINTOS.
    /// </summary>
    [Fact]
    public async Task Estado_ConIdsRepetidos_NoCuentanContraElTope()
    {
        using var factory = Host();
        using var client = Autenticado(factory);

        var ids = string.Join(',', Enumerable.Repeat("26350", 300));

        var response = await client.GetAsync(
            $"/api/v1/migracion/estado/registration?ids={ids}", TestContext.Current.CancellationToken);

        // Pasa la validación y muere al abrir la conexión: sin Postgres no puede terminar, pero lo
        // que se afirma aquí es que NO se fue por el 400 del tope.
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.Should().NotContain("migracion.demasiados_ids");
    }
}
