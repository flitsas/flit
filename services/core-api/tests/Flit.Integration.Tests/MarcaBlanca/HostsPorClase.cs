using Flit.Integration.Tests.Postgres;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Flit.Integration.Tests.MarcaBlanca;

/// <summary>
/// Hosts HTTP (<see cref="MarcaBlancaApiFactory"/>) compartidos por TODA una clase de pruebas, uno por configuración.
///
/// <para>xUnit crea una instancia de la clase por prueba: si la prueba (o un helper que llama en cada petición) hace
/// <c>new MarcaBlancaApiFactory(Fixture)</c>, cada prueba arranca la API completa (~1–3 s; más con el runner cargado).
/// Con este fixture el host de cada configuración se arranca la primera vez que se pide y se libera al terminar la
/// clase. Ver <c>HostPorPruebaArchitectureTests</c> en Flit.Admin.Tests.</para>
///
/// <para>Uso: <c>IClassFixture&lt;HostsPorClase&gt;</c>, el fixture como parámetro del constructor y
/// <c>hosts.Obtener(Fixture)</c> o <c>hosts.Obtener(Fixture, new Dictionary&lt;string, string?&gt; { ["Clave"] = "valor" })</c>.
/// La base sigue reiniciándose antes de cada prueba (<see cref="PostgresTestBase"/>); lo que se comparte es el proceso
/// del host, así que solo sirve para clases que no dependan de cachés en memoria del host entre pruebas (branding
/// público, tema de correo, dominios): esas siguen con su host por prueba (<see cref="MarcaBlancaHttpTestBase"/>).</para>
///
/// <para>Las pruebas de una misma clase corren en serie (y la colección Postgres entera también), así que no hace falta
/// sincronizar el diccionario.</para>
/// </summary>
public sealed class HostsPorClase : IAsyncDisposable
{
    private readonly Dictionary<string, (MarcaBlancaApiFactory Base, WebApplicationFactory<Program> Host)> _hosts =
        new(StringComparer.Ordinal);

    /// <summary>
    /// El host con esa <paramref name="configuracion"/> (claves de <c>IConfiguration</c>); lo arranca la primera vez.
    /// Con <paramref name="servicios"/> (reemplazos de DI) hay que dar una <paramref name="clave"/>: el host se arma con
    /// los servicios de la PRIMERA llamada con esa clave.
    /// </summary>
    public WebApplicationFactory<Program> Obtener(
        PostgresDatabaseFixture fixture,
        IReadOnlyDictionary<string, string?>? configuracion = null,
        Action<IServiceCollection>? servicios = null,
        string clave = "")
    {
        ArgumentNullException.ThrowIfNull(fixture);
        if (servicios is not null && clave.Length == 0)
            throw new ArgumentException("Con servicios propios el host necesita una clave.", nameof(clave));

        var id = clave + "|" + string.Join(";", (configuracion ?? new Dictionary<string, string?>())
            .OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}={kv.Value}"));
        if (_hosts.TryGetValue(id, out var host))
            return host.Host;

        var basica = new MarcaBlancaApiFactory(fixture);
        WebApplicationFactory<Program> configurada = basica;
        if (configuracion is not null || servicios is not null)
        {
            configurada = basica.WithWebHostBuilder(b =>
            {
                if (configuracion is not null)
                    b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(configuracion));
                if (servicios is not null)
                    b.ConfigureTestServices(servicios);
            });
        }

        _hosts[id] = (basica, configurada);
        return configurada;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var (basica, host) in _hosts.Values)
        {
            if (!ReferenceEquals(basica, host))
                await host.DisposeAsync();
            await basica.DisposeAsync();
        }

        _hosts.Clear();
    }
}
