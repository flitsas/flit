using System.Security.Claims;
using Flit.Api.Authorization;
using FluentAssertions;
using Xunit;

namespace Flit.Admin.Tests.Authorization;

/// <summary>HU #13145 (AC4) — solo el OT o el Super Admin fijan el mandatario; el gestor de la compañía no.</summary>
public sealed class MandateSignerEditPolicyTests
{
    private static ClaimsPrincipal User(params (string Type, string Value)[] claims) =>
        new(new ClaimsIdentity(claims.Select(c => new Claim(c.Type, c.Value)), "test"));

    [Fact]
    public void UsuarioDeLaCompania_NoPuedeFijarElMandatario()
    {
        MandateSignerEditPolicy.CanSet(User(
            ("role", "AdminCompany"), ("entity_type", "COMPANY"))).Should().BeFalse();
    }

    [Fact]
    public void Radicador_SinClaimsDeOt_NoPuedeFijarElMandatario()
    {
        MandateSignerEditPolicy.CanSet(User(("role", "Radicador"))).Should().BeFalse();
    }

    [Fact]
    public void UsuarioAnonimoSinClaims_NoPuede()
    {
        MandateSignerEditPolicy.CanSet(User()).Should().BeFalse();
    }

    [Theory]
    [InlineData("SuperAdmin")]
    [InlineData("ot_admin")]
    public void SuperAdminYOtAdmin_SiPueden(string rol)
    {
        MandateSignerEditPolicy.CanSet(User(("role", rol))).Should().BeTrue();
    }

    [Fact]
    public void CualquierUsuarioDeUnOrganismoDeTransito_SiPuede()
    {
        MandateSignerEditPolicy.CanSet(User(
            ("role", "OperadorOt"), ("entity_type", "TRANSIT_OFFICE"))).Should().BeTrue();
    }

    [Fact]
    public void ElCodigoDeErrorEsElDelContrato()
    {
        MandateSignerEditPolicy.ErrorCode.Should().Be("mandatario_no_editable_por_gestor");
    }
}
