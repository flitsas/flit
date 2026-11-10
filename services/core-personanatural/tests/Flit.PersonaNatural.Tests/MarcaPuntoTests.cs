using System.Net;
using System.Text.Json;
using System.Threading.RateLimiting;
using Flit.PersonaNatural.Api.Abuso;
using Flit.PersonaNatural.Api.Cotizaciones;
using Flit.PersonaNatural.Api.Punto;
using Flit.PersonaNatural.Api.Sesiones;
using Flit.Tramites.Grpc.V1;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.PersonaNatural.Tests;

/// <summary>
/// HU #14378 (Feature #14374, Épica #14297) — la marca publicada del organismo llega al portal: siempre 200 con
/// <c>origen: "flit"</c> ante código inválido, organismo sin marca o falla; sin caché por tiempo; imágenes publicadas con URL
/// inmutable. Host de prueba sin Postgres y con la fachada de core-api falsa.
/// </summary>
public sealed class MarcaPuntoTests
{
    private static readonly Guid Logo = Guid.Parse("0199b7e0-0000-7000-8000-0000000000a1");
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static MarcaPuntoRespuesta DelOrganismo(int version = 3) => new(
        MarcaPuntoRespuesta.OrigenOrganismo, "Tránsito de Prueba", new ColoresMarcaPunto("#0B3D91", "#0F5132", "#FFFFFF"),
        $"{MarcaPuntoEndpoints.RutaImagenes}{Logo}", null, version);

    [Fact]
    public async Task AC1_ConMarcaPublicadaRecibeNombreColoresYRutasDeImagenSinDatosInternos()
    {
        await using var host = new Anfitrion(new FachadaMarca { Marca = DelOrganismo() });

        var r = await host.GetAsync("/punto/marca/AB1");
        var json = JsonDocument.Parse(await r.Content.ReadAsStringAsync(Ct)).RootElement;

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        json.GetProperty("origen").GetString().Should().Be("organismo");
        json.GetProperty("nombre").GetString().Should().Be("Tránsito de Prueba");
        json.GetProperty("colores").GetProperty("primario").GetString().Should().Be("#0B3D91");
        json.GetProperty("colores").GetProperty("secundario").GetString().Should().Be("#0F5132");
        json.GetProperty("colores").GetProperty("sobrePrimario").GetString().Should().Be("#FFFFFF");
        json.GetProperty("logoUrl").GetString().Should().Be($"/api/v1/persona-natural/marca/imagenes/{Logo}");
        json.GetProperty("bannerUrl").ValueKind.Should().Be(JsonValueKind.Null);
        json.GetProperty("version").GetInt32().Should().Be(3);
        json.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("origen", "nombre", "colores", "logoUrl", "bannerUrl", "version", "carrusel", "banners");
        json.GetProperty("carrusel").ValueKind.Should().Be(JsonValueKind.Array);
        json.GetProperty("carrusel").GetArrayLength().Should().Be(0, "sin carrusel publicado el arreglo va vacío, no nulo");
    }

    [Fact]
    public async Task AC3_NuncaSeGuardaEnCachePorTiempo_LaSiguienteLecturaVaALaFachada()
    {
        var fachada = new FachadaMarca { Marca = DelOrganismo(1) };
        await using var host = new Anfitrion(fachada);

        var primera = await host.GetAsync("/punto/marca/AB1");
        fachada.Marca = DelOrganismo(2);
        var segunda = await host.GetAsync("/punto/marca/AB1");
        fachada.Marca = MarcaPuntoRespuesta.Flit;
        var tercera = await host.GetAsync("/punto/marca/AB1");

        primera.Headers.CacheControl!.NoStore.Should().BeTrue();
        (await Version(segunda)).Should().Be(2, "lo recién publicado se ve de inmediato");
        (await Origen(tercera)).Should().Be("flit", "lo recién retirado también");
        fachada.Llamadas.Should().Be(3, "sin caché en el BFF");
    }

    [Theory]
    [InlineData("DESCONOCIDO")]
    [InlineData("%20")]
    [InlineData("CODIGO-DEMASIADO-LARGO-PARA-UN-CODIGO")]
    public async Task AC2_UnCodigoInvalidoORecibeLaMarcaFlitConCodigo200(string codigo)
    {
        await using var host = new Anfitrion(new FachadaMarca { Marca = MarcaPuntoRespuesta.Flit });

        var r = await host.GetAsync($"/punto/marca/{codigo}");

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Origen(r)).Should().Be("flit");
    }

    [Fact]
    public async Task AC2_AnteUnaFallaDeLaFachadaRecibeLaMarcaFlitConCodigo200YSinDetalle()
    {
        await using var host = new Anfitrion(new FachadaMarca { Falla = new FachadaException("servicio_no_disponible", "detalle interno secreto", 503) });

        var r = await host.GetAsync("/punto/marca/AB1");
        var texto = await r.Content.ReadAsStringAsync(Ct);

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        texto.Should().NotContain("secreto");
        JsonDocument.Parse(texto).RootElement.GetProperty("origen").GetString().Should().Be("flit");
        JsonDocument.Parse(texto).RootElement.GetProperty("colores").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task LaImagenPublicadaSeSirveConUrlInmutableYEtag_YElBorradorOLaInexistenteDan404()
    {
        var fachada = new FachadaMarca { Imagen = new ImagenMarcaPunto(false, "abc123", "image/png", [1, 2, 3]) };
        await using var host = new Anfitrion(fachada);

        var r = await host.GetAsync($"/marca/imagenes/{Logo}");

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        r.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
        r.Headers.CacheControl!.Public.Should().BeTrue();
        r.Headers.CacheControl.Extensions.Select(e => e.Name).Should().Contain("immutable");
        r.Headers.CacheControl.MaxAge.Should().Be(TimeSpan.FromSeconds(31536000));
        r.Headers.ETag!.Tag.Should().Be("\"abc123\"");
        (await r.Content.ReadAsByteArrayAsync(Ct)).Should().Equal(1, 2, 3);

        fachada.Imagen = null;
        (await host.GetAsync($"/marca/imagenes/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await host.GetAsync("/marca/imagenes/no-es-un-id")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task LaImagenConElMismoEtagRespondeNoModificadoSinBytes()
    {
        await using var host = new Anfitrion(new FachadaMarca { Imagen = new ImagenMarcaPunto(true, "abc123", "image/png", null) });

        var r = await host.GetAsync($"/marca/imagenes/{Logo}", "\"abc123\"");

        r.StatusCode.Should().Be(HttpStatusCode.NotModified);
    }

    private static async Task<int> Version(HttpResponseMessage r) =>
        JsonDocument.Parse(await r.Content.ReadAsStringAsync(Ct)).RootElement.GetProperty("version").GetInt32();

    private static async Task<string?> Origen(HttpResponseMessage r) =>
        JsonDocument.Parse(await r.Content.ReadAsStringAsync(Ct)).RootElement.GetProperty("origen").GetString();

    // HU #14401 — marca de la web de persona natural.

    private static MarcaPuntoRespuesta DeLaWeb(int version = 1) => new(
        MarcaPuntoRespuesta.OrigenWeb, "Portal FLIT Persona", new ColoresMarcaPunto("#0B3D91", "#0F5132", "#FFFFFF"),
        $"{MarcaPuntoEndpoints.RutaImagenes}{Logo}", null, version);

    [Fact]
    public async Task Web_ConMarcaPublicadaRecibeLaMismaFormaQueElPuntoSinDatosInternos()
    {
        await using var host = new Anfitrion(new FachadaMarca { MarcaWeb = DeLaWeb(2) });

        var r = await host.GetAsync("/marca/web");
        var json = JsonDocument.Parse(await r.Content.ReadAsStringAsync(Ct)).RootElement;

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        r.Headers.CacheControl!.NoStore.Should().BeTrue();
        json.GetProperty("origen").GetString().Should().Be("web");
        json.GetProperty("nombre").GetString().Should().Be("Portal FLIT Persona");
        json.GetProperty("colores").GetProperty("sobrePrimario").GetString().Should().Be("#FFFFFF");
        json.GetProperty("logoUrl").GetString().Should().Be($"/api/v1/persona-natural/marca/imagenes/{Logo}");
        json.GetProperty("bannerUrl").ValueKind.Should().Be(JsonValueKind.Null);
        json.GetProperty("version").GetInt32().Should().Be(2);
        json.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("origen", "nombre", "colores", "logoUrl", "bannerUrl", "version", "carrusel", "banners");
        json.GetProperty("carrusel").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Web_SinMarcaOAnteUnaFallaRecibeLaMarcaFlitConCodigo200YSinDetalle()
    {
        var fachada = new FachadaMarca();
        await using var host = new Anfitrion(fachada);

        var sinMarca = await host.GetAsync("/marca/web");
        fachada.Falla = new FachadaException("servicio_no_disponible", "detalle interno secreto", 503);
        var conFalla = await host.GetAsync("/marca/web");
        var texto = await conFalla.Content.ReadAsStringAsync(Ct);

        sinMarca.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Origen(sinMarca)).Should().Be("flit");
        conFalla.StatusCode.Should().Be(HttpStatusCode.OK);
        texto.Should().NotContain("secreto");
        JsonDocument.Parse(texto).RootElement.GetProperty("origen").GetString().Should().Be("flit");
    }

    [Fact]
    public async Task Web_NuncaSeGuardaEnCachePorTiempoYNoSeCruzaConLaMarcaDelPunto()
    {
        var fachada = new FachadaMarca { MarcaWeb = DeLaWeb(1), Marca = DelOrganismo(7) };
        await using var host = new Anfitrion(fachada);

        var primera = await host.GetAsync("/marca/web");
        fachada.MarcaWeb = DeLaWeb(2);
        var segunda = await host.GetAsync("/marca/web");
        fachada.MarcaWeb = MarcaPuntoRespuesta.Flit;
        var tercera = await host.GetAsync("/marca/web");
        var punto = await host.GetAsync("/punto/marca/AB1");

        (await Version(primera)).Should().Be(1);
        (await Version(segunda)).Should().Be(2);
        (await Origen(tercera)).Should().Be("flit", "retirar la web se ve en la siguiente lectura");
        (await Origen(punto)).Should().Be("organismo");
        (await Version(punto)).Should().Be(7, "la marca del punto no cambió");
        fachada.LlamadasWeb.Should().Be(3);
    }

    // Mejora de la Épica #14297 — carrusel de la columna lateral.

    private static readonly Guid ImagenCarrusel = Guid.Parse("0199b7e0-0000-7000-8000-0000000000c1");

    [Fact]
    public async Task Carrusel_ElPortalRecibeElArregloConUrlPublicaAltYEnlaceONulo()
    {
        var basica = DelOrganismo();
        var marca = new MarcaPuntoRespuesta(basica.Origen, basica.Nombre, basica.Colores, basica.LogoUrl, basica.BannerUrl, basica.Version)
        {
            Carrusel =
            [
                new CarruselMarcaPunto($"{MarcaPuntoEndpoints.RutaImagenes}{ImagenCarrusel}", "Primera", "https://tramites.example.test/a"),
                new CarruselMarcaPunto($"{MarcaPuntoEndpoints.RutaImagenes}{Logo}", "Segunda", null),
            ],
        };
        await using var host = new Anfitrion(new FachadaMarca { Marca = marca });

        var json = JsonDocument.Parse(await (await host.GetAsync("/punto/marca/AB1")).Content.ReadAsStringAsync(Ct)).RootElement;

        var items = json.GetProperty("carrusel").EnumerateArray().ToList();
        items.Should().HaveCount(2);
        items[0].EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("imagenUrl", "alt", "enlace");
        items[0].GetProperty("imagenUrl").GetString().Should().Be($"/api/v1/persona-natural/marca/imagenes/{ImagenCarrusel}");
        items[0].GetProperty("alt").GetString().Should().Be("Primera");
        items[0].GetProperty("enlace").GetString().Should().Be("https://tramites.example.test/a");
        items[1].GetProperty("enlace").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Carrusel_LaMarcaFlitYLaDeFalloVanConElArregloVacio()
    {
        await using var host = new Anfitrion(new FachadaMarca { Marca = MarcaPuntoRespuesta.Flit });

        var json = JsonDocument.Parse(await (await host.GetAsync("/punto/marca/AB1")).Content.ReadAsStringAsync(Ct)).RootElement;

        json.GetProperty("carrusel").ValueKind.Should().Be(JsonValueKind.Array);
        json.GetProperty("carrusel").GetArrayLength().Should().Be(0);
    }

    private static ObtenerMarcaPuntoResponse RespuestaGrpc(string origen, params CarruselItem[] items)
    {
        var r = new ObtenerMarcaPuntoResponse
        {
            Origen = origen, Nombre = "Tránsito", ColorPrimario = "#0B3D91", ColorSecundario = "#0F5132", ColorSobrePrimario = "#FFFFFF", Version = 1,
        };
        r.Carrusel.AddRange(items);
        return r;
    }

    private static CarruselItem Item(string id, string alt, string enlace = "") => new() { ImagenId = id, Alt = alt, Enlace = enlace };

    [Fact]
    public void Carrusel_ElBffDescartaItemsConIdInvalidoAltVacioOEnlaceQueNoEsHttpOHttps()
    {
        var bueno = ImagenCarrusel.ToString();
        var r = RespuestaGrpc(
            "organismo",
            Item(bueno, "Con enlace", "https://example.test/a"),
            Item("no-es-un-id", "Id inválido"),
            Item(Guid.NewGuid().ToString(), "   "),
            Item(Guid.NewGuid().ToString(), "Enlace js", "javascript:alert(1)"),
            Item(Guid.NewGuid().ToString(), "Enlace ftp", "ftp://example.test/a"),
            Item(Guid.NewGuid().ToString(), "Enlace relativo", "/interno"),
            Item(Guid.NewGuid().ToString(), "Enlace largo", "https://example.test/" + new string('a', 490)),
            Item(Guid.NewGuid().ToString(), "Sin enlace"));

        var marca = MarcaPuntoGrpc.ARespuesta(r, MarcaPuntoRespuesta.OrigenOrganismo);

        marca.Carrusel.Select(i => i.Alt).Should().Equal("Con enlace", "Sin enlace");
        marca.Carrusel[0].ImagenUrl.Should().Be($"{MarcaPuntoEndpoints.RutaImagenes}{bueno}");
        marca.Carrusel[0].Enlace.Should().Be("https://example.test/a");
        marca.Carrusel[1].Enlace.Should().BeNull("enlace vacío = sin enlace");
    }

    [Fact]
    public void Carrusel_ElBffNoEntregaMasDeSeisItems()
    {
        var items = Enumerable.Range(0, 9).Select(i => Item(Guid.NewGuid().ToString(), $"Imagen {i}")).ToArray();

        MarcaPuntoGrpc.ARespuesta(RespuestaGrpc("organismo", items), MarcaPuntoRespuesta.OrigenOrganismo)
            .Carrusel.Should().HaveCount(6);
    }

    [Fact]
    public void Carrusel_SoloElOrigenOrganismoLoLlena_LaWebYUnaMarcaIncompletaVanVacias()
    {
        var item = Item(ImagenCarrusel.ToString(), "Alt");

        MarcaPuntoGrpc.ARespuesta(RespuestaGrpc("web", item), MarcaPuntoRespuesta.OrigenWeb).Carrusel.Should().BeEmpty();
        var incompleta = RespuestaGrpc("organismo", item);
        incompleta.Nombre = string.Empty;
        MarcaPuntoGrpc.ARespuesta(incompleta, MarcaPuntoRespuesta.OrigenOrganismo).Should().Be(MarcaPuntoRespuesta.Flit);
        MarcaPuntoGrpc.ARespuesta(RespuestaGrpc("flit", item), MarcaPuntoRespuesta.OrigenOrganismo).Carrusel.Should().BeEmpty();
    }

    // Mejora de la Épica #14297 — lista de banners del portal del organismo.

    private static BannerItem Banner(string id, string alt, string enlace = "") => new() { ImagenId = id, Alt = alt, Enlace = enlace };

    private static ObtenerMarcaPuntoResponse RespuestaBanners(string origen, string bannerId, params BannerItem[] items)
    {
        var r = RespuestaGrpc(origen);
        r.BannerId = bannerId;
        r.Banners.AddRange(items);
        return r;
    }

    [Fact]
    public async Task Banners_ElPortalRecibeElArregloConUrlPublicaAltYEnlace_YBannerUrlEsElPrimero()
    {
        var basica = DelOrganismo();
        var marca = new MarcaPuntoRespuesta(basica.Origen, basica.Nombre, basica.Colores, basica.LogoUrl, $"{MarcaPuntoEndpoints.RutaImagenes}{ImagenCarrusel}", basica.Version)
        {
            Banners =
            [
                new CarruselMarcaPunto($"{MarcaPuntoEndpoints.RutaImagenes}{ImagenCarrusel}", "Primero", "https://tramites.example.test/a"),
                new CarruselMarcaPunto($"{MarcaPuntoEndpoints.RutaImagenes}{Logo}", "Segundo", null),
            ],
        };
        await using var host = new Anfitrion(new FachadaMarca { Marca = marca });

        var json = JsonDocument.Parse(await (await host.GetAsync("/punto/marca/AB1")).Content.ReadAsStringAsync(Ct)).RootElement;

        var items = json.GetProperty("banners").EnumerateArray().ToList();
        items.Should().HaveCount(2);
        items[0].EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("imagenUrl", "alt", "enlace");
        items[0].GetProperty("imagenUrl").GetString().Should().Be($"/api/v1/persona-natural/marca/imagenes/{ImagenCarrusel}");
        items[0].GetProperty("alt").GetString().Should().Be("Primero");
        items[1].GetProperty("enlace").ValueKind.Should().Be(JsonValueKind.Null);
        json.GetProperty("bannerUrl").GetString().Should().Be(items[0].GetProperty("imagenUrl").GetString());
    }

    [Fact]
    public async Task Banners_LaMarcaFlitVaConElArregloVacio()
    {
        await using var host = new Anfitrion(new FachadaMarca { Marca = MarcaPuntoRespuesta.Flit });

        var json = JsonDocument.Parse(await (await host.GetAsync("/punto/marca/AB1")).Content.ReadAsStringAsync(Ct)).RootElement;

        json.GetProperty("banners").ValueKind.Should().Be(JsonValueKind.Array);
        json.GetProperty("banners").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public void Banners_ElBffDescartaItemsInvalidos_ConservaElOrden_Y_ConservaBannerUrl()
    {
        var bueno = ImagenCarrusel.ToString();
        var r = RespuestaBanners(
            "organismo", bueno,
            Banner(bueno, "Con enlace", "https://example.test/a"),
            Banner("no-es-un-id", "Id inválido"),
            Banner(Guid.NewGuid().ToString(), "   "),
            Banner(Guid.NewGuid().ToString(), "Enlace js", "javascript:alert(1)"),
            Banner(Guid.NewGuid().ToString(), "Sin enlace"));

        var marca = MarcaPuntoGrpc.ARespuesta(r, MarcaPuntoRespuesta.OrigenOrganismo);

        marca.Banners.Select(i => i.Alt).Should().Equal("Con enlace", "Sin enlace");
        marca.Banners[0].ImagenUrl.Should().Be($"{MarcaPuntoEndpoints.RutaImagenes}{bueno}");
        marca.Banners[0].Enlace.Should().Be("https://example.test/a");
        marca.Banners[1].Enlace.Should().BeNull();
        marca.BannerUrl.Should().Be($"{MarcaPuntoEndpoints.RutaImagenes}{bueno}", "compatibilidad: bannerUrl sigue siendo el primero");
    }

    [Fact]
    public void Banners_ElBffNoEntregaMasDeSeis_YSoloElOrigenOrganismoLosLlena()
    {
        var items = Enumerable.Range(0, 9).Select(i => Banner(Guid.NewGuid().ToString(), $"Banner {i}")).ToArray();

        MarcaPuntoGrpc.ARespuesta(RespuestaBanners("organismo", "", items), MarcaPuntoRespuesta.OrigenOrganismo).Banners.Should().HaveCount(6);
        MarcaPuntoGrpc.ARespuesta(RespuestaBanners("web", "", items), MarcaPuntoRespuesta.OrigenWeb).Banners.Should().BeEmpty();
        MarcaPuntoGrpc.ARespuesta(RespuestaBanners("flit", "", items), MarcaPuntoRespuesta.OrigenOrganismo).Banners.Should().BeEmpty();
    }

    private sealed class FachadaMarca : IMarcaPunto
    {
        public MarcaPuntoRespuesta MarcaWeb { get; set; } = MarcaPuntoRespuesta.Flit;
        public int LlamadasWeb { get; private set; }

        public Task<MarcaPuntoRespuesta> ObtenerWebAsync(CancellationToken ct)
        {
            LlamadasWeb++;
            return Falla is not null ? Task.FromException<MarcaPuntoRespuesta>(Falla) : Task.FromResult(MarcaWeb);
        }

        public MarcaPuntoRespuesta Marca { get; set; } = MarcaPuntoRespuesta.Flit;
        public ImagenMarcaPunto? Imagen { get; set; }
        public Exception? Falla { get; set; }
        public int Llamadas { get; private set; }

        public Task<MarcaPuntoRespuesta> ObtenerAsync(string codigo, CancellationToken ct)
        {
            Llamadas++;
            return Falla is not null ? Task.FromException<MarcaPuntoRespuesta>(Falla) : Task.FromResult(Marca);
        }

        public Task<ImagenMarcaPunto?> ObtenerImagenAsync(string imagenId, string? ifNoneMatch, CancellationToken ct) =>
            Falla is not null ? Task.FromException<ImagenMarcaPunto?>(Falla) : Task.FromResult(Imagen);
    }

    private sealed class Anfitrion : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly HttpClient _http;

        public Anfitrion(IMarcaPunto marca)
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddSingleton(marca);
            builder.Services.AddRateLimiter(o => o.AddPolicy(AbusoEndpoints.PoliticaPorIp, _ => RateLimitPartition.GetNoLimiter("marca-test")));
            _app = builder.Build();
            _app.UseRateLimiter();
            _app.MapMarcaPuntoEndpoints();
            _app.StartAsync().GetAwaiter().GetResult();
            _http = _app.GetTestClient();
        }

        public Task<HttpResponseMessage> GetAsync(string ruta, string? ifNoneMatch = null)
        {
            var peticion = new HttpRequestMessage(HttpMethod.Get, SesionEndpoints.Base + ruta);
            if (ifNoneMatch is not null)
                peticion.Headers.TryAddWithoutValidation("If-None-Match", ifNoneMatch);
            return _http.SendAsync(peticion, Ct);
        }

        public async ValueTask DisposeAsync()
        {
            _http.Dispose();
            await _app.DisposeAsync().ConfigureAwait(false);
        }
    }
}
