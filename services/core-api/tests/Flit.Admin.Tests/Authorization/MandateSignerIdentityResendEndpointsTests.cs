using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Admin.Domain.Companies.MandateSigners;
using Flit.Admin.Tests.Companies.MandateSigners;
using Flit.Infrastructure.Persistence.Entities.Admin;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Flit.Admin.Tests.Authorization;

/// <summary>
/// HU #13246 (Feature #13245, Épica #13090) — rutas NUEVAS de «Reenviar validación» del mandatario
/// (<c>identity-validation/resend</c>, compañía y hub OT) sobre PostgreSQL real, con el puerto del lanzador sustituido por un
/// doble. Verifican permisos (permiso de gestión de mandatarios y candado por origen), los códigos HTTP y que las tres rutas
/// retiradas <c>identity/send|resend|link</c> siguen respondiendo 410 (AC8). <para>Uso: <c>POST
/// /api/v1/admin/transit-offices/{ot}/mandate-signers/{id}/identity-validation/resend</c> ⇒ 200 <c>{ identity: "sent" }</c>.</para>
/// </summary>
public sealed class MandateSignerIdentityResendEndpointsTests(WebApplicationFactory<Program> factory)
    : MandateSignerEndpointsTestBase(WithStubLauncher(factory, Launcher))
{
    private static readonly StubMandateSignerIdentityLauncher Launcher = new();

    private static WebApplicationFactory<Program> WithStubLauncher(
        WebApplicationFactory<Program> factory, StubMandateSignerIdentityLauncher stub) =>
        factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
            s.Replace(ServiceDescriptor.Scoped<IMandateSignerIdentityLauncher>(_ => stub))));

    private async Task<Guid> SeedNaturalAsync(
        string method = "biometria", string? email = "mandatario@flit.test", string scope = "compania",
        string model = "natural", Guid? company = null)
    {
        await using var db = NewDb();
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        db.MandateSigners.Add(new MandateSigner
        {
            Id = id,
            TransitOfficeId = _officeA,
            FullName = "Ana Restrepo",
            DocumentType = "CC",
            DocumentNumber = $"7{Random.Shared.Next(10000000, 99999999)}",
            Email = email,
            IntegrityHash = new string('a', 64),
            RegisteredAt = now,
            IsActive = true,
            SignerModel = model,
            SignatureMethod = model == "natural" ? method : null,
            ValidityKind = "fixed",
            CreatedAt = now,
        });
        db.MandateSignerTransitOffices.Add(new MandateSignerTransitOffice
        {
            Id = Guid.NewGuid(), MandateSignerId = id, TransitOfficeId = _officeA, IsActive = true, CreatedAt = now,
        });
        db.MandateSignerCompanies.Add(new MandateSignerCompany
        {
            Id = Guid.NewGuid(), MandateSignerId = id, TransitOfficeId = _officeA, CompanyTenantId = company ?? _companyA,
            IsActive = true, ConfiguredByScope = scope, CreatedAt = now,
        });
        await db.SaveChangesAsync(Ct);
        _signerIds.Add(id);
        return id;
    }

    [Fact]
    public async Task HubOt_ReenviarConBiometria_200Sent_YLanzaEnElTenantDeLaCompania()
    {
        var signer = await SeedNaturalAsync();
        Launcher.Calls.Clear();
        Launcher.Outcome = MandateSignerIdentityLaunchOutcome.Sent;
        AuthenticateSuperAdmin(); // el OT no gestiona mandatarios de compañías

        var response = await _client.PostAsync(Hub(signer, "/identity-validation/resend"), null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("identity").GetString().Should().Be("sent");
        var call = Launcher.Calls.Should().ContainSingle().Subject;
        call.MandateSignerId.Should().Be(signer);
        call.TenantId.Should().Be(_companyA).And.NotBe(_otTenant);
    }

    [Fact]
    public async Task Compania_ReenviarConBiometria_Cola_200Queued()
    {
        var signer = await SeedNaturalAsync();
        Launcher.Calls.Clear();
        Launcher.Outcome = MandateSignerIdentityLaunchOutcome.Queued;
        AuthenticateCompanyA();

        var response = await _client.PostAsync(Company(_companyA, signer, "/identity-validation/resend"), null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("identity").GetString().Should().Be("queued");
        Launcher.Calls.Should().ContainSingle().Which.TenantId.Should().Be(_companyA);
    }

    [Fact]
    public async Task ReenviarConBaul_409_MandatarioNoRequiereValidacion()
    {
        var signer = await SeedNaturalAsync(method: "baul");
        AuthenticateSuperAdmin(); // el OT no gestiona mandatarios de compañías

        var response = await _client.PostAsync(Hub(signer, "/identity-validation/resend"), null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString()
            .Should().Be("mandatario_no_requiere_validacion");
    }

    [Fact]
    public async Task ReenviarJuridica_409_MandatarioNoRequiereValidacion()
    {
        var signer = await SeedNaturalAsync(model: "juridica", email: null);
        AuthenticateSuperAdmin(); // el OT no gestiona mandatarios de compañías

        (await _client.PostAsync(Hub(signer, "/identity-validation/resend"), null, Ct)).StatusCode
            .Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task ReenviarSinCorreo_422ConElCampoEmail()
    {
        var signer = await SeedNaturalAsync(email: null);
        AuthenticateSuperAdmin(); // el OT no gestiona mandatarios de compañías

        var response = await _client.PostAsync(Hub(signer, "/identity-validation/resend"), null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errors")[0].GetProperty("field").GetString()
            .Should().Be("email");
    }

    [Fact]
    public async Task ReenviarUnEliminado_404()
    {
        var signer = await SeedNaturalAsync();
        await using (var db = NewDb())
        {
            await db.MandateSigners.Where(s => s.Id == signer)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.DeletedAt, DateTimeOffset.UtcNow), Ct);
        }

        AuthenticateOt();

        (await _client.PostAsync(Hub(signer, "/identity-validation/resend"), null, Ct)).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ElProveedorRechazaDeFormaDefinitiva_502()
    {
        var signer = await SeedNaturalAsync();
        Launcher.Outcome = MandateSignerIdentityLaunchOutcome.Failed;
        AuthenticateSuperAdmin(); // el OT no gestiona mandatarios de compañías

        var response = await _client.PostAsync(Hub(signer, "/identity-validation/resend"), null, Ct);
        Launcher.Outcome = MandateSignerIdentityLaunchOutcome.Sent;

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
    }

    [Fact]
    public async Task UnGestorDelOt_NoPuedeReenviar_403()
    {
        // Mismo permiso de gestión que editar: el rol «gestor» del hub no escribe sobre mandatarios.
        var signer = await SeedNaturalAsync();
        AuthenticateOt("ot_gestor");

        (await _client.PostAsync(Hub(signer, "/identity-validation/resend"), null, Ct)).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task LaCompania_NoReenviaLoQueConfiguroElOrganismo_403Candado()
    {
        var signer = await SeedNaturalAsync(scope: "organismo");
        Launcher.Calls.Clear();
        AuthenticateCompanyA();

        var response = await _client.PostAsync(Company(_companyA, signer, "/identity-validation/resend"), null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Launcher.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task OtraCompania_NoReenvia_404()
    {
        var signer = await SeedNaturalAsync();
        AuthenticateCompanyB();

        (await _client.PostAsync(Company(_companyB, signer, "/identity-validation/resend"), null, Ct)).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("send")]
    [InlineData("resend")]
    [InlineData("link")]
    public async Task AC8_LasRutasRetiradasIdentitySendResendLink_SiguenEn410(string accion)
    {
        var signer = await SeedNaturalAsync();
        AuthenticateOt();
        var hub = await _client.PostAsync(Hub(signer, $"/identity/{accion}"), null, Ct);
        AuthenticateCompanyA();
        var compania = await _client.PostAsync(Company(_companyA, signer, $"/identity/{accion}"), null, Ct);

        hub.StatusCode.Should().Be(HttpStatusCode.Gone);
        compania.StatusCode.Should().Be(HttpStatusCode.Gone);
        (await hub.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errors")[0].GetProperty("code").GetString()
            .Should().Be("endpoint_deprecado");
    }
}
