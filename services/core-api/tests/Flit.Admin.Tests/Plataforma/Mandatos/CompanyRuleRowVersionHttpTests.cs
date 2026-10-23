using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;
using FluentAssertions;

namespace Flit.Admin.Tests.Plataforma.Mandatos;

/// <summary>
/// HU #13148 (Feature #13117, Épica #13090) — concurrencia optimista por <c>row_version</c> en
/// <c>admin.company_ot_mandate_rules</c>: el Super Admin no pisa sin saberlo el tipo de mandato que otro cambió.
/// Corre contra la base local con el trigger real (<c>tr_company_ot_mandate_rules_row_version</c>, DDL 123).
/// <para>Uso de ejemplo: <c>PUT .../company-rules/{C}</c> con <c>{ "assignmentMode": "open", "rowVersion": 4 }</c>
/// responde 200 con <c>rowVersion</c> 5; si otro ya lo dejó en 5, responde <c>409 row_version_conflict</c>.</para>
/// </summary>
public sealed class CompanyRuleRowVersionHttpTests(WebApplicationFactory<Program> factory)
    : CompanyRulesHttpTestBase(factory)
{
    private async Task<long> CreateRuleAsync(string mode = "signer")
    {
        AuthenticateSuperAdmin();
        var response = await Client.PutAsJsonAsync(RuleUrl(Company), new { assignmentMode = mode }, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("rowVersion").GetInt64();
    }

    [Fact]
    public async Task AC1_GuardarConLaVersionVigente_Responde200_ConElRowVersionIncrementado()
    {
        var initial = await CreateRuleAsync();

        var response = await Client.PutAsJsonAsync(
            RuleUrl(Company), new { assignmentMode = "open", rowVersion = initial }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("assignmentMode").GetString().Should().Be("open");
        body.GetProperty("rowVersion").GetInt64().Should().Be(initial + 1, "lo incrementa el trigger");
        (await ReadRuleAsync(Company)).RowVersion.Should().Be(initial + 1);
    }

    [Fact]
    public async Task AC2_GuardarConUnaVersionVieja_Responde409_YNoCambiaNingunDato()
    {
        var initial = await CreateRuleAsync();
        (await Client.PutAsJsonAsync(RuleUrl(Company), new { assignmentMode = "open", rowVersion = initial }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var stale = await Client.PutAsJsonAsync(
            RuleUrl(Company), new { assignmentMode = "institutional", institutionalMandataryName = "ENTIDAD X", rowVersion = initial }, Ct);

        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await stale.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("error").GetString()
            .Should().Be("row_version_conflict");
        var rule = await ReadRuleAsync(Company);
        rule.AssignmentMode.Should().Be("open", "la escritura vieja no cambia nada");
        rule.RowVersion.Should().Be(initial + 1);
    }

    [Fact]
    public async Task AC3_PrimeraReglaDeLaCompania_SinRowVersion_LaCreaYResponde200ConSuVersionInicial()
    {
        AuthenticateSuperAdmin();
        (await ReadRuleAsync(Company)).Exists.Should().BeFalse();

        var response = await Client.PutAsJsonAsync(RuleUrl(Company), new { assignmentMode = "open" }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("hasExplicitRule").GetBoolean().Should().BeTrue();
        body.GetProperty("rowVersion").GetInt64().Should().Be(0);
        (await ReadRuleAsync(Company)).Exists.Should().BeTrue();
    }

    [Fact]
    public async Task AC3b_AltaConRowVersionCuandoLaReglaYaNoExiste_Responde409()
    {
        AuthenticateSuperAdmin();

        var response = await Client.PutAsJsonAsync(
            RuleUrl(Company), new { assignmentMode = "open", rowVersion = 3 }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadRuleAsync(Company)).Exists.Should().BeFalse("otro usuario la restableció: no se recrea a ciegas");
    }

    [Fact]
    public async Task AC4_ReglaExistente_PutSinRowVersion_Responde409_YNoSobrescribe()
    {
        await CreateRuleAsync("signer");

        var response = await Client.PutAsJsonAsync(RuleUrl(Company), new { assignmentMode = "open" }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("error").GetString()
            .Should().Be("row_version_conflict");
        (await ReadRuleAsync(Company)).AssignmentMode.Should().Be("signer");
    }

    [Fact]
    public async Task AC5_PatchDefaultSigner_ConVersionDesactualizada_Responde409()
    {
        var initial = await CreateRuleAsync();
        (await Client.PutAsJsonAsync(RuleUrl(Company), new { assignmentMode = "signer", rowVersion = initial }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await Client.PatchAsJsonAsync(
            RuleUrl(Company) + "/default-signer",
            new { defaultMandateSignerId = (Guid?)null, rowVersion = initial },
            Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("error").GetString()
            .Should().Be("row_version_conflict");
        (await ReadRuleAsync(Company)).Exists.Should().BeTrue("el 409 no restablece la regla");
    }

    [Fact]
    public async Task AC5_Delete_ConVersionDesactualizada_Responde409_YConLaVigenteResponde204()
    {
        var initial = await CreateRuleAsync();
        (await Client.PutAsJsonAsync(RuleUrl(Company), new { assignmentMode = "open", rowVersion = initial }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var stale = await Client.DeleteAsync($"{RuleUrl(Company)}?rowVersion={initial}", Ct);
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadRuleAsync(Company)).Exists.Should().BeTrue();

        var current = await Client.DeleteAsync($"{RuleUrl(Company)}?rowVersion={initial + 1}", Ct);
        current.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ReadRuleAsync(Company)).Exists.Should().BeFalse();
    }

    [Fact]
    public async Task Compatibilidad_ElHubDelOt_SinRowVersion_SigueEscribiendo_YConVersionVieja_Responde409()
    {
        var initial = await CreateRuleAsync();
        (await Client.PutAsJsonAsync(RuleUrl(Company), new { assignmentMode = "open", rowVersion = initial }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        AuthenticateOtAdmin();
        var hubUrl = $"/api/v1/admin/ot/offices/{Office}/mandatos/company-rules/{Company}";

        var stale = await Client.DeleteAsync($"{hubUrl}?rowVersion={initial}", Ct);
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var withoutVersion = await Client.DeleteAsync(hubUrl, Ct);
        withoutVersion.StatusCode.Should().Be(HttpStatusCode.NoContent, "F1 y F3 no envían rowVersion");
        (await ReadRuleAsync(Company)).Exists.Should().BeFalse();
    }

    [Fact]
    public async Task AC6_FilasAnterioresALaMigracion_QuedanConRowVersionInicial_YElListadoFunciona()
    {
        // Fila "previa": insertada por SQL sin indicar row_version (como las que existían antes de la migración).
        await using (var db = CreateDbContext())
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO admin.company_ot_mandate_rules (company_tenant_id, transit_office_id, assignment_mode, created_at)
                VALUES ({Company}, {Office}, 'open', now())
                """,
                Ct);
        }

        AuthenticateSuperAdmin();
        var response = await Client.GetAsync($"/api/v1/admin/plataforma/mandatos/ot/{Office}/company-rules", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("items");
        var row = items.EnumerateArray().Single(e => e.GetProperty("companyTenantId").GetGuid() == Company);
        row.GetProperty("hasExplicitRule").GetBoolean().Should().BeTrue();
        row.GetProperty("rowVersion").GetInt64().Should().Be(0);

        await using var probe = CreateDbContext();
        var nullable = await probe.Database.SqlQuery<string>(
            $"""
            SELECT is_nullable AS "Value" FROM information_schema.columns
             WHERE table_schema = 'admin' AND table_name = 'company_ot_mandate_rules' AND column_name = 'row_version'
            """).SingleAsync(Ct);
        nullable.Should().Be("NO");
        var triggers = await probe.Database.SqlQuery<int>(
            $"""
            SELECT count(*)::int AS "Value" FROM pg_trigger
             WHERE tgname = 'tr_company_ot_mandate_rules_row_version' AND NOT tgisinternal
            """).SingleAsync(Ct);
        triggers.Should().Be(1);
    }
}
