using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Flit.Admin.Application.Auditing;
using Flit.Admin.Application.Companies.MandateSigners.RepresentedAssociations;
using Flit.Admin.Domain.Companies.MandateSigners;
using Flit.Api.Authorization;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Entities.Catalogs;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Companies.MandateSigners;

/// <summary>
/// HU #13176 (Feature #13119 F7, Épica #13090) — reporte de mandatarios impactados y retiro de las asociaciones
/// por Representante Legal con aviso previo a los clientes.
/// <para>Uso de ejemplo: <c>POST /api/v1/admin/mandate-signers/represented-associations/retire</c> con
/// <c>{"confirmaAvisoEnviado":false}</c> ⇒ 409 <c>aviso_no_confirmado</c> y ninguna fila cambia.</para>
/// </summary>
public sealed class RepresentedAssociationRetirementTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string ReportUrl = "/api/v1/admin/mandate-signers/represented-associations/impact-report";
    private const string RetireUrl = "/api/v1/admin/mandate-signers/represented-associations/retire";

    private static readonly SymmetricSecurityKey DummyKey = new(Encoding.UTF8.GetBytes(new string('k', 64)));
    private static readonly Guid Ot = Guid.NewGuid();
    private static readonly Guid Gestora = Guid.NewGuid();

    private readonly HttpClient _client;

    public RepresentedAssociationRetirementTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private void Authenticate(string role, string? entityType = null) =>
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", MintToken(role, entityType));

    // ── AC4: solo el Super Admin ───────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("ot_admin", AdminAuthorization.TransitOfficeEntityType)]
    [InlineData("ot_operator", AdminAuthorization.TransitOfficeEntityType)]
    [InlineData("gestor_tramites_ot", AdminAuthorization.TransitOfficeEntityType)]
    [InlineData("AdminCompany", null)]
    public async Task AC4_OtroPerfil_RecibeYa403_EnReporteYEnRetiro(string role, string? entityType)
    {
        Authenticate(role, entityType);

        (await _client.GetAsync(ReportUrl, Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await _client.PostAsJsonAsync(RetireUrl, new { confirmaAvisoEnviado = true }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AC4_SinSesion_Recibe401()
    {
        _client.DefaultRequestHeaders.Authorization = null;

        (await _client.GetAsync(ReportUrl, Ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await _client.PostAsync(RetireUrl, null, Ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── AC3: sin confirmación no se retira nada (409) ──────────────────────────────────────────────

    [Theory]
    [InlineData("{\"confirmaAvisoEnviado\":false}")]
    [InlineData("{}")]
    [InlineData("")]
    public async Task AC3_SinConfirmacion_Responde409AvisoNoConfirmado(string body)
    {
        Authenticate(AdminAuthorization.SuperAdminRole);

        var response = await _client.PostAsync(
            RetireUrl,
            body.Length == 0 ? null : new StringContent(body, Encoding.UTF8, "application/json"),
            Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync(Ct)).Should().Contain("aviso_no_confirmado");
    }

    [Fact]
    public async Task AC3_Handler_SinConfirmacion_NoTocaLaBaseNiAudita()
    {
        var store = Substitute.For<IRepresentedAssociationRetirementStore>();
        var audit = Substitute.For<IAdminAuditWriter>();
        var handler = new RetireRepresentedAssociationsHandler(store, audit);

        var act = async () => await handler.HandleAsync(false, Guid.NewGuid(), "SuperAdmin", Ct);

        await act.Should().ThrowAsync<RepresentedAssociationNoticeNotConfirmedException>();
        await store.DidNotReceive().RetireActiveAsync(Arg.Any<CancellationToken>());
        await audit.DidNotReceive().WriteAsync(Arg.Any<AdminAuditEntry>(), Arg.Any<CancellationToken>());
    }

    // ── AC2: retiro con aviso confirmado + auditoría sin nombres ───────────────────────────────────

    [Fact]
    public async Task AC2_Handler_ConConfirmacion_RetiraYAuditaCantidadFechaYRol()
    {
        var store = Substitute.For<IRepresentedAssociationRetirementStore>();
        store.RetireActiveAsync(Arg.Any<CancellationToken>()).Returns(7);
        var audit = Substitute.For<IAdminAuditWriter>();
        var actor = Guid.NewGuid();
        var handler = new RetireRepresentedAssociationsHandler(store, audit);

        var result = await handler.HandleAsync(true, actor, "SuperAdmin", Ct);

        result.RetiredRows.Should().Be(7);
        await audit.Received(1).WriteAsync(
            Arg.Is<AdminAuditEntry>(e =>
                e.ActorUserId == actor
                && e.Operation == AuditVocabulary.Operations.Delete
                && e.Result == AuditVocabulary.Results.Success
                && e.NewValue!.Contains("\"filasRetiradas\":7")
                && e.NewValue.Contains("\"rol\":\"SuperAdmin\"")
                && e.NewValue.Contains("\"fecha\"")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC2_Store_Retira_SoloLasActivas_YLosMandatariosQuedanSinAcotacion()
    {
        await using var ctx = NewContext();
        var (signer, ficha1, ficha2) = await SeedAsync(ctx);
        var store = new RepresentedAssociationRetirementStore(ctx);

        var retirados = await store.RetireActiveAsync(Ct);

        retirados.Should().Be(2);
        (await ctx.MandateSignerRepresentedCompanies.AsNoTracking().ToListAsync(Ct))
            .Should().ContainSingle(a => !a.IsActive, "el histórico inactivo no se toca");
        (await ctx.MandateSigners.AsNoTracking().AnyAsync(s => s.Id == signer, Ct)).Should().BeTrue();
        ficha1.Should().NotBe(ficha2);
    }

    // ── AC5: sin asociaciones / repetición ─────────────────────────────────────────────────────────

    [Fact]
    public async Task AC5_SinAsociaciones_ReporteVacio_YRetiroRepetidoDevuelveCero()
    {
        await using var ctx = NewContext();
        var store = new RepresentedAssociationRetirementStore(ctx);
        var report = new GetRepresentedAssociationImpactReportHandler(store);

        (await report.HandleAsync(Ct)).Should().BeEmpty();
        (await store.RetireActiveAsync(Ct)).Should().Be(0);

        await SeedAsync(ctx);
        (await store.RetireActiveAsync(Ct)).Should().Be(2);
        (await store.RetireActiveAsync(Ct)).Should().Be(0, "repetirlo no falla ni retira nada más");
        (await report.HandleAsync(Ct)).Should().BeEmpty();
    }

    // ── AC1: el reporte ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC1_Reporte_PorCompaniaYOrganismo_ConCantidadYNit_SinDocumentoNiFirma()
    {
        await using var ctx = NewContext();
        await SeedAsync(ctx);
        var report = new GetRepresentedAssociationImpactReportHandler(new RepresentedAssociationRetirementStore(ctx));

        var rows = await report.HandleAsync(Ct);

        var row = rows.Should().ContainSingle().Subject;
        row.CompanyName.Should().Be("Gestora SAS");
        row.TransitOfficeName.Should().Be("OT Prueba");
        row.MandateSignerName.Should().Be("Daniel Amado");
        row.AssociatedCount.Should().Be(2);
        row.AssociatedNits.Should().Equal("900111111", "900222222");
        typeof(RepresentedAssociationImpactRow).GetProperties().Select(p => p.Name).Should().NotContain(
            n => n.Contains("Document") || n.Contains("Signature") || n.Contains("Path") || n.Contains("Email"));
    }

    [Fact]
    public async Task AC1_Reporte_IgnoraMandatariosInactivosOEliminados()
    {
        await using var ctx = NewContext();
        var (signer, _, _) = await SeedAsync(ctx);
        var entity = await ctx.MandateSigners.SingleAsync(s => s.Id == signer, Ct);
        entity.DeletedAt = DateTimeOffset.UtcNow;
        await ctx.SaveChangesAsync(Ct);
        var report = new GetRepresentedAssociationImpactReportHandler(new RepresentedAssociationRetirementStore(ctx));

        (await report.HandleAsync(Ct)).Should().BeEmpty();
    }

    [Fact]
    public void AC1_Csv_LlevaLasColumnasDelReporte_SinDocumentoNiFirma_YNeutralizaFormulas()
    {
        var csv = RepresentedAssociationImpactCsv.Build(
        [
            new RepresentedAssociationImpactRow(
                Guid.NewGuid(), "=Gestora", Guid.NewGuid(), "OT, Prueba", Guid.NewGuid(), "Daniel \"D\" Amado",
                2, ["900111111", "900222222"]),
        ]);

        var lines = csv.TrimEnd().Split("\r\n");
        lines[0].Should().Be(
            "compania_id,compania,organismo_id,organismo,mandatario_id,mandatario," +
            "cantidad_empresas_asociadas,nit_empresas_asociadas");
        lines[0].Should().NotContainAny("documento", "firma", "signature");
        lines[1].Should().Contain("\"'=Gestora\"").And.Contain("\"OT, Prueba\"").And.Contain("\"Daniel \"\"D\"\" Amado\"");
        lines[1].Should().EndWith("\"2\",\"900111111; 900222222\"");
    }

    // ── infraestructura de la prueba ───────────────────────────────────────────────────────────────

    private static FlitDbContext NewContext() =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase($"flit-13176-{Guid.NewGuid()}")
            .Options);

    /// <summary>Un mandatario de Gestora en Ot con dos asociaciones activas y una inactiva (histórico).</summary>
    private static async Task<(Guid Signer, Guid Ficha1, Guid Ficha2)> SeedAsync(FlitDbContext ctx)
    {
        var now = DateTimeOffset.UtcNow;
        ctx.Tenants.Add(new Tenant
        {
            Id = Gestora, Code = "GES-" + Guid.NewGuid().ToString("N")[..6], LegalName = "Gestora SAS",
            TaxId = "800000001", TenantType = "COMPANY", IsActive = true, CreatedAt = now,
        });
        ctx.TransitOffices.Add(new TransitOffice { Id = Ot, Code = "OT-T", Name = "OT Prueba", IsActive = true });

        var f1 = Guid.NewGuid();
        var f2 = Guid.NewGuid();
        var f3 = Guid.NewGuid();
        foreach (var (id, nit) in new[] { (f1, "900111111"), (f2, "900222222"), (f3, "900333333") })
        {
            ctx.RepresentedCompanies.Add(new RepresentedCompanyEntity
            {
                Id = id, TenantId = Gestora, DocumentType = "NIT", DocumentNumber = nit, Name = "Empresa " + nit,
                CreatedAt = now,
            });
        }

        var signer = Guid.NewGuid();
        ctx.MandateSigners.Add(new MandateSigner
        {
            Id = signer, TransitOfficeId = Ot, FullName = "Daniel Amado", DocumentType = "CC",
            DocumentNumber = "1193552679", IntegrityHash = new string('a', 64), RegisteredAt = now, IsActive = true,
            CreatedAt = now,
        });
        ctx.MandateSignerCompanies.Add(new MandateSignerCompany
        {
            Id = Guid.NewGuid(), MandateSignerId = signer, TransitOfficeId = Ot, CompanyTenantId = Gestora,
            IsActive = true, CreatedAt = now,
        });
        foreach (var (empresa, activa) in new[] { (f1, true), (f2, true), (f3, false) })
        {
            ctx.MandateSignerRepresentedCompanies.Add(new MandateSignerRepresentedCompany
            {
                Id = Guid.NewGuid(), MandateSignerId = signer, TransitOfficeId = Ot, RepresentedCompanyId = empresa,
                IsActive = activa, CreatedAt = now,
            });
        }

        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        return (signer, f1, f2);
    }

    private static string MintToken(string role, string? entityType)
    {
        var claims = new List<Claim>
        {
            new("sub", Guid.NewGuid().ToString()),
            new("role", role),
            new("tenant_id", Guid.NewGuid().ToString()),
        };
        if (entityType is not null)
        {
            claims.Add(new Claim(AdminAuthorization.EntityTypeClaimType, entityType));
        }

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "https://api.flit.co",
            Audience = "flit-api",
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(DummyKey, SecurityAlgorithms.HmacSha256),
        });
    }
}
