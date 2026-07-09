using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using Flit.Infrastructure.Security;
using Flit.Modules.Security.Domain.Auth;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Flit.Infrastructure.Tests.Security;

/// <summary>
/// HU #10616 — claims nuevos emitidos por <see cref="RsaJwtTokenIssuer"/>: <c>company_name</c>,
/// <c>company_nit</c> y <c>entity_type</c> (AC1/AC2/AC4). Decodifica el JWT real (sin validar
/// firma, solo para inspeccionar claims) igual que haría un consumidor del token.
/// </summary>
public sealed class RsaJwtTokenIssuerTests
{
    private static RsaJwtTokenIssuer CreateIssuer()
    {
        var rsa = RSA.Create(2048);
        var keyMaterial = new JwtKeyMaterial
        {
            SigningKey = new RsaSecurityKey(rsa),
            Issuer = "https://api.flit.co",
            Audience = "flit-api",
        };
        var options = Options.Create(new JwtSettings());
        return new RsaJwtTokenIssuer(keyMaterial, options);
    }

    private static JwtSecurityToken Decode(string jwt) =>
        new JwtSecurityTokenHandler().ReadJwtToken(jwt);

    [Fact]
    public void IssueToken_CompanyTenant_EmitsCompanyNameNitAndEntityType()
    {
        // AC1 — tenant tipo Compañía con NIT registrado.
        var issuer = CreateIssuer();
        var roles = new List<UserRoleSnapshot> { new(Guid.NewGuid(), "AdminCompany") };

        var issued = issuer.IssueToken(
            Guid.NewGuid(), "admin@empresa.com", Guid.NewGuid(),
            "Acme Renting SAS", "900123456-7", "COMPANY",
            roles, ["companies.read"]);

        var token = Decode(issued.Token);
        token.Claims.Single(c => c.Type == "company_name").Value.Should().Be("Acme Renting SAS");
        token.Claims.Single(c => c.Type == "company_nit").Value.Should().Be("900123456-7");
        token.Claims.Single(c => c.Type == "entity_type").Value.Should().Be("COMPANY");
    }

    [Fact]
    public void IssueToken_TransitOfficeTenant_EmitsTransitOfficeEntityType()
    {
        // AC2 — tenant tipo Organismo de Tránsito con NIT registrado.
        var issuer = CreateIssuer();
        var roles = new List<UserRoleSnapshot> { new(Guid.NewGuid(), "ot_admin") };

        var issued = issuer.IssueToken(
            Guid.NewGuid(), "admin@ot.gov.co", Guid.NewGuid(),
            "Organismo de Tránsito Norte", "800987654-1", "TRANSIT_OFFICE",
            roles, ["ot.read"]);

        var token = Decode(issued.Token);
        token.Claims.Single(c => c.Type == "company_name").Value.Should().Be("Organismo de Tránsito Norte");
        token.Claims.Single(c => c.Type == "company_nit").Value.Should().Be("800987654-1");
        token.Claims.Single(c => c.Type == "entity_type").Value.Should().Be("TRANSIT_OFFICE");
    }

    [Fact]
    public void IssueToken_TenantWithoutTaxId_EmitsEmptyCompanyNitWithoutThrowing()
    {
        // AC4 — tenant sin NIT registrado: no debe romper la emisión del token.
        var issuer = CreateIssuer();
        var roles = new List<UserRoleSnapshot> { new(Guid.NewGuid(), "AdminCompany") };

        var act = () => issuer.IssueToken(
            Guid.NewGuid(), "admin@sinnit.com", Guid.NewGuid(),
            "Tenant Legacy Sin NIT", string.Empty, "COMPANY",
            roles, []);

        var issued = act.Should().NotThrow().Subject;
        var token = Decode(issued.Token);
        token.Claims.Single(c => c.Type == "company_nit").Value.Should().BeEmpty();
    }

    [Fact]
    public void IssueToken_MultipleRoles_EmitsRoleClaimsAndUnionOfPermissions()
    {
        // HU #10506 (ya implementada) — se re-verifica de paso que el emisor sigue soportando
        // multi-rol al pasar por los nuevos parámetros sin romper el comportamiento existente.
        var issuer = CreateIssuer();
        var roleAId = Guid.NewGuid();
        var roleBId = Guid.NewGuid();
        var roles = new List<UserRoleSnapshot> { new(roleAId, "gestor"), new(roleBId, "radicador") };

        var issued = issuer.IssueToken(
            Guid.NewGuid(), "multi@empresa.com", Guid.NewGuid(),
            "Acme Renting SAS", "900123456-7", "COMPANY",
            roles, ["procedures.read", "procedures.write"]);

        var token = Decode(issued.Token);
        token.Claims.Where(c => c.Type == "role_id").Select(c => c.Value)
            .Should().BeEquivalentTo([roleAId.ToString(), roleBId.ToString()]);
        token.Claims.Where(c => c.Type == "role_code").Select(c => c.Value)
            .Should().BeEquivalentTo(["gestor", "radicador"]);
    }
}
