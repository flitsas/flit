using Flit.Modules.Quipux.Application.UseCases.MapeoTipoTramite;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Modules.Quipux.Application.Tests.MapeoTipoTramite;

/// <summary>
/// ADR-0050 — la parametrización Quipux del tipo se configura, no se despliega.
///
/// <para>Vivía solo en un DDL, así que dar de alta un trámite en la secretaría exigía una migración.
/// Ahora se edita desde el configurador y se guarda en <c>external_refs</c>, junto al resto de la
/// parametrización del tipo.</para>
///
/// <para>La validación es la MISMA que aplica el worker al radicar: si el bloque no sobrevive al
/// <c>Parse</c>, no se guarda. Un bloque a medias no radicaría mal —la ausencia de mapeo utilizable
/// es el gate de elegibilidad— pero dejaría al administrador creyendo que configuró algo que el
/// worker descarta en silencio.</para>
/// </summary>
public sealed class ConfigurarMapeoQuipuxTests
{
    private readonly IProcedureTypeRepository _repo = Substitute.For<IProcedureTypeRepository>();

    private ProcedureType Tipo(string externalRefs = "{}")
    {
        var tipo = new ProcedureType
        {
            Id = Guid.NewGuid(),
            Code = "BLINDAJE",
            Name = "Blindaje",
            Family = "OTROS",
            ExternalRefs = externalRefs,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _repo.GetByIdAsync(tipo.Id, Arg.Any<CancellationToken>()).Returns(tipo);
        return tipo;
    }

    private static MapeoQuipuxDto Valido() =>
        new("OTROS", 42, 51, "BL", "plate", null, 35);

    [Fact]
    public async Task UnMapeoCompletoSeGuardaYSeVuelveAleer()
    {
        var tipo = Tipo();

        var (guardado, error) = await new GuardarMapeoQuipuxHandler(_repo)
            .HandleAsync(tipo.Id, Valido(), TestContext.Current.CancellationToken);

        error.Should().BeNull();
        guardado.Should().NotBeNull();

        var (leido, _) = await new ObtenerMapeoQuipuxHandler(_repo)
            .HandleAsync(tipo.Id, TestContext.Current.CancellationToken);

        leido!.TipoTramite.Should().Be(42);
        leido.Prefijo.Should().Be("BL");
        leido.CampoPlaca.Should().Be("plate");
        leido.CampoVin.Should().BeNull();
    }

    [Theory]
    [InlineData("VEHICULAR", 42, 51, "BL", 35)]   // familia fuera del vocabulario de la secretaría
    [InlineData("OTROS", 0, 51, "BL", 35)]        // sin código de trámite
    [InlineData("OTROS", 42, 0, "BL", 35)]        // sin código de requisito
    [InlineData("OTROS", 42, 51, "", 35)]         // sin prefijo
    [InlineData("OTROS", 42, 51, "BL", 0)]        // sin tope de empresa
    public async Task UnMapeoAMediasNoSeGuarda(
        string familia, int tipoTramite, int tipoRequisito, string prefijo, int maxEmpresa)
    {
        var tipo = Tipo();

        var (_, error) = await new GuardarMapeoQuipuxHandler(_repo).HandleAsync(
            tipo.Id,
            new MapeoQuipuxDto(familia, tipoTramite, tipoRequisito, prefijo, "plate", null, maxEmpresa),
            TestContext.Current.CancellationToken);

        error.Should().Be(GuardarMapeoQuipuxHandler.NoUtilizable);
        tipo.ExternalRefs.Should().Be("{}", "no se persiste un bloque que el worker descartaría");
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ElPrefijoYLaFamiliaSeNormalizanAMayusculas()
    {
        var tipo = Tipo();

        await new GuardarMapeoQuipuxHandler(_repo).HandleAsync(
            tipo.Id,
            new MapeoQuipuxDto(" otros ", 42, 51, " bl ", "plate", null, 35),
            TestContext.Current.CancellationToken);

        var (leido, _) = await new ObtenerMapeoQuipuxHandler(_repo)
            .HandleAsync(tipo.Id, TestContext.Current.CancellationToken);

        leido!.Familia.Should().Be("OTROS");
        leido.Prefijo.Should().Be("BL");
    }

    [Fact]
    public async Task RetirarElMapeoDejaElTipoSinRadicar()
    {
        var tipo = Tipo("""{"quipux":{"familia":"OTROS","tipoTramite":42,"tipoRequisito":51,"prefijo":"BL","campoPlaca":"plate","campoVin":null,"maxLongitudEmpresa":35}}""");

        var (_, error) = await new GuardarMapeoQuipuxHandler(_repo)
            .HandleAsync(tipo.Id, null, TestContext.Current.CancellationToken);

        error.Should().BeNull();
        tipo.ExternalRefs.Should().NotContain("quipux");

        var (leido, _) = await new ObtenerMapeoQuipuxHandler(_repo)
            .HandleAsync(tipo.Id, TestContext.Current.CancellationToken);
        leido.Should().BeNull("sin bloque, el tipo simplemente no se radica");
    }

    [Fact]
    public async Task LaVarianteHeredadaSeConserva()
    {
        // El formulario no la ofrece —era para cuando el leasing era una casilla dentro de la
        // matrícula—, pero sobrescribirla en silencio cambiaría cómo se radica el tipo canónico.
        var tipo = Tipo("""{"quipux":{"familia":"TRASPASO","tipoTramite":16,"tipoRequisito":51,"prefijo":"TR","campoPlaca":"plate","campoVin":null,"maxLongitudEmpresa":35,"variante":{"campo":"es_unilateral","cuandoVerdadero":{"tipoTramite":213,"prefijo":"TRU"}}}}""");

        await new GuardarMapeoQuipuxHandler(_repo).HandleAsync(
            tipo.Id,
            new MapeoQuipuxDto("TRASPASO", 16, 51, "TR", "plate", null, 40),
            TestContext.Current.CancellationToken);

        tipo.ExternalRefs.Should().Contain("es_unilateral");
        tipo.ExternalRefs.Should().Contain("213");
        tipo.ExternalRefs.Should().Contain("40", "el campo editado sí cambia");
    }

    [Fact]
    public async Task OtrasClavesDeExternalRefsNoSeTocan()
    {
        var tipo = Tipo("""{"ict":{"transactionType":5},"quipux":null}""");

        await new GuardarMapeoQuipuxHandler(_repo)
            .HandleAsync(tipo.Id, Valido(), TestContext.Current.CancellationToken);

        tipo.ExternalRefs.Should().Contain("transactionType",
            "external_refs es el punto central: editar una integración no puede borrar otra");
    }

    [Fact]
    public async Task UnTipoQueNoExisteSeReporta()
    {
        _repo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((ProcedureType?)null);

        var (_, error) = await new GuardarMapeoQuipuxHandler(_repo)
            .HandleAsync(Guid.NewGuid(), Valido(), TestContext.Current.CancellationToken);

        error.Should().Be("not_found");
    }
}
