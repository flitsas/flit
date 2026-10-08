using Flit.Queries.Domain.Tenancy;
using Flit.Tramites.Application.UseCases.ConsolidadoLotes;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities.ConsolidadoLotes;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.ConsolidadoLotes;

/// <summary>
/// HU #13417 (épica #13216, ADR-0070 adenda v7) — <see cref="LoteAlcanceRedPolicy"/>: interpretación de
/// <c>alcanceRed</c> (raíz del cuerpo y, en transición, el filtro) y las puertas de la vista de red, reutilizadas en
/// el orden de <c>/network/**</c>: <see cref="NetworkScopePolicy.Validate"/> → <see cref="NetworkScopePolicy.ValidateRole"/>
/// → <see cref="NetworkScopePolicy.Narrow"/> → <see cref="NetworkDocumentsPolicy.ValidateKind"/>.
/// </summary>
/// <remarks>
/// Uso de ejemplo:
/// <code>
/// LoteAlcanceRedPolicy.TryInterpretar("red", null, out var pedido);                 // pedido = toda la red
/// var (alcance, error) = await LoteAlcanceRedPolicy.EvaluarAsync(scope, roles, pedido!, switches, ct);
/// </code>
/// </remarks>
public sealed class LoteAlcanceRedPolicyTests
{
    private static readonly Guid P = Guid.Parse("13417000-0000-4000-8000-0000000000a1");
    private static readonly Guid C1 = Guid.Parse("13417000-0000-4000-8000-0000000000c1");
    private static readonly Guid C2 = Guid.Parse("13417000-0000-4000-8000-0000000000c2");
    private static readonly Guid X = Guid.Parse("13417000-0000-4000-8000-0000000000f9");
    private static readonly string[] Admin = ["AdminCompany"];

    private readonly IHierarchySwitches _switches = Substitute.For<IHierarchySwitches>();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static TenantScope Red(GroupKind kind = GroupKind.MarcaBlanca) => TenantScope.Group(P, [C1, C2], kind);

    // ── AC9 — interpretación del cuerpo ─────────────────────────────────────────────────

    [Theory]
    [InlineData("red", null)]
    [InlineData(" RED ", null)]
    [InlineData(null, "red")]
    [InlineData("red", "red")]
    public void AC9_Red_EnLaRaizOEnElFiltro_EsTodaLaRed(string? raiz, string? filtro)
    {
        LoteAlcanceRedPolicy.TryInterpretar(raiz, filtro, out var pedido).Should().BeTrue();

        pedido.Should().NotBeNull();
        pedido!.HijaId.Should().BeNull("«red» es toda la red");
    }

    [Fact]
    public void AC9_UnUuidEsUnaHija_YEnLaRaizPrevaleceSobreUnFiltroVacio()
    {
        LoteAlcanceRedPolicy.TryInterpretar(C1.ToString(), "  ", out var pedido).Should().BeTrue();

        pedido!.HijaId.Should().Be(C1);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", "")]
    public void AC10_SinAlcance_EsPropio(string? raiz, string? filtro)
    {
        LoteAlcanceRedPolicy.TryInterpretar(raiz, filtro, out var pedido).Should().BeTrue();

        pedido.Should().BeNull("null = alcance propio, el lote de siempre");
    }

    [Theory]
    [InlineData("todos", null)]
    [InlineData(null, "propio")]
    [InlineData("00000000-0000-0000-0000-000000000000", null)]
    [InlineData("red", "13417000-0000-4000-8000-0000000000c1")]
    [InlineData("13417000-0000-4000-8000-0000000000c1", "13417000-0000-4000-8000-0000000000c2")]
    public void AC9_ValorInvalidoOContradictorio_NoSeInterpreta(string? raiz, string? filtro)
    {
        LoteAlcanceRedPolicy.TryInterpretar(raiz, filtro, out var pedido).Should().BeFalse();

        pedido.Should().BeNull();
    }

    // ── Puertas (AC4, AC6, AC7, AC8) ───────────────────────────────────────────────────

    [Fact]
    public async Task AC1_CabezaMarcaBlancaConRolAdmin_TodaLaRed_DevuelveElAlcanceDeGrupo_SinLeerElInterruptor()
    {
        var (alcance, error) = await LoteAlcanceRedPolicy.EvaluarAsync(
            Red(), Admin, new AlcanceRedPedido(null), _switches, Ct);

        error.Should().BeNull();
        alcance!.Alcance.ReadTenantIds.Should().BeEquivalentTo([P, C1, C2]);
        alcance.HijaId.Should().BeNull();
        alcance.Resumen.Should().Be("red");
        await _switches.DidNotReceiveWithAnyArgs().IsNetworkDocumentsConcesionEnabledAsync(Ct);
    }

    [Fact]
    public async Task AC2_UnaHija_AcotaElAlcanceASoloEsaHija()
    {
        var (alcance, error) = await LoteAlcanceRedPolicy.EvaluarAsync(
            Red(), Admin, new AlcanceRedPedido(C1), _switches, Ct);

        error.Should().BeNull();
        alcance!.Alcance.ReadTenantIds.Should().Equal([C1]);
        alcance.HijaId.Should().Be(C1);
        alcance.Resumen.Should().Be("hija");
    }

    [Fact]
    public async Task UuidDeLaPropiaCabeza_SeNormalizaAPropio()
    {
        var (alcance, error) = await LoteAlcanceRedPolicy.EvaluarAsync(
            Red(GroupKind.Concesion), Admin, new AlcanceRedPedido(P), _switches, Ct);

        error.Should().BeNull();
        alcance.Should().BeNull("la cabeza acotada a sí misma es el lote propio de siempre");
    }

    [Fact]
    public async Task AC4_HijaFueraDeLaRed_NetworkChildOutOfScope()
    {
        var (alcance, error) = await LoteAlcanceRedPolicy.EvaluarAsync(
            Red(), Admin, new AlcanceRedPedido(X), _switches, Ct);

        alcance.Should().BeNull();
        error.Should().Be(NetworkScopePolicy.ChildOutOfScope);
    }

    [Fact]
    public async Task AC6_CabezaSinRolAdmin_NetworkRoleRequired_AntesDeMirarLaHija()
    {
        var (_, error) = await LoteAlcanceRedPolicy.EvaluarAsync(
            Red(), ["Radicador", "Operador"], new AlcanceRedPedido(X), _switches, Ct);

        error.Should().Be(NetworkScopePolicy.RoleRequired, "la segunda puerta va antes de Narrow");
    }

    [Fact]
    public async Task AC7_ClienteSinRedOSuperAdminOSinAlcance_NetworkScopeRequired_AntesDelRol()
    {
        foreach (var scope in new TenantScope?[] { TenantScope.Single(P), null })
        {
            var (alcance, error) = await LoteAlcanceRedPolicy.EvaluarAsync(
                scope, [], new AlcanceRedPedido(null), _switches, Ct);

            alcance.Should().BeNull();
            error.Should().Be(NetworkScopePolicy.ScopeRequired, "la primera puerta va antes del rol");
        }
    }

    [Fact]
    public async Task AC8_CabezaConcesionConDocumentosDeRedApagados_NetworkDocumentsDisabled_EnRedYEnHija()
    {
        _switches.IsNetworkDocumentsConcesionEnabledAsync(Arg.Any<CancellationToken>()).Returns(false);

        foreach (var hija in new Guid?[] { null, C1 })
        {
            var (alcance, error) = await LoteAlcanceRedPolicy.EvaluarAsync(
                Red(GroupKind.Concesion), Admin, new AlcanceRedPedido(hija), _switches, Ct);

            alcance.Should().BeNull();
            error.Should().Be(NetworkDocumentsPolicy.DocumentsDisabled);
        }
    }

    [Fact]
    public async Task AC8_CabezaConcesionConDocumentosDeRedEncendidos_Permite()
    {
        _switches.IsNetworkDocumentsConcesionEnabledAsync(Arg.Any<CancellationToken>()).Returns(true);

        var (alcance, error) = await LoteAlcanceRedPolicy.EvaluarAsync(
            Red(GroupKind.Concesion), Admin, new AlcanceRedPedido(null), _switches, Ct);

        error.Should().BeNull();
        alcance!.Alcance.IsGroup.Should().BeTrue();
    }

    [Fact]
    public void Mensajes_SonTextoDeUsuarioSinCodigos()
    {
        foreach (var codigo in new[]
                 {
                     NetworkScopePolicy.ScopeRequired, NetworkScopePolicy.RoleRequired,
                     NetworkScopePolicy.ChildOutOfScope, NetworkDocumentsPolicy.DocumentsDisabled,
                 })
        {
            var texto = LoteAlcanceRedPolicy.Mensaje(codigo);
            texto.Should().NotBeNullOrWhiteSpace().And.NotContain(codigo);
        }
    }

    // ── LoteConsolidados.alcanceRed (solo lectura) ─────────────────────────────────────

    [Fact]
    public void ResumenDe_ElLoteDeRedEsRed_ElAcotadoEsHija_YElPropioNulo()
    {
        LoteAlcanceRed.ResumenDe(new ConsolidadoExportBatch { NetworkScope = true }).Should().Be("red");
        LoteAlcanceRed.ResumenDe(new ConsolidadoExportBatch { NetworkScope = true, ScopeTenantId = C1 }).Should().Be("hija");
        LoteAlcanceRed.ResumenDe(new ConsolidadoExportBatch()).Should().BeNull();
        LoteAlcanceRed.ResumenDe(new ConsolidadoExportBatch
        {
            Origin = ConsolidadoExportOrigin.Superadmin,
            ScopeTenantId = C1,
        }).Should().BeNull("el scope del Super Admin no es vista de red");
    }

    [Fact]
    public void ElAlcanceDeRedNoAdmiteElAlcanceTotalNiUnaHijaQueNoEsLaDelAlcance()
    {
        var conHijaAjena = () => new LoteAlcanceRed(TenantScope.Single(C1), C2);
        var hijaSinAcotar = () => new LoteAlcanceRed(Red(), C1);

        conHijaAjena.Should().Throw<ArgumentException>();
        hijaSinAcotar.Should().Throw<ArgumentException>();
    }
}
