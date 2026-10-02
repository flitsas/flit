using Flit.Admin.Application.Companies.MandateSigners;
using Flit.Admin.Domain.Companies.MandateSigners;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Companies.MandateSigners;

/// <summary>
/// HU #13134 (Feature #13115) — regla ÚNICA de permisos por origen del mandatario: un mandatario del organismo de
/// tránsito solo lo modifican el Admin OT y el Super Admin; uno de la compañía también el Admin de Compañía. Sin
/// base de datos: la regla pura y la guarda que la aplica.
/// </summary>
public sealed class MandateSignerOriginRulesTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(MandateSignerActorKind.SuperAdmin, "organismo", true)]
    [InlineData(MandateSignerActorKind.SuperAdmin, "compania", true)]
    [InlineData(MandateSignerActorKind.OtAdmin, "organismo", true)]
    [InlineData(MandateSignerActorKind.OtAdmin, "compania", true)]
    [InlineData(MandateSignerActorKind.CompanyAdmin, "organismo", false)]
    [InlineData(MandateSignerActorKind.CompanyAdmin, "compania", true)]
    [InlineData(MandateSignerActorKind.None, "organismo", false)]
    [InlineData(MandateSignerActorKind.None, "compania", false)]
    public void CanModify_aplica_la_matriz_rol_por_origen(MandateSignerActorKind actor, string origin, bool esperado) =>
        MandateSignerOriginRules.CanModify(actor, origin).Should().Be(esperado);

    [Theory]
    [InlineData("organismo", "organismo")]
    [InlineData("super_admin", "organismo")]
    [InlineData("compania", "compania")]
    public void GroupOf_agrupa_super_admin_con_el_organismo(string scope, string grupo) =>
        MandateSignerOriginRules.GroupOf(scope).Should().Be(grupo);

    [Fact]
    public void OriginOf_basta_un_vinculo_del_organismo_para_que_el_mandatario_sea_del_organismo()
    {
        MandateSignerOriginRules.OriginOf(["compania", "organismo"]).Should().Be("organismo");
        MandateSignerOriginRules.OriginOf(["compania", "super_admin"]).Should().Be("organismo");
        MandateSignerOriginRules.OriginOf(["compania", "compania"]).Should().Be("compania");
    }

    [Fact]
    public void OriginOf_sin_vinculos_se_asume_del_organismo() =>
        MandateSignerOriginRules.OriginOf([]).Should().Be("organismo");

    [Fact]
    public async Task Guard_rechaza_al_rol_sin_permiso_sin_consultar_si_el_mandatario_existe()
    {
        var reader = Substitute.For<IMandateSignerReader>();
        var guard = new MandateSignerAccessGuard(reader);

        var access = await guard.CheckCompanyWriteAsync(MandateSignerActorKind.None, Guid.NewGuid(), Guid.NewGuid(), Ct);

        access.Should().Be(MandateSignerAccess.Forbidden);
        await reader.DidNotReceive().GetOriginForCompanyAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null, MandateSignerActorKind.CompanyAdmin, MandateSignerAccess.NotFound)]
    [InlineData("organismo", MandateSignerActorKind.CompanyAdmin, MandateSignerAccess.LockedByOtOrigin)]
    [InlineData("compania", MandateSignerActorKind.CompanyAdmin, MandateSignerAccess.Allowed)]
    [InlineData("organismo", MandateSignerActorKind.SuperAdmin, MandateSignerAccess.Allowed)]
    public async Task Guard_decide_por_origen_y_rol(string? origen, MandateSignerActorKind actor, MandateSignerAccess esperado)
    {
        var reader = Substitute.For<IMandateSignerReader>();
        reader.GetOriginForCompanyAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(origen);

        var access = await new MandateSignerAccessGuard(reader)
            .CheckCompanyWriteAsync(actor, Guid.NewGuid(), Guid.NewGuid(), Ct);

        access.Should().Be(esperado);
    }
}
