using System.Net;
using Flit.PersonaNatural.Api.Configuracion;
using Flit.PersonaNatural.Api.Cotizaciones;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.PersonaNatural.Tests;

/// <summary>Fachada de configuración de prueba: devuelve lo que se le asigne y cuenta las llamadas.</summary>
internal sealed class FachadaConfiguracionFalsa : IFachadaConfiguracion
{
    public int Llamadas;
    public int LlamadasImagen;
    public string? ErrorAlPedir;
    public Dictionary<string, ImagenBannerFachada> Imagenes { get; } = [];

    public ConfiguracionFachada Configuracion { get; set; } = new(
        "3",
        new DateTimeOffset(2026, 10, 27, 10, 0, 0, TimeSpan.Zero),
        [new PreguntaDto("p1", "¿Cuánto tarda?", "Depende del organismo.", 1), new PreguntaDto("p2", "¿Es seguro?", "Sí.", 2)],
        [new BannerFachada(ConfiguracionTests.Banner, "Promoción", "Promoción de traspasos", "https://flit.test/promo", 1, "abc123")],
        [
            new EnlaceDto("POLITICA_DATOS", "https://flit.test/politica.pdf", true),
            new EnlaceDto("TERMINOS", null, false),
            new EnlaceDto("CONTACTO_VENTAS", "mailto:ventas@flit.test", true),
            new EnlaceDto("VIDEO", null, false),
        ],
        new SoporteDto("soporte@flit.test", "+57 601 000 0000", "L-V 8:00 a 17:00"),
        30);

    public Task<ConfiguracionFachada> ObtenerAsync(CancellationToken ct)
    {
        Interlocked.Increment(ref Llamadas);
        return ErrorAlPedir is { } e ? throw new FachadaException(e, "error de prueba", 503) : Task.FromResult(Configuracion);
    }

    public Task<ImagenBannerFachada?> ObtenerImagenAsync(string bannerId, string? ifNoneMatch, CancellationToken ct)
    {
        Interlocked.Increment(ref LlamadasImagen);
        if (ErrorAlPedir is { } e)
            throw new FachadaException(e, "error de prueba", 503);
        if (!Imagenes.TryGetValue(bannerId, out var imagen))
            return Task.FromResult<ImagenBannerFachada?>(null);
        return Task.FromResult<ImagenBannerFachada?>(
            string.Equals(ifNoneMatch, imagen.Sha256, StringComparison.OrdinalIgnoreCase) ? imagen with { NoModificado = true, Contenido = [] } : imagen);
    }
}

/// <summary>
/// HU #14073 (Feature J, Épica #14021) — el BFF entrega el contenido público del portal desde la fachada de core-api (falsa
/// aquí), con caché de 60 s, sin sesión ni captcha, sin datos bancarios ni parámetros de protección. Base efímera propia; sin
/// motor se omite en local y falla en CI.
/// </summary>
public sealed class ConfiguracionTests : IAsyncLifetime
{
    internal const string Banner = "0199b7e0-0000-7000-8000-0000000000b1";
    private const string Ruta = "/api/v1/persona-natural/configuracion";

    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];

    private readonly FachadaConfiguracionFalsa _fachada = new();
    private PortalHost _host = null!;
    private bool _iniciado;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (_iniciado)
            await _host.DisposeAsync();
    }

    private async Task IniciarAsync(params (string Clave, string Valor)[] ajustes)
    {
        _host = await PortalHost.IniciarAsync("pncfg", (s, _) => s.AddSingleton<IFachadaConfiguracion>(_fachada), ajustes);
        _iniciado = true;
        _fachada.Imagenes[Banner] = new ImagenBannerFachada(false, "abc123", "image/png", Png);
    }

    [Fact]
    public async Task ElContenidoPublicadoSale_SinSesionNiCaptcha_ConLaRutaDeLaImagenYSinDatosBancarios()
    {
        await IniciarAsync();

        var r = await _host.EnviarAsync(HttpMethod.Get, Ruta);

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        r.Headers.CacheControl!.MaxAge.Should().Be(TimeSpan.FromSeconds(60));
        var crudo = await r.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var json = System.Text.Json.JsonDocument.Parse(crudo).RootElement;
        json.GetProperty("version").GetString().Should().Be("3");
        json.GetProperty("preguntas").EnumerateArray().Select(p => p.GetProperty("pregunta").GetString()).Should().Equal("¿Cuánto tarda?", "¿Es seguro?");
        var banner = json.GetProperty("banners")[0];
        banner.GetProperty("textoAlternativo").GetString().Should().Be("Promoción de traspasos");
        banner.GetProperty("imagenUrl").GetString().Should().Be($"{Ruta}/banners/{Banner}/imagen?v=abc123");
        json.GetProperty("enlaces").EnumerateArray().Select(e => e.GetProperty("codigo").GetString())
            .Should().Equal("POLITICA_DATOS", "TERMINOS", "CONTACTO_VENTAS", "VIDEO");
        var terminos = json.GetProperty("enlaces")[1];
        (terminos.GetProperty("url").ValueKind, terminos.GetProperty("conDocumento").GetBoolean()).Should().Be((System.Text.Json.JsonValueKind.Null, false), "sin documento oficial el portal conserva su aviso");
        json.GetProperty("soporte").GetProperty("correo").GetString().Should().Be("soporte@flit.test");
        json.GetProperty("borradorVigenciaDias").GetInt32().Should().Be(30);
        crudo.Should().NotContainEquivalentOf("cuenta").And.NotContainEquivalentOf("banco").And.NotContainEquivalentOf("captcha").And.NotContainEquivalentOf("tope");
    }

    [Fact]
    public async Task NadaPublicado_PasaLaVersionCero_ParaQueElPortalConserveSuContenidoActual()
    {
        await IniciarAsync();
        _fachada.Configuracion = new ConfiguracionFachada(
            "0", null, [], [], [new EnlaceDto("POLITICA_DATOS", null, false)], new SoporteDto(null, null, null), null);

        var json = (await PortalHost.JsonAsync(await _host.EnviarAsync(HttpMethod.Get, Ruta))).RootElement;

        json.GetProperty("version").GetString().Should().Be("0");
        json.GetProperty("preguntas").GetArrayLength().Should().Be(0);
        json.GetProperty("banners").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task LaConfiguracionSeCacheaUnosSegundos_YSinCacheCadaLecturaVaAlaFachada()
    {
        await IniciarAsync();

        await _host.EnviarAsync(HttpMethod.Get, Ruta);
        await _host.EnviarAsync(HttpMethod.Get, Ruta);

        _fachada.Llamadas.Should().Be(1, "la segunda lectura sale de la caché de 60 s");
        await _host.DisposeAsync();
        _iniciado = false;
        await IniciarAsync(("Cotizaciones:CacheCatalogoSegundos", "0"));
        var antes = _fachada.Llamadas;

        await _host.EnviarAsync(HttpMethod.Get, Ruta);
        await _host.EnviarAsync(HttpMethod.Get, Ruta);

        _fachada.Llamadas.Should().Be(antes + 2);
    }

    [Fact]
    public async Task LaImagenSale_ConEtag_YConElMismoEtagResponde304SinBytes()
    {
        await IniciarAsync();

        var r = await _host.EnviarAsync(HttpMethod.Get, $"{Ruta}/banners/{Banner}/imagen");
        var igual = await _host.EnviarAsync(HttpMethod.Get, $"{Ruta}/banners/{Banner}/imagen", cabeceras: ("If-None-Match", "\"abc123\""));

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        r.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
        r.Headers.ETag!.Tag.Should().Be("\"abc123\"");
        (await r.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken)).Should().Equal(Png);
        igual.StatusCode.Should().Be(HttpStatusCode.NotModified);
    }

    [Fact]
    public async Task UnBannerInexistenteOVencidoDa404_YUnIdentificadorInvalidoNiLlegaALaFachada()
    {
        await IniciarAsync();

        var desconocido = await _host.EnviarAsync(HttpMethod.Get, $"{Ruta}/banners/0199b7e0-0000-7000-8000-0000000000ff/imagen");
        var invalido = await _host.EnviarAsync(HttpMethod.Get, $"{Ruta}/banners/no-es-un-guid/imagen");

        desconocido.StatusCode.Should().Be(HttpStatusCode.NotFound);
        invalido.StatusCode.Should().Be(HttpStatusCode.NotFound);
        _fachada.LlamadasImagen.Should().Be(1, "el identificador inválido se descarta antes de preguntar a core-api");
    }

    [Fact]
    public async Task SiCoreApiFalla_ElPortalRecibe503ConCodigoEstable_SinDetalleInterno()
    {
        await IniciarAsync();
        _fachada.ErrorAlPedir = "servicio_no_disponible";

        var r = await _host.EnviarAsync(HttpMethod.Get, Ruta);
        var imagen = await _host.EnviarAsync(HttpMethod.Get, $"{Ruta}/banners/{Banner}/imagen");

        r.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await PortalHost.CodigoAsync(r)).Should().Be("servicio_no_disponible");
        imagen.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }
}
