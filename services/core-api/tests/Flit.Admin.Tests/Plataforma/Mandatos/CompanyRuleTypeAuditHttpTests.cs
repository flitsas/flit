using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
using FluentAssertions;

namespace Flit.Admin.Tests.Plataforma.Mandatos;

/// <summary>
/// HU #13149 (Feature #13117, Épica #13090) — bitácora de los cambios del tipo de mandato de una compañía en un
/// organismo y acceso exclusivo del Super Admin. Corre contra la base local (<c>admin.tenant_config_audit_logs</c>).
/// <para>Uso de ejemplo: el Super Admin cambia C de <c>signer</c> a <c>open</c> en O; la bitácora guarda actor,
/// fecha, organismo, compañía, tipo anterior (<c>signer</c>) y nuevo (<c>open</c>), sin datos personales.</para>
/// </summary>
public sealed class CompanyRuleTypeAuditHttpTests(WebApplicationFactory<Program> factory)
    : CompanyRulesHttpTestBase(factory)
{
    private async Task<long> CreateRuleAsync(string mode)
    {
        AuthenticateSuperAdmin();
        var response = await Client.PutAsJsonAsync(RuleUrl(Company), new { assignmentMode = mode }, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("rowVersion").GetInt64();
    }

    [Fact]
    public async Task AC1_CambioDeTipo_QuedaEnLaBitacora_ConActorOrganismoCompaniaYTipos()
    {
        var version = await CreateRuleAsync("signer");

        var response = await Client.PutAsJsonAsync(
            RuleUrl(Company), new { assignmentMode = "open", rowVersion = version }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var rows = await ReadAuditAsync(Company);
        var row = rows.Should().ContainSingle("solo el cambio real de tipo deja entrada").Subject;
        row.Result.Should().Be("success");
        row.Operation.Should().Be("update");
        row.ChangedBy.Should().Be(SuperAdminUserId);
        row.ChangedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2));
        row.TargetEntityId.Should().Be(Company);
        using var oldJson = JsonDocument.Parse(row.OldValue!);
        using var newJson = JsonDocument.Parse(row.NewValue!);
        oldJson.RootElement.GetProperty("assignmentMode").GetString().Should().Be("signer");
        oldJson.RootElement.GetProperty("officeId").GetGuid().Should().Be(Office);
        oldJson.RootElement.GetProperty("companyTenantId").GetGuid().Should().Be(Company);
        newJson.RootElement.GetProperty("assignmentMode").GetString().Should().Be("open");
        newJson.RootElement.GetProperty("restoredToDefault").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task AC2_Restablecer_QuedaEnLaBitacora_ConElTipoAnteriorYQueVolvioAlDefault()
    {
        var version = await CreateRuleAsync("signer");
        (await Client.PutAsJsonAsync(RuleUrl(Company), new { assignmentMode = "open", rowVersion = version }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await Client.DeleteAsync($"{RuleUrl(Company)}?rowVersion={version + 1}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var rows = await ReadAuditAsync(Company);
        rows.Should().HaveCount(2);
        var row = rows[^1];
        row.Operation.Should().Be("delete");
        row.Result.Should().Be("success");
        using var oldJson = JsonDocument.Parse(row.OldValue!);
        using var newJson = JsonDocument.Parse(row.NewValue!);
        oldJson.RootElement.GetProperty("assignmentMode").GetString().Should().Be("open");
        oldJson.RootElement.GetProperty("hadExplicitRule").GetBoolean().Should().BeTrue();
        newJson.RootElement.GetProperty("restoredToDefault").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task AC2b_RestablecerSinReglaPropia_NoDejaEntradaDeCambio()
    {
        AuthenticateSuperAdmin();

        var response = await Client.DeleteAsync(RuleUrl(Company), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ReadAuditAsync(Company)).Should().BeEmpty();
    }

    [Fact]
    public async Task AC3_ValidacionRechazada_NoModificaLaRegla_YLaBitacoraRegistraElIntentoConSuCodigo()
    {
        AuthenticateSuperAdmin();

        var response = await Client.PutAsJsonAsync(RuleUrl(Company), new { assignmentMode = "institutional" }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("error").GetString()
            .Should().Be("mandatario_institucional_requerido");
        (await ReadRuleAsync(Company)).Exists.Should().BeFalse();
        var row = (await ReadAuditAsync(Company)).Should().ContainSingle().Subject;
        row.Result.Should().Be("failure");
        row.ErrorCode.Should().Be("mandatario_institucional_requerido");
        row.ChangedBy.Should().Be(SuperAdminUserId);
        using var attempted = JsonDocument.Parse(row.NewValue!);
        attempted.RootElement.GetProperty("attemptedAssignmentMode").GetString().Should().Be("institutional");
        attempted.RootElement.GetProperty("officeId").GetGuid().Should().Be(Office);
    }

    [Fact]
    public async Task AC4_SinCambioReal_Responde200_YNoGeneraEntradaDeCambioDeTipo()
    {
        var version = await CreateRuleAsync("open");

        var response = await Client.PutAsJsonAsync(
            RuleUrl(Company), new { assignmentMode = "open", rowVersion = version }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var rows = await ReadAuditAsync(Company);
        // El alta inicial pasó de signer (heredado) a open; el PUT repetido no suma otra.
        rows.Should().HaveCount(1);
        rows[0].Operation.Should().Be("update");
    }

    [Fact]
    public async Task AC5_AdminOt_NoTieneAcceso_PutYDelete_Responden403_YNoModificanNada()
    {
        AuthenticateOtAdmin();

        var put = await Client.PutAsJsonAsync(RuleUrl(Company), new { assignmentMode = "open" }, Ct);
        var delete = await Client.DeleteAsync(RuleUrl(Company), Ct);

        put.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        delete.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReadRuleAsync(Company)).Exists.Should().BeFalse();
        (await ReadAuditAsync(Company)).Should().BeEmpty();
    }

    [Fact]
    public async Task AC6_AdminDeCompania_Responde403_YSinToken_Responde401()
    {
        AuthenticateCompanyAdmin();
        var forbidden = await Client.PutAsJsonAsync(RuleUrl(Company), new { assignmentMode = "open" }, Ct);

        AuthenticateAnonymous();
        var anonymous = await Client.PutAsJsonAsync(RuleUrl(Company), new { assignmentMode = "open" }, Ct);

        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ReadRuleAsync(Company)).Exists.Should().BeFalse();
    }

    [Fact]
    public async Task AC7_DatosSensibles_LaBitacoraGuardaSoloTiposOrganismoYCompania()
    {
        AuthenticateSuperAdmin();
        var response = await Client.PutAsJsonAsync(
            RuleUrl(Company),
            new
            {
                assignmentMode = "institutional",
                institutionalMandataryName = "UNION TEMPORAL DE PRUEBA F5",
                institutionalMandataryNit = "900123456-7",
                chamberCity = "Medellín",
            },
            Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var row = (await ReadAuditAsync(Company)).Should().ContainSingle().Subject;
        var stored = (row.OldValue ?? string.Empty) + (row.NewValue ?? string.Empty);
        stored.Should().NotContain("UNION TEMPORAL").And.NotContain("900123456").And.NotContain("Medellín");
        using var newJson = JsonDocument.Parse(row.NewValue!);
        newJson.RootElement.EnumerateObject().Select(p => p.Name)
            .Should().BeEquivalentTo("officeId", "companyTenantId", "assignmentMode", "restoredToDefault");
    }
}
