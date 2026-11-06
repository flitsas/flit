using Flit.Admin.Application.Companies.Branding;
using Flit.Admin.Application.Companies.TransitOffices.PortalMarca;
using Flit.Admin.Domain.Companies.Branding;
using Flit.Admin.Domain.Companies.TransitOffices.PortalMarca;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Flit.Admin.Tests.PortalMarca;

/// <summary>
/// Feature #14374 (Épica #14297) — reglas y casos de uso de la marca del portal del organismo (HU #14375, #14376, #14378).
/// Repositorio y almacenamiento en memoria; el SQL real se prueba en <see cref="PortalMarcaPostgresTests"/>.
/// </summary>
public sealed partial class PortalMarcaHandlerTests
{
    private static readonly Guid Organismo = Guid.Parse("0a000000-0000-4000-8000-000000014375");
    private static readonly Guid Actor = Guid.Parse("ac000000-0000-4000-8000-000000014375");
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly RepositorioMarcaEnMemoria _repo = new();
    private readonly AlmacenImagenEnMemoria _almacen = new();
    private readonly AuditoriaEnMemoria _auditoria = new();
    private readonly PortalMarcaAdminHandler _admin;

    public PortalMarcaHandlerTests()
    {
        _admin = new PortalMarcaAdminHandler(_repo, _auditoria);
        _repo.Codigos["OT-A"] = Organismo;
    }

    private static PortalMarcaColoresDto Colores(string principal = "#0B3D91", string secundario = "#0F5132", string sobre = "#FFFFFF") =>
        new(principal, secundario, sobre);

    private async Task<PortalMarcaDto> GuardarValidoAsync(string nombre = "Tránsito de prueba")
    {
        var actual = await _admin.ObtenerAsync(Organismo, Ct);
        var r = await _admin.GuardarAsync(Organismo, new GuardarPortalMarcaRequest(nombre, Colores(), actual.RowVersion), Actor, Ct);
        r.Error.Should().BeNull();
        return r.Valor!;
    }

    // ── HU #14375 AC1 — publicar ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC1_PublicarUnBorradorConContrasteDejaLoPublicadoIgualAlBorradorYSubeLaVersion()
    {
        var borrador = await GuardarValidoAsync();

        var r = await _admin.PublicarAsync(Organismo, borrador.RowVersion, Actor, Ct);

        r.Error.Should().BeNull();
        r.Valor!.Published.Should().BeEquivalentTo(r.Valor.Draft);
        r.Valor.PublishedVersion.Should().Be(1);
        r.Valor.PublishedBy!.UserId.Should().Be(Actor);
        r.Valor.HasUnpublishedChanges.Should().BeFalse();

        var segunda = await _admin.PublicarAsync(Organismo, r.Valor.RowVersion, Actor, Ct);
        segunda.Valor!.PublishedVersion.Should().Be(2, "cada publicación aumenta la versión");
    }

    [Fact]
    public async Task AC1_SinPublicarElBorradorSigueTeniendoCambiosPendientes()
    {
        var borrador = await GuardarValidoAsync();

        borrador.Published.Should().BeNull();
        borrador.HasUnpublishedChanges.Should().BeTrue();
        borrador.Completeness.IsComplete.Should().BeTrue();
    }

    // ── HU #14375 AC2 — contraste ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC2_ConContrasteMenorA45NoSePublicaYRespondeElMotivoExacto()
    {
        var actual = await _admin.ObtenerAsync(Organismo, Ct);
        var guardado = await _admin.GuardarAsync(
            Organismo, new GuardarPortalMarcaRequest("Tránsito de prueba", Colores(principal: "#FFEB3B"), actual.RowVersion), Actor, Ct);
        guardado.Error.Should().BeNull("el borrador se puede guardar; el contraste se exige al publicar");

        var r = await _admin.PublicarAsync(Organismo, guardado.Valor!.RowVersion, Actor, Ct);

        r.Error.Should().NotBeNull();
        r.Error!.Code.Should().Be(PortalMarcaErrores.Contraste);
        r.Error.Status.Should().Be(422);
        r.Error.Message.Should().Contain("texto sobre el color principal").And.Contain("1,").And.Contain(":1").And.Contain("4,5");
        (await _admin.ObtenerAsync(Organismo, Ct)).Published.Should().BeNull("no se publica nada");
        (await _admin.ObtenerAsync(Organismo, Ct)).PublishedVersion.Should().Be(0);
    }

    [Fact]
    public async Task AC2_ElContrasteTambienSeExigeContraElSecundario()
    {
        var actual = await _admin.ObtenerAsync(Organismo, Ct);
        var g = await _admin.GuardarAsync(
            Organismo, new GuardarPortalMarcaRequest("Tránsito de prueba", Colores(secundario: "#EEEEEE"), actual.RowVersion), Actor, Ct);

        var r = await _admin.PublicarAsync(Organismo, g.Valor!.RowVersion, Actor, Ct);

        r.Error!.Code.Should().Be(PortalMarcaErrores.Contraste);
        r.Error.Message.Should().Contain("color secundario");
    }

    [Fact]
    public async Task AC2_UnBorradorIncompletoNoSePublicaYDiceQueFalta()
    {
        var actual = await _admin.ObtenerAsync(Organismo, Ct);

        var r = await _admin.PublicarAsync(Organismo, actual.RowVersion, Actor, Ct);

        r.Error!.Code.Should().Be(PortalMarcaErrores.Incompleta);
        r.Error.Message.Should().Contain("el nombre del portal").And.Contain("el color principal");
    }

    [Theory]
    [InlineData("A")]
    [InlineData("<b>Tránsito</b>")]
    public async Task GuardarConUnNombreInvalidoSeRechazaConMotivo(string nombre)
    {
        var actual = await _admin.ObtenerAsync(Organismo, Ct);

        var r = await _admin.GuardarAsync(Organismo, new GuardarPortalMarcaRequest(nombre, null, actual.RowVersion), Actor, Ct);

        r.Error!.Code.Should().Be(PortalMarcaErrores.Datos);
        r.Error.Status.Should().Be(400);
    }

    [Fact]
    public async Task GuardarConUnColorSinFormatoHexSeRechazaYUnoValidoSeNormalizaAMayusculas()
    {
        var actual = await _admin.ObtenerAsync(Organismo, Ct);
        var mal = await _admin.GuardarAsync(Organismo, new GuardarPortalMarcaRequest("Tránsito", Colores(principal: "azul"), actual.RowVersion), Actor, Ct);
        mal.Error!.Code.Should().Be(PortalMarcaErrores.Datos);

        var bien = await _admin.GuardarAsync(Organismo, new GuardarPortalMarcaRequest("Tránsito", Colores(principal: "#0b3d91"), actual.RowVersion), Actor, Ct);
        bien.Valor!.Draft.Colors!.Primary.Should().Be("#0B3D91");
    }

    [Fact]
    public async Task GuardarConUnaVersionVencidaRespondeConflicto409()
    {
        var primero = await GuardarValidoAsync();

        var r = await _admin.GuardarAsync(Organismo, new GuardarPortalMarcaRequest("Otro nombre", Colores(), primero.RowVersion - 1), Actor, Ct);

        r.Error!.Code.Should().Be(PortalMarcaErrores.Conflicto);
        r.Error.Status.Should().Be(409);
    }

    [Fact]
    public async Task PublicarConUnaVersionVencidaRespondeConflicto409YNoPublica()
    {
        var borrador = await GuardarValidoAsync();

        var r = await _admin.PublicarAsync(Organismo, borrador.RowVersion - 1, Actor, Ct);

        r.Error!.Code.Should().Be(PortalMarcaErrores.Conflicto);
        (await _admin.ObtenerAsync(Organismo, Ct)).Published.Should().BeNull();
    }

    // ── HU #14375 AC3 — retirar ──────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC3_RetirarDejaAlOrganismoConLaMarcaFlitYConservaElBorrador()
    {
        var borrador = await GuardarValidoAsync();
        var publicada = (await _admin.PublicarAsync(Organismo, borrador.RowVersion, Actor, Ct)).Valor!;

        var r = await _admin.RetirarAsync(Organismo, publicada.RowVersion, Actor, Ct);

        r.Error.Should().BeNull();
        r.Valor!.Published.Should().BeNull();
        r.Valor.PublishedBy.Should().BeNull();
        r.Valor.Draft.PlatformName.Should().Be("Tránsito de prueba", "el borrador se conserva");
        r.Valor.PublishedVersion.Should().Be(1, "la versión no baja al retirar");
    }

    // ── HU #14375 AC5 — auditoría ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC5_PublicarRegistraEnLaAuditoriaQuienYQueCambio()
    {
        var borrador = await GuardarValidoAsync();
        _auditoria.Entradas.Clear();

        await _admin.PublicarAsync(Organismo, borrador.RowVersion, Actor, Ct);

        var entrada = _auditoria.Entradas.Should().ContainSingle().Subject;
        entrada.ActorUserId.Should().Be(Actor);
        entrada.TargetEntityId.Should().Be(Organismo);
        entrada.EntityName.Should().Be("portal_marca_publicada");
        entrada.NewValue.Should().Contain("\"platformName\"").And.Contain("#0B3D91");
        entrada.OldValue.Should().BeNull("era la primera publicación");
    }

    [Fact]
    public async Task AC5_RetirarYGuardarTambienQuedanAuditados()
    {
        var borrador = await GuardarValidoAsync();
        var publicada = (await _admin.PublicarAsync(Organismo, borrador.RowVersion, Actor, Ct)).Valor!;
        await _admin.RetirarAsync(Organismo, publicada.RowVersion, Actor, Ct);

        _auditoria.Entradas.Select(e => e.EntityName).Should()
            .Equal("portal_marca_borrador", "portal_marca_publicada", "portal_marca_retirada");
    }

    // ── HU #14376 — imágenes ─────────────────────────────────────────────────────────────────────────────────────────

    // ── HU #14378 — entrega al portal ────────────────────────────────────────────────────────────────────────────────

    // ── Reglas puras ─────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ElContrasteMinimoEsElDeMarcaBlanca()
    {
        PortalMarcaReglas.ContrasteMinimo.Should().Be(new BrandingOptions().MinContrastRatio);
        PortalMarcaReglas.LogoMaxBytes.Should().Be(512 * 1024);
        PortalMarcaReglas.BannerMaxBytes.Should().Be(2 * 1024 * 1024);
    }
}
