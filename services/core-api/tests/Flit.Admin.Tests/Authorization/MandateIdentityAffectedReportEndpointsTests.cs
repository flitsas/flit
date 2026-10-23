using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Infrastructure.Persistence.Entities.Admin;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Flit.Admin.Tests.Authorization;

/// <summary>
/// HU #13247 (Feature #13245, Épica #13090) — <c>GET /api/v1/admin/mandate-signers/identity-validation-report</c> sobre
/// PostgreSQL real: solo el Super Admin (403 al resto), filtra por organismo, exporta CSV y trae compañía, organismo y correo
/// de los mandatarios con biometría sin validación propia aprobada. <para>Uso: Super Admin ⇒ 200
/// <c>{ data: [...], total: n }</c>.</para>
/// </summary>
public sealed class MandateIdentityAffectedReportEndpointsTests(WebApplicationFactory<Program> factory)
    : MandateSignerEndpointsTestBase(factory)
{
    private const string Url = "/api/v1/admin/mandate-signers/identity-validation-report";

    private async Task<Guid> SeedBiometriaSinValidacionAsync(string email)
    {
        await using var db = NewDb();
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        db.MandateSigners.Add(new MandateSigner
        {
            Id = id,
            TransitOfficeId = _officeA,
            FullName = "Ana Restrepo Reporte",
            DocumentType = "CC",
            DocumentNumber = $"6{Random.Shared.Next(10000000, 99999999)}",
            Email = email,
            IntegrityHash = new string('a', 64),
            RegisteredAt = now,
            IsActive = true,
            SignerModel = "natural",
            SignatureMethod = "biometria",
            ValidityKind = "fixed",
            CreatedAt = now,
        });
        db.MandateSignerTransitOffices.Add(new MandateSignerTransitOffice
        {
            Id = Guid.NewGuid(), MandateSignerId = id, TransitOfficeId = _officeA, IsActive = true, CreatedAt = now,
        });
        db.MandateSignerCompanies.Add(new MandateSignerCompany
        {
            Id = Guid.NewGuid(), MandateSignerId = id, TransitOfficeId = _officeA, CompanyTenantId = _companyA,
            IsActive = true, ConfiguredByScope = "compania", CreatedAt = now,
        });
        await db.SaveChangesAsync(Ct);
        _signerIds.Add(id);
        return id;
    }

    [Fact]
    public async Task SuperAdmin_RecibeLosAfectados_ConCompaniaOrganismoYCorreo()
    {
        var signer = await SeedBiometriaSinValidacionAsync("afectado@flit.test");
        AuthenticateSuperAdmin();

        var response = await _client.GetAsync($"{Url}?transitOfficeId={_officeA}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var fila = body.GetProperty("data").EnumerateArray()
            .Single(e => e.GetProperty("mandateSignerId").GetGuid() == signer);
        fila.GetProperty("email").GetString().Should().Be("afectado@flit.test");
        fila.GetProperty("companyTenantId").GetGuid().Should().Be(_companyA);
        fila.GetProperty("transitOfficeId").GetGuid().Should().Be(_officeA);
        fila.GetProperty("identityStatus").GetString().Should().Be("none");
        body.GetProperty("total").GetInt32().Should().Be(body.GetProperty("data").GetArrayLength());
    }

    [Fact]
    public async Task SuperAdmin_ExportaCsvConElCorreo()
    {
        await SeedBiometriaSinValidacionAsync("csv@flit.test");
        AuthenticateSuperAdmin();

        var response = await _client.GetAsync($"{Url}?transitOfficeId={_officeA}&format=csv", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        (await response.Content.ReadAsStringAsync(Ct)).Should().Contain("\"csv@flit.test\"");
    }

    [Theory]
    [InlineData("ot_admin")]
    [InlineData("AdminCompany")]
    public async Task ElResto_403(string rol)
    {
        await SeedBiometriaSinValidacionAsync("otro@flit.test");
        if (rol == "AdminCompany")
        {
            AuthenticateCompanyA();
        }
        else
        {
            AuthenticateOt(rol);
        }

        (await _client.GetAsync(Url, Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
