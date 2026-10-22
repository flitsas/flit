using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Infrastructure.Persistence.Entities.Admin;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Admin.Tests.Plataforma.Mandatos;

/// <summary>
/// HU #13171 (Feature #13118, Épica #13090) — PUT /api/v1/admin/plataforma/mandatos/formatos/{code}: el Super Admin
/// edita nombre, tipo y plantilla de un formato existente, con RowVersion y bitácora. Corre contra la base local
/// (DDL 124 aplicado). Edita solo <c>generico</c> y lo restaura al terminar; NUNCA publica plantillas aquí porque las
/// versiones son inmutables (el trigger rechaza DELETE): la publicación se prueba en
/// <see cref="MandateFormatAdminServiceTests"/> sobre una base en memoria.
/// </summary>
public sealed class MandatoFormatEditHttpTests(WebApplicationFactory<Program> factory)
    : CompanyRulesHttpTestBase(factory)
{
    private const string Code = "generico";
    private const string FormatsUrl = "/api/v1/admin/plataforma/mandatos/formatos";

    private async Task<JsonElement> ReadFormatAsync(string code = Code)
    {
        var items = (await (await Client.GetAsync(FormatsUrl, Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct))
            .GetProperty("items");
        return items.EnumerateArray().Single(i => i.GetProperty("code").GetString() == code).Clone();
    }

    private Task<HttpResponseMessage> PutAsync(object body, string code = Code) =>
        Client.PutAsJsonAsync($"{FormatsUrl}/{code}", body, Ct);

    private async Task<List<TenantConfigAuditLog>> ReadFormatAuditAsync()
    {
        await using var db = CreateDbContext();
        return await db.TenantConfigAuditLogs.AsNoTracking()
            .Where(l => l.EntityName == "mandate_format" && l.ChangedBy == SuperAdminUserId)
            .OrderBy(l => l.ChangedAt)
            .ToListAsync(Ct);
    }

    [Fact]
    public async Task AC1_Renombrar_Responde200_ElCatalogoDevuelveElNombreNuevoConRowVersionIncrementado()
    {
        AuthenticateSuperAdmin();
        var before = await ReadFormatAsync();

        var response = await PutAsync(new { rowVersion = before.GetProperty("rowVersion").GetInt64(), name = "Genérico 2026" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var after = await ReadFormatAsync();
        after.GetProperty("name").GetString().Should().Be("Genérico 2026");
        after.GetProperty("rowVersion").GetInt64().Should().BeGreaterThan(before.GetProperty("rowVersion").GetInt64());
        after.GetProperty("currentVersion").GetInt32().Should().Be(before.GetProperty("currentVersion").GetInt32());
    }

    [Theory]
    [InlineData("signer")]
    [InlineData("institutional")]
    [InlineData("open")]
    public async Task AC2_CambiarTipoAsociado_Responde200_YGuardaElModo(string mode)
    {
        AuthenticateSuperAdmin();
        // El modo de partida es siempre distinto del pedido para que haya cambio real.
        var start = mode == "open" ? "signer" : "open";
        var rv = (await ReadFormatAsync()).GetProperty("rowVersion").GetInt64();
        (await PutAsync(new { rowVersion = rv, assignmentMode = start })).StatusCode.Should().Be(HttpStatusCode.OK);
        rv = (await ReadFormatAsync()).GetProperty("rowVersion").GetInt64();

        var response = await PutAsync(new { rowVersion = rv, assignmentMode = mode });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadFormatAsync()).GetProperty("assignmentMode").GetString().Should().Be(mode);
    }

    [Fact]
    public async Task AC4_PlantillaConVariableInvalida_Responde400_PlantillaVariableInvalida_YNoCreaVersion()
    {
        AuthenticateSuperAdmin();
        var before = await ReadFormatAsync();

        var response = await PutAsync(
            new { rowVersion = before.GetProperty("rowVersion").GetInt64(), body = "Cédula {{cedula_inventada}}" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("error").GetString().Should().Be("plantilla_variable_invalida");
        body.GetProperty("unknownVariables").EnumerateArray().Single().GetProperty("name").GetString()
            .Should().Be("cedula_inventada");
        var after = await ReadFormatAsync();
        after.GetProperty("currentVersion").GetInt32().Should().Be(before.GetProperty("currentVersion").GetInt32());
        after.GetProperty("rowVersion").GetInt64().Should().Be(before.GetProperty("rowVersion").GetInt64());
    }

    [Theory]
    [InlineData("", "nombre_vacio")]
    [InlineData("   ", "nombre_vacio")]
    [InlineData("N81", "nombre_demasiado_largo")]
    [InlineData("Sabaneta", "nombre_repetido")]
    [InlineData("  sabaneta ", "nombre_repetido")]
    public async Task AC5_NombreInvalido_Responde400ConElMotivo_YNoModificaNada(string name, string error)
    {
        AuthenticateSuperAdmin();
        var before = await ReadFormatAsync();
        var sent = name == "N81" ? new string('n', 81) : name;

        var response = await PutAsync(new { rowVersion = before.GetProperty("rowVersion").GetInt64(), name = sent });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("error").GetString().Should().Be(error);
        var after = await ReadFormatAsync();
        after.GetProperty("name").GetString().Should().Be(before.GetProperty("name").GetString());
        after.GetProperty("rowVersion").GetInt64().Should().Be(before.GetProperty("rowVersion").GetInt64());
    }

    [Fact]
    public async Task AC6_Conflicto_RowVersionDesactualizadoOAusente_Responde409_YNoCambiaNingunDato()
    {
        AuthenticateSuperAdmin();
        var before = await ReadFormatAsync();
        var rv = before.GetProperty("rowVersion").GetInt64();

        var stale = await PutAsync(new { rowVersion = rv - 1, name = "Obsoleto" });
        var missing = await PutAsync(new { name = "Sin versión" });

        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        missing.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await stale.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("error").GetString()
            .Should().Be("row_version_conflict");
        var after = await ReadFormatAsync();
        after.GetProperty("name").GetString().Should().Be(before.GetProperty("name").GetString());
        after.GetProperty("rowVersion").GetInt64().Should().Be(rv);
    }

    [Fact]
    public async Task AC7_SinCrearNiBorrar_CodigoNuevoEs404_PostYDeleteSonMetodoNoPermitido()
    {
        AuthenticateSuperAdmin();

        var create = await PutAsync(new { rowVersion = 0, name = "Nuevo" }, "nuevo_formato");
        var post = await Client.PostAsJsonAsync($"{FormatsUrl}/{Code}", new { name = "X" }, Ct);
        var delete = await Client.DeleteAsync($"{FormatsUrl}/{Code}", Ct);

        create.StatusCode.Should().Be(HttpStatusCode.NotFound);
        post.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
        delete.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
        (await (await Client.GetAsync(FormatsUrl, Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct))
            .GetProperty("items").GetArrayLength().Should().Be(5);
    }

    [Fact]
    public async Task AC8_SoloSuperAdmin_AdminOt403_AdminDeCompania403_SinToken401_NoModificaNada()
    {
        AuthenticateSuperAdmin();
        var before = await ReadFormatAsync();
        var body = new { rowVersion = before.GetProperty("rowVersion").GetInt64(), name = "Intruso" };

        AuthenticateOtAdmin();
        var ot = await PutAsync(body);
        AuthenticateCompanyAdmin();
        var company = await PutAsync(body);
        AuthenticateAnonymous();
        var anonymous = await PutAsync(body);

        ot.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        company.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        AuthenticateSuperAdmin();
        (await ReadFormatAsync()).GetProperty("name").GetString().Should().Be(before.GetProperty("name").GetString());
    }

    [Fact]
    public async Task AC9_Bitacora_ExitoSinCambioYFallo_RegistranActorFormatoValoresYCodigoDeError()
    {
        AuthenticateSuperAdmin();
        var before = await ReadFormatAsync();
        var rv = before.GetProperty("rowVersion").GetInt64();
        var oldName = before.GetProperty("name").GetString();
        var oldMode = before.GetProperty("assignmentMode").GetString();
        var newMode = oldMode == "open" ? "signer" : "open";

        (await PutAsync(new { rowVersion = rv, name = "Genérico bitácora", assignmentMode = newMode }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var rv2 = (await ReadFormatAsync()).GetProperty("rowVersion").GetInt64();
        (await PutAsync(new { rowVersion = rv2, name = "Genérico bitácora" })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PutAsync(new { rowVersion = rv2, name = "" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var rows = await ReadFormatAuditAsync();
        rows.Should().HaveCount(3);
        var ok = rows[0];
        ok.Result.Should().Be("success");
        ok.Operation.Should().Be("update");
        ok.ChangedBy.Should().Be(SuperAdminUserId);
        ok.ChangedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2));
        using (var oldJson = JsonDocument.Parse(ok.OldValue!))
        using (var newJson = JsonDocument.Parse(ok.NewValue!))
        {
            oldJson.RootElement.GetProperty("format").GetString().Should().Be(Code);
            oldJson.RootElement.GetProperty("name").GetString().Should().Be(oldName);
            oldJson.RootElement.GetProperty("assignmentMode").GetString().Should().Be(oldMode);
            newJson.RootElement.GetProperty("name").GetString().Should().Be("Genérico bitácora");
            newJson.RootElement.GetProperty("assignmentMode").GetString().Should().Be(newMode);
            newJson.RootElement.GetProperty("version").GetInt32().Should().BeGreaterThanOrEqualTo(0);
            newJson.RootElement.GetProperty("changed").GetBoolean().Should().BeTrue();
        }

        using (var noChange = JsonDocument.Parse(rows[1].NewValue!))
        {
            rows[1].Result.Should().Be("success");
            noChange.RootElement.GetProperty("changed").GetBoolean().Should().BeFalse();
        }

        rows[2].Result.Should().Be("failure");
        rows[2].ErrorCode.Should().Be("nombre_vacio");
        // Nunca el cuerpo completo de una plantilla ni datos de personas.
        rows.Select(r => r.NewValue).Should().OnlyContain(v => !v!.Contains("{{", StringComparison.Ordinal));
    }

    public override void Dispose()
    {
        // Devuelve el formato editado a sus valores de fábrica (el trigger de row_version sube el token, sin problema).
        using (var db = CreateDbContext())
        {
            db.MandateFormatSettings.Where(s => s.FormatCode == Code)
                .ExecuteUpdate(s => s
                    .SetProperty(x => x.DisplayName, "Genérico")
                    .SetProperty(x => x.AssignmentMode, "signer"));
            db.TenantConfigAuditLogs
                .Where(l => l.EntityName == "mandate_format" && l.ChangedBy == SuperAdminUserId)
                .ExecuteDelete();
        }

        base.Dispose();
    }

    // ── Restablecer redacción de fábrica (sin publicar plantillas: las versiones son inmutables) ────────────────

    private Task<HttpResponseMessage> ResetAsync(object body, string code = Code) =>
        Client.PostAsJsonAsync($"{FormatsUrl}/{code}/restablecer-plantilla", body, Ct);

    [Fact]
    public async Task Restablecer_SuperAdmin_SinPlantillaEditada_Responde200_SinCambios()
    {
        AuthenticateSuperAdmin();
        var before = await ReadFormatAsync();
        // Corre contra la base local: si alguien publicó una plantilla en «generico», no se le descarta. El restablecer
        // con plantilla editada se prueba en MandateFormatAdminServiceTests / MandateFormatRepositoryTests.
        if (before.GetProperty("currentVersion").GetInt32() > 0)
            return;

        var response = await ResetAsync(new { rowVersion = before.GetProperty("rowVersion").GetInt64() });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("changed").GetBoolean().Should().BeFalse();
        (await ReadFormatAsync()).GetProperty("currentVersion").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Restablecer_SoloSuperAdmin_AdminOt403_AdminDeCompania403_SinToken401()
    {
        var body = new { rowVersion = 0L };

        AuthenticateOtAdmin();
        var ot = await ResetAsync(body);
        AuthenticateCompanyAdmin();
        var company = await ResetAsync(body);
        AuthenticateAnonymous();
        var anonymous = await ResetAsync(body);

        ot.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        company.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Restablecer_SinRowVersion_Responde409()
    {
        AuthenticateSuperAdmin();

        (await ResetAsync(new { })).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
}
