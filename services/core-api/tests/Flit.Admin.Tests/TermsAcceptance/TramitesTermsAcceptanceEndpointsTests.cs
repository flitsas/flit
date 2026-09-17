using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Admin.Application.Auditing;
using Flit.Admin.Tests.Companies;
using Flit.Infrastructure.Persistence;
using Flit.Tramites.Application.UseCases.TermsAcceptance;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Admin.Tests.TermsAcceptance;

/// <summary>
/// Epic #12543 — <c>POST /api/v1/tramites/terms-acceptances</c>: autenticación, validación del
/// tipo, y que la aceptación quede escrita con user_id, accepted_at UTC, tramite_type e IP (§5)
/// tanto en la tabla de evidencia como reflejada en el rastro unificado. Corre contra la base de
/// desarrollo que levanta <see cref="WebApplicationFactory{TEntryPoint}"/>; cada prueba borra lo
/// que insertó.
/// </summary>
public sealed class TramitesTermsAcceptanceEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Url = "/api/v1/tramites/terms-acceptances";

    private readonly WebApplicationFactory<Program> _factory;

    public TramitesTermsAcceptanceEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Post_SinToken_Returns401()
    {
        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(Url, new { procedureTypeCode = "BLINDAJE" }, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetCurrent_DevuelveLaUrlDelDocumento()
    {
        using var client = Client(TestTokenFactory.CreateAdminCompanyToken(await AnyCompanyTenantAsync()));
        var response = await client.GetAsync(Url + "/current", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        doc.RootElement.GetProperty("url").GetString().Should().Be(ProcedureTermsOptions.DefaultUrl);
    }

    [Fact]
    public async Task Post_SinCode_Returns400_YNoEscribeNada()
    {
        var user = await AnyUserAsync();
        using var client = Client(TestTokenFactory.CreateAdminCompanyToken(await AnyCompanyTenantAsync(), user));

        var antes = await CountByUserAsync(user);

        var response = await client.PostAsJsonAsync(Url, new { procedureTypeCode = "" }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CountByUserAsync(user)).Should().Be(antes);
    }

    [Fact]
    public async Task Post_ConCodeInexistente_Returns422_YNoEscribeNada()
    {
        var user = await AnyUserAsync();
        using var client = Client(TestTokenFactory.CreateAdminCompanyToken(await AnyCompanyTenantAsync(), user));

        var antes = await CountByUserAsync(user);

        var response = await client.PostAsJsonAsync(Url, new { procedureTypeCode = "NO_EXISTE_12543" }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await CountByUserAsync(user)).Should().Be(antes);
    }

    [Fact]
    public async Task Post_UsuarioDeCompania_Returns201_YDejaEvidenciaConTenantDelToken_IpYReflejoEnAuditoria()
    {
        var tenant = await AnyCompanyTenantAsync();
        var user = await AnyUserAsync();
        using var client = Client(TestTokenFactory.CreateAdminCompanyToken(tenant, user));
        // El tenant del header se ignora para el usuario de compañía: manda el del JWT.
        client.DefaultRequestHeaders.Add("X-Tenant-Id", Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "190.24.1.9, 10.0.0.1");
        client.DefaultRequestHeaders.UserAgent.ParseAdd("FlitTests/1.0");
        var antes = DateTimeOffset.UtcNow.AddSeconds(-1);

        try
        {
            var response = await client.PostAsJsonAsync(Url, new { procedureTypeCode = "BLINDAJE" }, TestContext.Current.CancellationToken);

            response.StatusCode.Should().Be(HttpStatusCode.Created);
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            var id = doc.RootElement.GetProperty("id").GetGuid();
            doc.RootElement.GetProperty("procedureTypeCode").GetString().Should().Be("BLINDAJE");
            doc.RootElement.GetProperty("termsUrl").GetString().Should().Be(ProcedureTermsOptions.DefaultUrl);
            response.Headers.Location!.ToString().Should().EndWith($"{Url}/{id}");

            await using var db = CreateDbContext();
            var fila = await db.ProcedureTermsAcceptances.SingleAsync(x => x.Id == id, TestContext.Current.CancellationToken);
            fila.UserId.Should().Be(user);
            fila.TenantId.Should().Be(tenant);
            fila.ProcedureTypeCode.Should().Be("BLINDAJE");
            fila.ClientIp.Should().Be("190.24.1.9", "primer salto de X-Forwarded-For");
            fila.UserAgent.Should().Be("FlitTests/1.0");
            fila.AcceptedAt.ToUniversalTime().Should().BeOnOrAfter(antes);

            var reflejo = await db.TenantConfigAuditLogs
                .SingleAsync(x => x.TargetEntityId == id && x.Operation == AuditVocabulary.Operations.AcceptTerms, TestContext.Current.CancellationToken);
            reflejo.Module.Should().Be(AuditVocabulary.Modules.Tramites);
            reflejo.ChangedBy.Should().Be(user);
            reflejo.TenantId.Should().Be(tenant);
            reflejo.TenantType.Should().Be("COMPANY", "el tenant de prueba no es organismo de tránsito");
            reflejo.ClientIp.Should().Be("190.24.1.9");
            // jsonb normaliza el texto: se lee como JSON, no como cadena literal.
            using var detalle = JsonDocument.Parse(reflejo.NewValue!);
            detalle.RootElement.GetProperty("procedureTypeCode").GetString().Should().Be("BLINDAJE");
            detalle.RootElement.GetProperty("termsUrl").GetString().Should().Be(ProcedureTermsOptions.DefaultUrl);
        }
        finally
        {
            await CleanupAsync(user, antes);
        }
    }

    [Fact]
    public async Task Post_DosVecesElMismoUsuarioYTipo_DejaDosFilas()
    {
        // RN-05: se pide en cada creación; no hay unicidad que colapse la segunda aceptación.
        var user = await AnyUserAsync();
        using var client = Client(TestTokenFactory.CreateAdminCompanyToken(await AnyCompanyTenantAsync(), user));
        var desde = DateTimeOffset.UtcNow.AddSeconds(-1);
        var antes = await CountByUserAsync(user);

        try
        {
            var r1 = await client.PostAsJsonAsync(Url, new { procedureTypeCode = "BLINDAJE" }, TestContext.Current.CancellationToken);
            var r2 = await client.PostAsJsonAsync(Url, new { procedureTypeCode = "BLINDAJE" }, TestContext.Current.CancellationToken);

            r1.StatusCode.Should().Be(HttpStatusCode.Created);
            r2.StatusCode.Should().Be(HttpStatusCode.Created);
            (await CountByUserAsync(user)).Should().Be(antes + 2);
        }
        finally
        {
            await CleanupAsync(user, desde);
        }
    }

    [Fact]
    public async Task Post_SuperAdminSinCompaniaAcotada_Returns201_ConTenantNulo()
    {
        var user = await AnyUserAsync();
        using var client = Client(SuperAdminToken(user));
        var desde = DateTimeOffset.UtcNow.AddSeconds(-1);

        try
        {
            var response = await client.PostAsJsonAsync(Url, new { procedureTypeCode = "BLINDAJE" }, TestContext.Current.CancellationToken);

            response.StatusCode.Should().Be(HttpStatusCode.Created);
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            var id = doc.RootElement.GetProperty("id").GetGuid();
            await using var db = CreateDbContext();
            var fila = await db.ProcedureTermsAcceptances.SingleAsync(x => x.Id == id, TestContext.Current.CancellationToken);
            fila.TenantId.Should().BeNull();
        }
        finally
        {
            await CleanupAsync(user, desde);
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────────────

    private HttpClient Client(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static string SuperAdminToken(Guid user)
    {
        var handler = new Microsoft.IdentityModel.JsonWebTokens.JsonWebTokenHandler();
        return handler.CreateToken(new Microsoft.IdentityModel.Tokens.SecurityTokenDescriptor
        {
            Issuer = "https://api.flit.co",
            Audience = "flit-api",
            Subject = new System.Security.Claims.ClaimsIdentity(
            [
                new System.Security.Claims.Claim("sub", user.ToString()),
                new System.Security.Claims.Claim("role", "SuperAdmin"),
            ]),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new Microsoft.IdentityModel.Tokens.SigningCredentials(
                new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(new string('k', 64))),
                Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256),
        });
    }

    private FlitDbContext CreateDbContext() =>
        _factory.Services.CreateScope().ServiceProvider.GetRequiredService<FlitDbContext>();

    /// <summary>La evidencia tiene FK a <c>identity.users</c>: hace falta un usuario real de la base.</summary>
    private async Task<Guid> AnyUserAsync()
    {
        await using var db = CreateDbContext();
        return await db.Database.SqlQueryRaw<Guid>(
                "SELECT id AS \"Value\" FROM identity.users ORDER BY created_at LIMIT 1")
            .SingleAsync(TestContext.Current.CancellationToken);
    }

    private async Task<Guid> AnyCompanyTenantAsync()
    {
        await using var db = CreateDbContext();
        return await db.Database.SqlQueryRaw<Guid>(
                "SELECT id AS \"Value\" FROM identity.tenants WHERE tenant_type <> 'TRANSIT_OFFICE' AND is_active ORDER BY created_at LIMIT 1")
            .SingleAsync(TestContext.Current.CancellationToken);
    }

    private async Task<int> CountByUserAsync(Guid user)
    {
        await using var db = CreateDbContext();
        return await db.ProcedureTermsAcceptances.CountAsync(x => x.UserId == user, TestContext.Current.CancellationToken);
    }

    /// <summary>Borra solo lo que la prueba creó (aceptaciones de ese usuario desde <paramref name="desde"/>).</summary>
    private async Task CleanupAsync(Guid user, DateTimeOffset desde)
    {
        await using var db = CreateDbContext();
        var ids = await db.ProcedureTermsAcceptances
            .Where(x => x.UserId == user && x.AcceptedAt >= desde)
            .Select(x => x.Id)
            .ToListAsync(TestContext.Current.CancellationToken);
        await db.TenantConfigAuditLogs.Where(x => x.TargetEntityId != null && ids.Contains(x.TargetEntityId.Value))
            .ExecuteDeleteAsync(TestContext.Current.CancellationToken);
        await db.ProcedureTermsAcceptances.Where(x => ids.Contains(x.Id))
            .ExecuteDeleteAsync(TestContext.Current.CancellationToken);
    }
}
