using Microsoft.AspNetCore.Mvc.Testing;

namespace Flit.Admin.Tests;

/// <summary>
/// Un host por CLASE de pruebas, no uno por prueba.
///
/// <para>xUnit crea una instancia de la clase por cada prueba. Si el constructor hace
/// <c>factory.WithWebHostBuilder(...)</c>, cada prueba arranca un host completo (~5 s de CPU, 30+ s con el runner
/// cargado) y nunca lo libera: en CI eso eran ~2.000 s de CPU de Flit.Admin.Tests en 7 clases.</para>
///
/// <para>Uso: una clase anidada que deriva de esta y describe la configuración en <see cref="Configurar"/>, y
/// <c>IClassFixture&lt;MiClase.Host&gt;</c> en lugar de <c>IClassFixture&lt;WebApplicationFactory&lt;Program&gt;&gt;</c>.
/// El host se arranca la primera vez que se pide <see cref="Factory"/> y se libera al terminar la clase.</para>
///
/// <para>Las pruebas de una misma clase corren en serie (xUnit solo paraleliza entre clases), así que un doble de
/// prueba guardado en el fixture se puede reiniciar en el constructor de la clase sin carreras. Lo que no se debe
/// compartir es estado que la prueba no pueda reiniciar (un reloj falso que avanza, un singleton con caché que la
/// prueba invalida a mano): esas clases siguen con su propio host.</para>
/// </summary>
public abstract class HostPorClase : IDisposable
{
    private readonly WebApplicationFactory<Program> _base = new();
    private readonly Lazy<WebApplicationFactory<Program>> _factory;

    protected HostPorClase() => _factory = new(() => Configurar(_base));

    /// <summary>La fábrica configurada (un solo host para todas las pruebas de la clase).</summary>
    public WebApplicationFactory<Program> Factory => _factory.Value;

    /// <summary>La fábrica sin configurar, para la prueba que necesita el host por defecto (también uno por clase).</summary>
    public WebApplicationFactory<Program> Base => _base;

    /// <summary>Lo que antes hacía el constructor de la clase con <c>WithWebHostBuilder</c>.</summary>
    protected abstract WebApplicationFactory<Program> Configurar(WebApplicationFactory<Program> factory);

    public void Dispose()
    {
        if (_factory.IsValueCreated)
            _factory.Value.Dispose();
        _base.Dispose();
        GC.SuppressFinalize(this);
    }
}
