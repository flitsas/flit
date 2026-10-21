using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Flit.Api.Authorization;
using Flit.Modules.Security.Domain.Roles;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Integration;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Authorization;

/// <summary>
/// HU #13664 (Feature #13636 F-F, Épica #12753) — Ver/Crear por familia de trámite detrás de
/// <c>Security:ModulePermissionEnforcement:Enabled</c>. El gate se prueba directo (AC2/AC3) y la ruta de carga masiva por HTTP
/// (la familia sale de la plantilla, sin depender de la base): sin «Crear» de la familia → 403.
/// <para>Uso de ejemplo: <c>Gate(true, "Radicador", "matriculas.read").CanCreate(ProcedureFamily.Matriculas)</c> es false (Ver sin Crear).</para>
/// </summary>
public sealed class FamilyPermissionEnforcementTests : IClassFixture<TramitesCreatePermissionEnforcementTests.Hosts>
{
    private static readonly Guid Tenant = Guid.Parse("0c000000-0000-4000-8000-000000013664");
    private readonly WebApplicationFactory<Program> _apagado;
    private readonly WebApplicationFactory<Program> _encendido;

    public FamilyPermissionEnforcementTests(TramitesCreatePermissionEnforcementTests.Hosts hosts)
    {
        _apagado = hosts.Apagado;
        _encendido = hosts.Encendido;
    }

    private static HttpProcedureFamilyPermissionGate Gate(bool enabled, string role, params string[] permisos)
    {
        var claims = new List<Claim> { new("role_code", role) };
        claims.AddRange(permisos.Select(p => new Claim("permissions", p)));
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) };
        return Build(enabled, http);
    }

    private static HttpProcedureFamilyPermissionGate Build(bool enabled, HttpContext? http)
    {
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(http);
        var options = Substitute.For<IOptionsMonitor<ModulePermissionEnforcementOptions>>();
        options.CurrentValue.Returns(new ModulePermissionEnforcementOptions { Enabled = enabled });
        return new HttpProcedureFamilyPermissionGate(accessor, options);
    }

    [Fact]
    public void LosSlugsDeLaReglaYDelGateCoinciden()
    {
        var delGate = new[] { ProcedureFamily.Matriculas, ProcedureFamily.Traspaso, ProcedureFamily.Otros }
            .Select(f => (FamilyPermissionSlugs.Read(f), FamilyPermissionSlugs.Create(f)));

        FamilyPermissionRules.Pairs.Should().BeEquivalentTo(delGate);
    }

    // Flag apagado: comportamiento idéntico al actual (sin permisos individuales todo pasa).
    [Fact]
    public void FlagApagado_TodoPermitido_SinRestriccionDeListados()
    {
        var gate = Gate(false, "Radicador");

        gate.IsActive.Should().BeFalse();
        gate.CanCreate(ProcedureFamily.Matriculas).Should().BeTrue();
        gate.CanRead(ProcedureFamily.Otros).Should().BeTrue();
        gate.ReadableFamilyCodes().Should().BeNull();
    }

    // AC2: matriculas.read sin matriculas.create → no crea Matrículas (sí las otras con su Crear).
    [Fact]
    public void AC2_VerSinCrear_NoCreaDeEsaFamilia()
    {
        var gate = Gate(true, "Radicador", "matriculas.read", "traspasos.read", "traspasos.create");

        gate.CanRead(ProcedureFamily.Matriculas).Should().BeTrue();
        gate.CanCreate(ProcedureFamily.Matriculas).Should().BeFalse();
        gate.CanCreate(ProcedureFamily.Traspaso).Should().BeTrue();
        gate.CanCreate(ProcedureFamily.Otros).Should().BeFalse();
    }

    // AC3: sin otros-tramites.read no ve la familia OTROS (listados: solo las familias con .read).
    [Fact]
    public void AC3_SinVerOtros_NoLaVe_YElListadoSeRestringe()
    {
        var gate = Gate(true, "Radicador", "matriculas.read", "traspasos.read");

        gate.CanRead(ProcedureFamily.Otros).Should().BeFalse();
        gate.ReadableFamilyCodes().Should().BeEquivalentTo(["MATRICULAS", "TRASPASO"]);
    }

    [Fact]
    public void ConLasTresFamilias_ElListadoNoSeRestringe_YSinNingunaQuedaVacio()
    {
        Gate(true, "Radicador", "matriculas.read", "traspasos.read", "otros-tramites.read").ReadableFamilyCodes().Should().BeNull();
        Gate(true, "Radicador").ReadableFamilyCodes().Should().BeEmpty();
    }

    [Fact]
    public void SuperAdmin_ConservaElBypass()
    {
        var gate = Gate(true, "SuperAdmin");

        gate.CanCreate(ProcedureFamily.Otros).Should().BeTrue();
        gate.ReadableFamilyCodes().Should().BeNull();
    }

    [Fact]
    public void SinUsuarioHttp_OGrpc_NoSeExige()
    {
        Build(true, null).CanCreate(ProcedureFamily.Matriculas).Should().BeTrue();

        var grpc = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("role_code", "ict")], "test")) };
        var method = new global::Grpc.Core.Method<string, string>(global::Grpc.Core.MethodType.Unary, "ict", "Crear",
            global::Grpc.Core.Marshallers.StringMarshaller, global::Grpc.Core.Marshallers.StringMarshaller);
        grpc.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(
            new global::Grpc.AspNetCore.Server.GrpcMethodMetadata(typeof(object), method)), "grpc"));
        Build(true, grpc).CanCreate(ProcedureFamily.Matriculas).Should().BeTrue();
    }

    // Un cliente REST no se exime enviando Content-Type: application/grpc (el header lo controla el cliente).
    [Fact]
    public void ContentTypeGrpc_EnRutaRest_SigueExigiendo()
    {
        var rest = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("role_code", "Radicador")], "test")) };
        rest.Request.ContentType = "application/grpc";

        Build(true, rest).CanRead(ProcedureFamily.Matriculas).Should().BeFalse();
        Build(true, rest).ReadableFamilyCodes().Should().BeEmpty();
    }

    // HTTP: la plantilla fija la familia del lote (matricula/traspaso/otros) → 403 sin el «Crear» de esa familia.
    [Theory]
    [InlineData("matricula", "matriculas.create", "traspasos.create")]
    [InlineData("traspaso", "traspasos.create", "matriculas.create")]
    [InlineData("otros", "otros-tramites.create", "matriculas.create")]
    public async Task AC2_CargaMasiva_FlagEncendido_SinCrearDeLaFamilia_403_ConElloNo(string tipo, string propio, string ajeno)
    {
        var sin = await Llamar(_encendido, tipo, Token("tramites.create", "matriculas.read", "traspasos.read", "otros-tramites.read", ajeno));
        var con = await Llamar(_encendido, tipo, Token("tramites.create", propio));

        sin.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        con.StatusCode.Should().NotBe(HttpStatusCode.Forbidden).And.NotBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CargaMasiva_FlagApagado_SinPermisosPorFamilia_NoRecibe403()
    {
        var response = await Llamar(_apagado, "matricula", Token("tramites.create"));

        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden).And.NotBe(HttpStatusCode.Unauthorized);
    }

    private static async Task<HttpResponseMessage> Llamar(WebApplicationFactory<Program> factory, string tipo, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/tramites/carga-masiva/lotes?tipo={tipo}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var form = new MultipartFormDataContent { { new ByteArrayContent([1, 2, 3]), "archivo", "lote.xlsx" } };
        request.Content = form;
        return await factory.CreateClient().SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static string Token(params string[] permisos)
    {
        var claims = new List<Claim>
        {
            new("sub", "13664000-0000-4000-8000-000000000001"),
            new("role", "Radicador"),
            new("role_code", "Radicador"),
            new("tenant_id", Tenant.ToString()),
            new("entity_type", "COMPANY"),
        };
        claims.AddRange(permisos.Select(p => new Claim("permissions", p)));

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "https://api.flit.co",
            Audience = "flit-api",
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(new string('k', 64))), SecurityAlgorithms.HmacSha256),
        });
    }
}
