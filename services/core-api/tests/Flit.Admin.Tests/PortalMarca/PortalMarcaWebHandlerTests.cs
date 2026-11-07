using Flit.Admin.Application.Companies.TransitOffices.PortalMarca;
using Flit.Admin.Domain.Companies.TransitOffices.PortalMarca;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Flit.Admin.Tests.PortalMarca;

/// <summary>
/// HU #14401 (Feature #14400, Épica #14297) — la marca de la web de persona natural reutiliza los casos de uso del organismo con ámbito
/// web: mismas reglas de contraste, publicación y retiro, entrega sin borrador ni banner, y sin cruce con la marca de un organismo.
/// Repositorio en memoria; el SQL real se prueba en <see cref="PortalMarcaPostgresTests"/>.
/// </summary>
public sealed class PortalMarcaWebHandlerTests
{
    private static readonly Guid Organismo = Guid.Parse("0a000000-0000-4000-8000-000000014401");
    private static readonly Guid Actor = Guid.Parse("ac000000-0000-4000-8000-000000014401");
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly RepositorioMarcaEnMemoria _repo = new();
    private readonly AlmacenImagenEnMemoria _almacen = new();
    private readonly AuditoriaEnMemoria _auditoria = new();
    private readonly PortalMarcaAdminHandler _admin;
    private readonly PortalMarcaImagenHandler _imagenes;
    private readonly PortalMarcaPuntoHandler _punto;

    public PortalMarcaWebHandlerTests()
    {
        _admin = new PortalMarcaAdminHandler(_repo, _auditoria);
        _imagenes = new PortalMarcaImagenHandler(_repo, _almacen, _admin);
        _punto = new PortalMarcaPuntoHandler(_repo, _almacen, NullLogger<PortalMarcaPuntoHandler>.Instance);
        _repo.Codigos["OT-A"] = Organismo;
    }

    private static PortalMarcaColoresDto Colores(string principal = "#0B3D91", string secundario = "#0F5132", string sobre = "#FFFFFF") =>
        new(principal, secundario, sobre);

    private async Task<PortalMarcaDto> GuardarWebAsync(string nombre = "Web de persona", PortalMarcaColoresDto? colores = null)
    {
        var actual = await _admin.ObtenerAsync(PortalMarcaAmbito.Web, Ct);
        var r = await _admin.GuardarAsync(PortalMarcaAmbito.Web, new GuardarPortalMarcaRequest(nombre, colores ?? Colores(), actual.RowVersion), Actor, Ct);
        r.Error.Should().BeNull();
        return r.Valor!;
    }

    private async Task<PortalMarcaDto> GuardarOrganismoAsync(string nombre = "Organismo A")
    {
        var actual = await _admin.ObtenerAsync(Organismo, Ct);
        var r = await _admin.GuardarAsync(Organismo, new GuardarPortalMarcaRequest(nombre, Colores("#102A43"), actual.RowVersion), Actor, Ct);
        r.Error.Should().BeNull();
        return r.Valor!;
    }

    [Fact]
    public async Task LaWebPublicadaSeEntregaConOrigenWebSinBorradorNiBannerYConLaVersionPublicada()
    {
        var borrador = await GuardarWebAsync();
        // Un banner en el registro (no hay ruta para cargarlo) nunca sale en la entrega de la web.
        _repo.Sembrar(borrador.ToRegistro() with { Draft = borrador.ToRegistro().Draft with { BannerId = Guid.NewGuid() } });
        var actual = await _admin.ObtenerAsync(PortalMarcaAmbito.Web, Ct);
        await _admin.PublicarAsync(PortalMarcaAmbito.Web, actual.RowVersion, Actor, Ct);
        var publicada = await _admin.ObtenerAsync(PortalMarcaAmbito.Web, Ct);
        await _admin.GuardarAsync(PortalMarcaAmbito.Web, new GuardarPortalMarcaRequest("Solo borrador", Colores("#000000"), publicada.RowVersion), Actor, Ct);

        var marca = await _punto.ObtenerWebAsync(Ct);

        marca.Origen.Should().Be("web");
        marca.Nombre.Should().Be("Web de persona", "lo guardado como borrador no se ve");
        marca.Colores.Should().Be(new MarcaPuntoColoresDto("#0B3D91", "#0F5132", "#FFFFFF"));
        marca.BannerId.Should().BeNull("los banners de la web siguen en Contenido");
        marca.Version.Should().Be(1);
    }

    [Fact]
    public async Task SinMarcaPublicadaIncompletaORetiradaLaWebRecibeLaMarcaFlit()
    {
        (await _punto.ObtenerWebAsync(Ct)).Should().Be(MarcaPuntoDto.Flit, "no hay fila");

        var borrador = await GuardarWebAsync();
        (await _punto.ObtenerWebAsync(Ct)).Should().Be(MarcaPuntoDto.Flit, "el borrador no se publica solo");

        var publicada = (await _admin.PublicarAsync(PortalMarcaAmbito.Web, borrador.RowVersion, Actor, Ct)).Valor!;
        (await _punto.ObtenerWebAsync(Ct)).Origen.Should().Be("web");

        await _admin.RetirarAsync(PortalMarcaAmbito.Web, publicada.RowVersion, Actor, Ct);
        (await _punto.ObtenerWebAsync(Ct)).Should().Be(MarcaPuntoDto.Flit, "lo retirado se ve en la siguiente lectura");
    }

    [Fact]
    public async Task UnaFallaDeLecturaEntregaLaMarcaFlitYNuncaUnError()
    {
        _repo.FallarAlLeer = true;

        (await _punto.ObtenerWebAsync(Ct)).Should().Be(MarcaPuntoDto.Flit);
    }

    [Fact]
    public async Task LaWebSinCacheCadaLecturaVaAlRepositorio()
    {
        await _punto.ObtenerWebAsync(Ct);
        await _punto.ObtenerWebAsync(Ct);

        _repo.Lecturas.Should().Be(2);
    }

    [Fact]
    public async Task LaWebExigeElMismoContrasteQueElOrganismoYNoPublicaNada()
    {
        var guardado = await GuardarWebAsync(colores: Colores(principal: "#FFEB3B"));

        var r = await _admin.PublicarAsync(PortalMarcaAmbito.Web, guardado.RowVersion, Actor, Ct);

        r.Error!.Code.Should().Be(PortalMarcaErrores.Contraste);
        r.Error.Status.Should().Be(422);
        (await _admin.ObtenerAsync(PortalMarcaAmbito.Web, Ct)).Published.Should().BeNull();
    }

    [Fact]
    public async Task LaWebNoSePublicaSinNombreNiColores()
    {
        var actual = await _admin.ObtenerAsync(PortalMarcaAmbito.Web, Ct);
        var r = await _admin.PublicarAsync(PortalMarcaAmbito.Web, actual.RowVersion, Actor, Ct);

        r.Error!.Code.Should().Be(PortalMarcaErrores.Incompleta);
    }

    [Fact]
    public async Task LaWebConservaElControlDeVersion()
    {
        var guardado = await GuardarWebAsync();

        var viejo = await _admin.GuardarAsync(PortalMarcaAmbito.Web, new GuardarPortalMarcaRequest("Otro", Colores(), guardado.RowVersion - 1), Actor, Ct);

        viejo.Error!.Code.Should().Be(PortalMarcaErrores.Conflicto);
    }

    // ── Web y organismo no se cruzan ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LaMarcaWebPublicadaNoLlegaAlPuntoDeNingunOrganismoNiCambiaSuMarca()
    {
        var organismo = await GuardarOrganismoAsync();
        var antes = await _repo.ObtenerAsync(Organismo, Ct);
        var web = await GuardarWebAsync("Web");
        await _admin.PublicarAsync(PortalMarcaAmbito.Web, web.RowVersion, Actor, Ct);

        (await _punto.ObtenerAsync("OT-A", Ct)).Should().Be(MarcaPuntoDto.Flit, "el organismo no publicó y no hereda la web");
        (await _repo.ObtenerAsync(Organismo, Ct)).Should().Be(antes, "publicar la web no toca la marca del organismo");
        (await _admin.ObtenerAsync(Organismo, Ct)).Draft.PlatformName.Should().Be(organismo.Draft.PlatformName);
    }

    [Fact]
    public async Task LaMarcaDelOrganismoPublicadaNoLlegaALaWebNiCambiaSuMarca()
    {
        var web = await GuardarWebAsync("Web");
        var antes = await _repo.ObtenerAsync(PortalMarcaAmbito.Web, Ct);
        var organismo = await GuardarOrganismoAsync();
        await _admin.PublicarAsync(Organismo, organismo.RowVersion, Actor, Ct);

        (await _punto.ObtenerWebAsync(Ct)).Should().Be(MarcaPuntoDto.Flit, "la web no publicó y no hereda el organismo");
        (await _repo.ObtenerAsync(PortalMarcaAmbito.Web, Ct)).Should().Be(antes, "publicar el organismo no toca la marca de la web");
        (await _punto.ObtenerAsync("OT-A", Ct)).Origen.Should().Be("organismo");
        web.Draft.PlatformName.Should().Be("Web");
    }

    [Fact]
    public async Task LaImagenDeLaWebQuedaEnSuAmbitoYSePuedeQuitar()
    {
        using var logo = new MemoryStream(ImagenesDePrueba.Png());

        var subido = await _imagenes.SubirAsync(PortalMarcaAmbito.Web, PortalMarcaImagen.KindLogo, "logo.png", logo, Actor, Ct);

        subido.Error.Should().BeNull();
        var id = subido.Valor!.Draft.LogoId!.Value;
        (await _imagenes.BuscarAsync(id, Ct))!.TransitOfficeId.Should().Be(PortalMarcaAmbito.Web);
        (await _admin.ObtenerAsync(Organismo, Ct)).Draft.LogoId.Should().BeNull("el logo de la web no aparece en el organismo");

        var quitado = await _imagenes.QuitarAsync(PortalMarcaAmbito.Web, PortalMarcaImagen.KindLogo, Actor, Ct);
        quitado.Draft.LogoId.Should().BeNull();
    }

    [Fact]
    public async Task LaAuditoriaDeLaWebSeDistingueDeLaDelOrganismo()
    {
        await GuardarWebAsync();
        await GuardarOrganismoAsync();

        _auditoria.Entradas[0].EntityName.Should().Be("portal_marca_web_borrador");
        _auditoria.Entradas[0].TargetEntityType.Should().Be("PERSONA_NATURAL_MARCA_WEB");
        _auditoria.Entradas[0].TargetEntityId.Should().BeNull();
        _auditoria.Entradas[1].EntityName.Should().Be("portal_marca_borrador");
        _auditoria.Entradas[1].TargetEntityId.Should().Be(Organismo);
    }
}

internal static class PortalMarcaWebPruebas
{
    /// <summary>Reconstruye el registro de un DTO de administración para sembrarlo en el repositorio en memoria.</summary>
    public static PortalMarcaRegistro ToRegistro(this PortalMarcaDto d) => new(
        d.TransitOfficeId,
        new PortalMarcaSnapshot(
            d.Draft.PlatformName,
            d.Draft.Colors is null ? null : new Flit.Admin.Domain.Companies.Branding.BrandColors(d.Draft.Colors.Primary, d.Draft.Colors.Secondary, d.Draft.Colors.OnPrimary),
            d.Draft.LogoId,
            d.Draft.BannerId),
        null,
        d.PublishedVersion,
        null,
        null,
        d.RowVersion);
}
