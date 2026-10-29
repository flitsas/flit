using System.Security.Cryptography;
using System.Text.Json;
using Flit.Admin.Application.Banners.Ports;
using Flit.Admin.Application.Plataforma.PersonaNatural;
using Flit.Admin.Domain.Plataforma.PersonaNatural;
using Flit.Admin.Tests.Identity;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.PersonaNatural;
using Flit.Platform.Sdk;
using Flit.Platform.Sdk.Grpc;
using Flit.Tramites.Grpc.V1;
using FluentAssertions;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using Cliente = Flit.Tramites.Grpc.V1.PersonaNaturalService.PersonaNaturalServiceClient;

namespace Flit.Admin.Tests.PersonaNatural;

/// <summary>
/// HU #14073 (Feature J, Épica #14021) — contenido administrable del portal: preguntas frecuentes, banners, enlaces,
/// borrador / publicado, y lectura acotada por la fachada gRPC. Corre contra la base con las migraciones aplicadas (DDL 165)
/// y un almacenamiento de imágenes en memoria. La publicación es GLOBAL (una sola versión vigente para todo el portal), así
/// que todas las pruebas de esta clase corren en serie y cada una retira lo que creó (las versiones son historial inmutable por
/// trigger: solo un superusuario, la base de pruebas, puede retirarlas).
/// </summary>
public sealed class ContenidoPnTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Scope = "platform.tramites.persona_natural";
    private const string Portal = "svc-personanatural";
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4, 5, 6, 7, 8];

    // ── Reglas de forma (sin base) ────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("<b>Hola</b>")]
    [InlineData("texto <script>alert(1)</script>")]
    [InlineData("<!-- x -->")]
    public void AC1_NoSeAceptaHtml(string texto) =>
        ContenidoPnReglas.ValidarTexto(texto, "La pregunta", 300)!.Value.Error.Should().Be(ContenidoPnErrores.HtmlNoPermitido);

    [Fact]
    public void AC1_UnaComparacionConMenorQueNoEsHtmlYElLimiteDeLongitudSeAplica()
    {
        ContenidoPnReglas.ValidarTexto("¿Cuesta menos de 5 < 10 pesos?", "La pregunta", 300).Should().BeNull();
        ContenidoPnReglas.ValidarTexto(new string('a', 301), "La pregunta", 300)!.Value.Error.Should().Be(ContenidoPnErrores.TextoInvalido);
        ContenidoPnReglas.ValidarTexto("  ", "La pregunta", 300)!.Value.Error.Should().Be(ContenidoPnErrores.TextoInvalido);
    }

    [Theory]
    [InlineData("POLITICA_DATOS", "ftp://x.co/doc")]
    [InlineData("TERMINOS", "no es url")]
    [InlineData("TERMINOS", "mailto:a@b.co")]
    public void AC3_UnEnlaceSoloAceptaUrlHttp_ElMailtoSoloEnElContactoDeVentas(string codigo, string url) =>
        ContenidoPnReglas.ValidarEnlace(codigo, url)!.Value.Error.Should().Be(ContenidoPnErrores.EnlaceInvalido);

    [Fact]
    public void AC3_ElContactoDeVentasAceptaMailtoYTelYUnEnlaceVacioEsSinDocumento()
    {
        ContenidoPnReglas.ValidarEnlace("CONTACTO_VENTAS", "mailto:ventas@flit.test").Should().BeNull();
        ContenidoPnReglas.ValidarEnlace("CONTACTO_VENTAS", "tel:+576010000000").Should().BeNull();
        ContenidoPnReglas.ValidarEnlace("POLITICA_DATOS", null).Should().BeNull();
    }

    [Fact]
    public void AC4_LaFotoDelContenidoEsCanonica_YDetectaSiElBorradorDifiereDeLoPublicado()
    {
        var a = new FaqPublicadaPn(Guid.Parse("00000000-0000-0000-0000-000000000001"), "p1", "r1", 1);
        var b = new FaqPublicadaPn(Guid.Parse("00000000-0000-0000-0000-000000000002"), "p2", "r2", 2);
        var enlaces = CodigosEnlacePn.Todos.Select(c => new EnlacePn(c, null, null)).ToList();
        var uno = new ContenidoPn([a, b], [], enlaces);
        var otroOrden = new ContenidoPn([b, a], [], enlaces);
        var distinto = new ContenidoPn([a], [], enlaces);

        ContenidoPnSnapshot.Escribir(uno).Should().Be(ContenidoPnSnapshot.Escribir(otroOrden), "la lista se ordena por orden e id");
        ContenidoPnSnapshot.Difiere(uno, ContenidoPnSnapshot.Leer(ContenidoPnSnapshot.Escribir(uno))).Should().BeFalse("la ida y vuelta conserva el contenido");
        ContenidoPnSnapshot.Difiere(distinto, uno).Should().BeTrue();
        ContenidoPnSnapshot.Difiere(uno, null).Should().BeTrue("sin versión publicada hay cambios por publicar");
    }

    // ── AC1 / AC4 — preguntas frecuentes, borrador y publicado ────────────────────────────────────────────────────────

    [Fact]
    public async Task AC1_AC4_ElPortalSoloVeLasPreguntasActivasPublicadas_EnElOrdenConfigurado_YElBorradorNoSeVe()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = AnfitrionGrpc(out _);
        await using var e = await Escenario.CrearAsync(host, ct);
        var p1 = (await e.FaqAsync("Primera", "Respuesta 1", null, e.Actor, ct)).Valor!;
        var p2 = (await e.FaqAsync("Segunda", "Respuesta 2", null, e.Actor, ct)).Valor!;
        var p3 = (await e.FaqAsync("Tercera", "Respuesta 3", null, e.Actor, ct)).Valor!;

        // Borrador: nada se ve en el portal.
        (await e.Lector.LeerAsync(ct)).Preguntas.Should().NotContain(p => p.Id == p1.Id);

        // La lista de orden tiene que traer todas las preguntas (también las iniciales del portal, si la base ya las cargó).
        var resto = (await e.Contenido.ListarFaqsAsync(ct)).Select(f => f.Id).Where(id => id != p3.Id && id != p1.Id && id != p2.Id);
        (await e.Contenido.ReordenarFaqsAsync([p3.Id, p1.Id, p2.Id, .. resto], e.Actor, ct)).Error.Should().BeNull();
        (await e.Contenido.EstablecerFaqActivaAsync(p2.Id, false, e.Actor, ct)).Error.Should().BeNull();
        (await e.Contenido.ActualizarFaqAsync(p1.Id, "Primera editada", "Respuesta 1 editada", null, e.Actor, ct)).Error.Should().BeNull();
        var publicada = await e.Contenido.PublicarAsync(e.Actor, ct);

        publicada.Error.Should().BeNull();
        var visto = (await e.Lector.LeerAsync(ct)).Preguntas.Where(p => e.Ids.Contains(p.Id)).ToList();
        visto.Select(p => p.Pregunta).Should().Equal("Tercera", "Primera editada");
    }

    [Fact]
    public async Task AC1_UnaListaDeOrdenQueNoTraeTodasLasPreguntasSeRechaza_YElTextoInvalidoNoSeGuarda()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = AnfitrionGrpc(out _);
        await using var e = await Escenario.CrearAsync(host, ct);
        var p1 = (await e.FaqAsync("Primera", "Respuesta", null, e.Actor, ct)).Valor!;
        await e.FaqAsync("Segunda", "Respuesta", null, e.Actor, ct);

        (await e.Contenido.ReordenarFaqsAsync([p1.Id], e.Actor, ct)).Error.Should().Be(ContenidoPnErrores.OrdenInvalido);
        (await e.FaqAsync("<b>x</b>", "ok", null, e.Actor, ct)).Error.Should().Be(ContenidoPnErrores.HtmlNoPermitido);
        (await e.Contenido.ActualizarFaqAsync(Guid.NewGuid(), "a", "b", null, e.Actor, ct)).Error.Should().Be(ContenidoPnErrores.NoEncontrado);
        (await e.Contenido.ListarFaqsAsync(ct)).Where(f => e.Ids.Contains(f.Id)).Should().HaveCount(2);
    }

    [Fact]
    public async Task AC4_AlPublicarQuedaConFechaUsuarioYLaVersionAnteriorSigueConsultable_YSinCambiosNoPublica()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = AnfitrionGrpc(out _);
        await using var e = await Escenario.CrearAsync(host, ct);
        await e.FaqAsync("Pregunta uno", "Respuesta uno", null, e.Actor, ct);
        var v1 = (await e.Contenido.PublicarAsync(e.Actor, ct)).Valor!;
        (await e.Contenido.PublicarAsync(e.Actor, ct)).Error.Should().Be(ContenidoPnErrores.SinCambios);

        var faq = (await e.Contenido.ListarFaqsAsync(ct)).Single(f => e.Ids.Contains(f.Id));
        await e.Contenido.ActualizarFaqAsync(faq.Id, "Pregunta uno editada", "Respuesta uno", null, e.Actor, ct);
        (await e.Contenido.BorradorAsync(ct)).Should().Match<BorradorContenidoPn>(b => b.HayCambiosSinPublicar && b.VersionPublicada == v1.Version);
        (await e.Lector.LeerAsync(ct)).Preguntas.Single(p => p.Id == faq.Id).Pregunta.Should().Be("Pregunta uno", "el borrador no se ve");

        var v2 = (await e.Contenido.PublicarAsync(e.Actor, ct)).Valor!;

        v2.Version.Should().Be(v1.Version + 1);
        v2.PublicadoPor.Should().Be(e.Actor);
        v2.PublicadoEn.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2));
        (await e.Contenido.ObtenerVersionAsync(v1.Version, ct))!.Contenido.Preguntas.Single(p => p.Id == faq.Id).Pregunta.Should().Be("Pregunta uno");
        (await e.Contenido.ListarVersionesAsync(ct)).Select(v => v.Version).Should().Contain([v1.Version, v2.Version]);
        // Una versión publicada es historial: la base no deja editarla ni borrarla.
        var editar = async () => await e.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE admin.persona_natural_content_versions SET published_by = NULL WHERE version = {v1.Version}", ct);
        await editar.Should().ThrowAsync<Exception>();
        var borrar = async () => await e.Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM admin.persona_natural_content_versions WHERE version = {v1.Version}", ct);
        await borrar.Should().ThrowAsync<Exception>();
    }

    // ── AC2 — banners ─────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC2_UnBannerSinTextoAlternativoOConImagenInvalidaSeRechaza()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = AnfitrionGrpc(out _);
        await using var e = await Escenario.CrearAsync(host, ct);

        (await e.BannerAsync(Datos(texto: "  ", imagen: Imagen(Png)), e.Actor, ct)).Error.Should().Be(ContenidoPnErrores.BannerInvalido);
        (await e.BannerAsync(Datos(imagen: null), e.Actor, ct)).Error.Should().Be(ContenidoPnErrores.ImagenObligatoria);
        (await e.BannerAsync(Datos(imagen: Imagen(Png, "application/pdf")), e.Actor, ct)).Error.Should().Be(ContenidoPnErrores.ImagenInvalida);
        (await e.BannerAsync(Datos(imagen: Imagen("esto no es una imagen"u8.ToArray())), e.Actor, ct)).Error.Should().Be(ContenidoPnErrores.ImagenInvalida, "los primeros bytes no son los de una imagen");
        var grande = new byte[(2 * 1024 * 1024) + 1];
        Png.CopyTo(grande, 0);
        (await e.BannerAsync(Datos(imagen: Imagen(grande)), e.Actor, ct)).Error.Should().Be(ContenidoPnErrores.ImagenInvalida, "más de 2 MB");
        (await e.BannerAsync(Datos(vigenteDesde: DateTimeOffset.UtcNow, vigenteHasta: null, imagen: Imagen(Png)), e.Actor, ct)).Error.Should().Be(ContenidoPnErrores.BannerInvalido, "ambas fechas o ninguna");
        (await e.Contenido.ListarBannersAsync(ct)).Where(b => e.Ids.Contains(b.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task AC2_ElPortalMuestraElBannerSoloDentroDeSuVigencia()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = AnfitrionGrpc(out _);
        await using var e = await Escenario.CrearAsync(host, ct);
        var ahora = DateTimeOffset.UtcNow;
        var siempre = (await e.BannerAsync(Datos(titulo: "Siempre", imagen: Imagen(Png)), e.Actor, ct)).Valor!;
        var vigente = (await e.BannerAsync(Datos(titulo: "Vigente", orden: 2, vigenteDesde: ahora.AddDays(-1), vigenteHasta: ahora.AddDays(1), imagen: Imagen(Png)), e.Actor, ct)).Valor!;
        var vencido = (await e.BannerAsync(Datos(titulo: "Vencido", orden: 3, vigenteDesde: ahora.AddDays(-5), vigenteHasta: ahora.AddDays(-2), imagen: Imagen(Png)), e.Actor, ct)).Valor!;
        var futuro = (await e.BannerAsync(Datos(titulo: "Futuro", orden: 4, vigenteDesde: ahora.AddDays(2), vigenteHasta: ahora.AddDays(5), imagen: Imagen(Png)), e.Actor, ct)).Valor!;
        var inactivo = (await e.BannerAsync(Datos(titulo: "Inactivo", orden: 5, activo: false, imagen: Imagen(Png)), e.Actor, ct)).Valor!;
        e.Ids.UnionWith([siempre.Id, vigente.Id, vencido.Id, futuro.Id, inactivo.Id]);

        (await e.Contenido.PublicarAsync(e.Actor, ct)).Error.Should().BeNull();

        var banners = (await e.Lector.LeerAsync(ct)).Banners.Where(b => e.Ids.Contains(b.Id)).Select(b => b.Titulo).ToList();
        banners.Should().Equal("Siempre", "Vigente");
        (await e.Lector.BannerVigenteAsync(vencido.Id, ct)).Should().BeNull("venció");
        (await e.Lector.BannerVigenteAsync(inactivo.Id, ct)).Should().BeNull("no se publicó");
        (await e.Lector.BannerVigenteAsync(vigente.Id, ct)).Should().NotBeNull();
    }

    [Fact]
    public async Task AC2_ReemplazarLaImagenConservaLosDemasDatos_YDesactivarOReordenarQuedaAuditado()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = AnfitrionGrpc(out _);
        await using var e = await Escenario.CrearAsync(host, ct);
        var b = (await e.BannerAsync(Datos(titulo: "Uno", imagen: Imagen(Png)), e.Actor, ct)).Valor!;
        var otra = Png.Concat(new byte[] { 9, 9, 9 }).ToArray();

        var editado = (await e.Contenido.ActualizarBannerAsync(b.Id, Datos(titulo: "Uno editado", imagen: Imagen(otra)), e.Actor, ct)).Valor!;
        var sinImagen = (await e.Contenido.ActualizarBannerAsync(b.Id, Datos(titulo: "Uno de nuevo", imagen: null), e.Actor, ct)).Valor!;
        var apagado = (await e.Contenido.EstablecerBannerActivoAsync(b.Id, false, e.Actor, ct)).Valor!;

        editado.ImagenSha256.Should().NotBe(b.ImagenSha256);
        sinImagen.ImagenSha256.Should().Be(editado.ImagenSha256, "sin imagen nueva se conserva la actual");
        apagado.Activo.Should().BeFalse();
        var auditoria = await e.Db.Database.SqlQuery<AuditoriaFila>($"""
            SELECT operation, new_value FROM admin.tenant_config_audit_logs
             WHERE entity_name = 'persona_natural_banners' AND changed_by = {e.Actor}
            """).ToListAsync(ct);
        auditoria.Select(a => a.Operation).Should().BeEquivalentTo("create", "update", "update", "deactivate");
        (await e.Contenido.ReordenarBannersAsync([b.Id], e.Actor, ct)).Error.Should().BeNull();
        (await e.Contenido.ActualizarBannerAsync(Guid.NewGuid(), Datos(imagen: null), e.Actor, ct)).Error.Should().Be(ContenidoPnErrores.NoEncontrado);
    }

    // ── AC3 — enlaces y contacto ──────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC3_UnEnlaceLegalSinDocumentoSePublicaSinValor_YConDocumentoViajaConSuUrl()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = AnfitrionGrpc(out _);
        await using var e = await Escenario.CrearAsync(host, ct);
        await e.FaqAsync("Para tener algo que publicar", "ok", null, e.Actor, ct);
        e.EnlacesTocados = true;
        var antes = (await e.Contenido.ListarEnlacesAsync(ct)).Single(l => l.Codigo == "POLITICA_DATOS").Url;

        (await e.Contenido.EstablecerEnlaceAsync("POLITICA_DATOS", "https://flit.test/politica.pdf", e.Actor, ct)).Error.Should().BeNull();
        (await e.Contenido.EstablecerEnlaceAsync("terminos", "  ", e.Actor, ct)).Error.Should().BeNull("un enlace vacío = sin documento oficial");
        (await e.Contenido.EstablecerEnlaceAsync("OTRO", "https://x.co", e.Actor, ct)).Error.Should().Be(ContenidoPnErrores.CodigoEnlaceInvalido);
        await e.Contenido.PublicarAsync(e.Actor, ct);

        var enlaces = (await e.Lector.LeerAsync(ct)).Enlaces;
        enlaces.Select(l => l.Codigo).Should().Equal(CodigosEnlacePn.Todos, "siempre los cuatro");
        enlaces.Single(l => l.Codigo == "POLITICA_DATOS").Url.Should().Be("https://flit.test/politica.pdf");
        enlaces.Single(l => l.Codigo == "TERMINOS").Url.Should().BeNull("sin documento oficial el portal conserva su aviso");
        antes.Should().BeNull("el contenido nace vacío");
    }

    // ── AC5 — lectura pública acotada por la fachada gRPC ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC5_ObtenerConfiguracionPortal_DevuelveSoloLoPublicadoYVigente_SinBorradorNiParametrosDeProteccion()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = AnfitrionGrpc(out var almacenamiento);
        await using var e = await Escenario.CrearAsync(host, ct);
        var ahora = DateTimeOffset.UtcNow;
        var faq = (await e.FaqAsync("Pregunta publicada", "Respuesta publicada", null, e.Actor, ct)).Valor!;
        var vigente = (await e.BannerAsync(Datos(titulo: "Banner vigente", textoAlternativo: "Texto alternativo", enlaceUrl: "https://flit.test/promo", vigenteDesde: ahora.AddDays(-1), vigenteHasta: ahora.AddDays(1), imagen: Imagen(Png)), e.Actor, ct)).Valor!;
        var vencido = (await e.BannerAsync(Datos(titulo: "Banner vencido", vigenteDesde: ahora.AddDays(-5), vigenteHasta: ahora.AddDays(-2), imagen: Imagen(Png)), e.Actor, ct)).Valor!;
        e.Ids.UnionWith([vigente.Id, vencido.Id]);
        await e.Contenido.PublicarAsync(e.Actor, ct);
        // Un borrador posterior (no publicado) no se ve.
        await e.FaqAsync("Pregunta en borrador", "No debe verse", null, e.Actor, ct);
        var cliente = await ClienteAsync(host, PlatformTenants.PersonaNatural, ct);

        var r = await cliente.ObtenerConfiguracionPortalAsync(new ObtenerConfiguracionPortalRequest(), cancellationToken: ct);

        int.Parse(r.Version, System.Globalization.CultureInfo.InvariantCulture).Should().BeGreaterThan(0);
        r.PublicadoEn.Should().NotBeNull();
        r.Preguntas.Select(p => p.Pregunta).Should().Contain("Pregunta publicada").And.NotContain("Pregunta en borrador");
        r.Banners.Select(b => b.Titulo).Should().Contain("Banner vigente").And.NotContain("Banner vencido");
        var banner = r.Banners.Single(b => b.Id == vigente.Id.ToString());
        (banner.TextoAlternativo, banner.EnlaceUrl, banner.ImagenContentType).Should().Be(("Texto alternativo", "https://flit.test/promo", "image/png"));
        r.Enlaces.Select(l => l.Codigo).Should().Equal(CodigosEnlacePn.Todos);
        r.Enlaces.Should().OnlyContain(l => l.ConDocumento == (l.Url.Length > 0));
        // La respuesta no tiene ningún campo de datos bancarios ni de protección (topes, captcha, sesión).
        var campos = ObtenerConfiguracionPortalResponse.Descriptor.Fields.InDeclarationOrder().Select(f => f.Name)
            .Concat(ParametrosPublicosPortal.Descriptor.Fields.InDeclarationOrder().Select(f => f.Name)).ToList();
        campos.Should().NotContain(c => c.Contains("banc", StringComparison.OrdinalIgnoreCase) || c.Contains("cuenta", StringComparison.OrdinalIgnoreCase)
            || c.Contains("tope", StringComparison.OrdinalIgnoreCase) || c.Contains("captcha", StringComparison.OrdinalIgnoreCase) || c.Contains("sesion", StringComparison.OrdinalIgnoreCase));

        // Imagen: bytes con ETag; el mismo ETag no devuelve bytes; un banner vencido o no publicado no se encuentra.
        var imagen = await cliente.ObtenerImagenBannerPortalAsync(new ObtenerImagenBannerPortalRequest { BannerId = vigente.Id.ToString() }, cancellationToken: ct);
        imagen.Encontrado.Should().BeTrue();
        imagen.Contenido.ToByteArray().Should().Equal(Png);
        imagen.Sha256.Should().Be(Convert.ToHexString(SHA256.HashData(Png)).ToLowerInvariant());
        var igual = await cliente.ObtenerImagenBannerPortalAsync(new ObtenerImagenBannerPortalRequest { BannerId = vigente.Id.ToString(), IfNoneMatch = imagen.Sha256 }, cancellationToken: ct);
        (igual.NoModificado, igual.Contenido.Length).Should().Be((true, 0));
        (await cliente.ObtenerImagenBannerPortalAsync(new ObtenerImagenBannerPortalRequest { BannerId = vencido.Id.ToString() }, cancellationToken: ct)).Encontrado.Should().BeFalse();
        (await cliente.ObtenerImagenBannerPortalAsync(new ObtenerImagenBannerPortalRequest { BannerId = Guid.NewGuid().ToString() }, cancellationToken: ct)).Encontrado.Should().BeFalse();
        faq.Id.Should().NotBeEmpty();
        almacenamiento.Guardadas.Should().NotBeEmpty();
    }

    [Fact]
    public async Task AC5_ElCandadoVaPrimero_YUnaEmpresaAjenaNoLeeNada()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = AnfitrionGrpc(out _);
        var ajeno = await ClienteAsync(host, Guid.NewGuid(), ct);

        var error = (await FluentActions.Awaiting(() => ajeno.ObtenerConfiguracionPortalAsync(new ObtenerConfiguracionPortalRequest(), cancellationToken: ct).ResponseAsync)
            .Should().ThrowAsync<RpcException>()).Which;

        PlatformRpcErrors.Reason(error).Should().Be("invalid_tenant");
    }

    // ── Soporte ───────────────────────────────────────────────────────────────────────────────────────────────────────

    private static DatosBannerPn Datos(
        string titulo = "Banner de prueba", string? textoAlternativo = "Texto alternativo", string? enlaceUrl = null, int orden = 1,
        DateTimeOffset? vigenteDesde = null, DateTimeOffset? vigenteHasta = null, bool activo = true, ImagenSubidaPn? imagen = null, string? texto = null) =>
        new(titulo, texto ?? textoAlternativo, enlaceUrl, orden, vigenteDesde, vigenteHasta, activo, imagen);

    private static ImagenSubidaPn Imagen(byte[] bytes, string contentType = "image/png") =>
        new("banner.png", contentType, bytes.Length, new MemoryStream(bytes));

    private WebApplicationFactory<Program> AnfitrionGrpc(out AlmacenEnMemoria almacenamiento)
    {
        var almacen = new AlmacenEnMemoria();
        almacenamiento = almacen;
        return OidcServerTests.WithOidc(factory).WithWebHostBuilder(b =>
        {
            b.UseSetting("PersonaNatural:Fachada:Habilitada", "true");
            b.UseSetting($"Suite:Oidc:ServiceClients:{Portal}:Secret", OidcServerTests.ServiceSecret);
            b.UseSetting($"Suite:Oidc:ServiceClients:{Portal}:Scopes:0", Scope);
            b.ConfigureServices(s =>
            {
                s.RemoveAll<IBannerImageStorage>();
                s.AddSingleton<IBannerImageStorage>(almacen);
            });
        }).ConEmpresaPersonaNatural();
    }

    private static async Task<Cliente> ClienteAsync(WebApplicationFactory<Program> host, Guid empresa, CancellationToken ct)
    {
        using var http = host.CreateClient();
        http.DefaultRequestHeaders.Add("X-Flit-Domain", OidcServerTests.Hub);
        var response = await http.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = Portal,
            ["client_secret"] = OidcServerTests.ServiceSecret,
            ["scope"] = Scope,
        }), ct);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var token = json.RootElement.GetProperty("access_token").GetString()!;
        var canal = GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions { HttpHandler = host.Server.CreateHandler() });
        return new Cliente(canal.CreateCallInvoker().Intercept(metadata =>
        {
            metadata.Add("authorization", $"Bearer {token}");
            metadata.Add("x-flit-tenant-id", empresa.ToString());
            return metadata;
        }));
    }

    private sealed class AuditoriaFila
    {
        public string? Operation { get; set; }
        public string? NewValue { get; set; }
    }

    /// <summary>Almacenamiento de imágenes en memoria: sin file-manager ni S3 en las pruebas.</summary>
    internal sealed class AlmacenEnMemoria : IBannerImageStorage
    {
        private readonly Dictionary<string, byte[]> _archivos = [];

        public IReadOnlyCollection<string> Guardadas => _archivos.Keys;

        public async Task<StoredBannerImage> SaveAsync(string filename, Stream content, CancellationToken cancellationToken = default)
        {
            using var copia = new MemoryStream();
            await content.CopyToAsync(copia, cancellationToken);
            var bytes = copia.ToArray();
            var ruta = $"memoria/{Guid.NewGuid():N}-{filename}";
            lock (_archivos)
                _archivos[ruta] = bytes;
            return new StoredBannerImage(ruta, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), bytes.Length);
        }

        public Task<Stream?> OpenReadAsync(string storagePath, CancellationToken cancellationToken = default)
        {
            lock (_archivos)
                return Task.FromResult<Stream?>(_archivos.TryGetValue(storagePath, out var bytes) ? new MemoryStream(bytes) : null);
        }
    }

    /// <summary>Un usuario y todo lo que la prueba crea (preguntas, banners, enlaces, versiones); se retira al terminar.</summary>
    private sealed class Escenario : IAsyncDisposable
    {
        private readonly IServiceScope _scope;

        public FlitDbContext Db { get; }
        public ContenidoPnHandler Contenido { get; }
        public PersonaNaturalConfiguracionPublicaReader Lector { get; }
        public Guid Actor { get; } = Guid.NewGuid();
        public HashSet<Guid> Ids { get; } = [];
        public bool EnlacesTocados { get; set; }

        private Escenario(IServiceScope scope)
        {
            _scope = scope;
            var sp = scope.ServiceProvider;
            Db = sp.GetRequiredService<FlitDbContext>();
            Contenido = sp.GetRequiredService<ContenidoPnHandler>();
            Lector = sp.GetRequiredService<PersonaNaturalConfiguracionPublicaReader>();
        }

        public static async Task<Escenario> CrearAsync(WebApplicationFactory<Program> host, CancellationToken ct)
        {
            var e = new Escenario(host.Services.CreateScope());
            var correo = $"pn-cont-{e.Actor:N}@example.test";
            await e.Db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO identity.users (id, email, display_name, status, created_at) VALUES ({e.Actor}, {correo}, {"Actor de prueba"}, 'active', now())", ct);
            return e;
        }

        /// <summary>Crea la pregunta y recuerda su id (para filtrar lo que la prueba creó y retirarlo al terminar).</summary>
        public async Task<ResultadoContenidoPn<FaqPn>> FaqAsync(string pregunta, string respuesta, bool? activa, Guid actor, CancellationToken ct)
        {
            var r = await Contenido.CrearFaqAsync(pregunta, respuesta, activa, actor, ct);
            if (r.Valor is { } f)
                Ids.Add(f.Id);
            return r;
        }

        public async Task<ResultadoContenidoPn<BannerPn>> BannerAsync(DatosBannerPn datos, Guid actor, CancellationToken ct)
        {
            var r = await Contenido.CrearBannerAsync(datos, actor, ct);
            if (r.Valor is { } b)
                Ids.Add(b.Id);
            return r;
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var tx = await Db.Database.BeginTransactionAsync();
                await Db.Database.ExecuteSqlRawAsync("SET LOCAL session_replication_role = replica");
                await Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM admin.persona_natural_content_versions WHERE published_by = {Actor}");
                await Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM admin.persona_natural_faqs WHERE created_by = {Actor}");
                await Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM admin.persona_natural_banners WHERE created_by = {Actor}");
                await Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM admin.persona_natural_links WHERE created_by = {Actor}");
                await Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM admin.tenant_config_audit_logs WHERE changed_by = {Actor}");
                await Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM identity.users WHERE id = {Actor}");
                await tx.CommitAsync();
            }
            finally
            {
                _scope.Dispose();
            }
        }
    }
}
