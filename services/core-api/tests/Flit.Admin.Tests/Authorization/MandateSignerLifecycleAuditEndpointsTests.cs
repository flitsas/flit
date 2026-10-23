using System.Net;
using System.Text.Json;
using Flit.Infrastructure.Persistence.Entities.Admin;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Admin.Tests.Authorization;

/// <summary>
/// HU #13138 (Feature #13115) — bitácora del ciclo de vida del mandatario sobre PostgreSQL y los endpoints reales: cada
/// baja, eliminación, retiro de default y reactivación exitosa deja sus eventos con rol y módulo del actor; un 403,
/// 404 o 409 no deja ninguno.
/// </summary>
public sealed class MandateSignerLifecycleAuditEndpointsTests(WebApplicationFactory<Program> factory)
    : MandateSignerEndpointsTestBase(factory)
{
    private async Task<List<TenantConfigAuditLog>> EventosAsync(Guid signerId)
    {
        await using var db = NewDb();
        return await db.TenantConfigAuditLogs.AsNoTracking()
            .Where(l => l.TargetEntityId == signerId && l.Module == "mandatarios")
            .OrderBy(l => l.ChangedAt)
            .ToListAsync(Ct);
    }

    private static JsonElement Payload(TenantConfigAuditLog log) => JsonDocument.Parse(log.NewValue!).RootElement;

    [Fact]
    public async Task AC1_y_AC2_el_Super_Admin_que_desactiva_deja_baja_y_un_retiro_por_cada_default_con_rol_y_modulo()
    {
        var signer = await SeedSignerAsync("Ana Restrepo", [(_companyA, "organismo")]);
        await SeedDefaultsAsync(signer);   // default de la compañía A y general del organismo A
        AuthenticateSuperAdmin();

        (await _client.PostAsync(Hub(signer, "/inactivate"), null, Ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var eventos = await EventosAsync(signer);
        var baja = eventos.Should().ContainSingle(l => l.FieldName == "deactivated").Subject;
        baja.Operation.Should().Be("deactivate");
        baja.Result.Should().Be("success");
        baja.ChangedBy.Should().Be(_superAdminUser);
        var payload = Payload(baja);
        payload.GetProperty("actorRole").GetString().Should().Be("super_admin");
        payload.GetProperty("actorModule").GetString().Should().Be("plataforma");
        payload.GetProperty("mandateSignerId").GetGuid().Should().Be(signer);
        payload.GetProperty("links").GetArrayLength().Should().Be(1);
        eventos.Count(l => l.FieldName == "default_removed" && l.Operation == "remove_default").Should().Be(2);
        baja.NewValue.Should().NotContain("Ana").And.NotContain("Restrepo", "sin datos personales del mandatario");
    }

    [Fact]
    public async Task AC2_el_Admin_de_Compania_que_elimina_un_default_deja_eliminacion_y_retiros_con_su_rol()
    {
        var signer = await SeedSignerAsync("Beto", [(_companyA, "compania")]);
        await SeedDefaultsAsync(signer);
        AuthenticateCompanyA();

        (await _client.DeleteAsync(Company(_companyA, signer, "?confirmarImpacto=true"), Ct))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var eventos = await EventosAsync(signer);
        var eliminacion = eventos.Should().ContainSingle(l => l.FieldName == "deleted").Subject;
        eliminacion.Operation.Should().Be("delete");
        Payload(eliminacion).GetProperty("actorRole").GetString().Should().Be("admin_compania");
        Payload(eliminacion).GetProperty("actorModule").GetString().Should().Be("compania");
        eventos.Count(l => l.FieldName == "default_removed").Should().Be(2);
    }

    [Fact]
    public async Task AC4_la_reactivacion_deja_un_evento_con_los_vinculos_restaurados()
    {
        var signer = await SeedSignerAsync("Ana", [(_companyA, "organismo"), (_companyB, "organismo")]);
        AuthenticateSuperAdmin();
        (await _client.PostAsync(Hub(signer, "/inactivate"), null, Ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        AuthenticateSuperAdmin();

        (await _client.PostAsync(Hub(signer, "/reactivate"), null, Ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        var evento = (await EventosAsync(signer)).Should().ContainSingle(l => l.FieldName == "reactivated").Subject;
        evento.Operation.Should().Be("reactivate");
        var payload = Payload(evento);
        payload.GetProperty("restoredLinks").GetArrayLength().Should().Be(2);
        payload.GetProperty("actorRole").GetString().Should().Be("super_admin");
        payload.GetProperty("actorModule").GetString().Should().Be("plataforma");
    }

    [Fact]
    public async Task AC5_un_403_404_o_409_no_escribe_ningun_evento()
    {
        var delOrganismo = await SeedSignerAsync("Ana", [(_companyA, "organismo")]);
        var conImpacto = await SeedSignerAsync("Beto", [(_companyA, "compania")]);
        await SeedDefaultsAsync(conImpacto);   // es default: la baja tiene impacto y exige confirmación

        AuthenticateCompanyA();
        (await _client.PostAsync(Company(_companyA, delOrganismo, "/inactivate"), null, Ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        AuthenticateSuperAdmin();
        (await _client.PostAsync(Hub(Guid.NewGuid(), "/inactivate"), null, Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _client.DeleteAsync(Hub(conImpacto), Ct)).StatusCode.Should().Be(HttpStatusCode.Conflict);

        (await EventosAsync(delOrganismo)).Should().BeEmpty();
        (await EventosAsync(conImpacto)).Should().BeEmpty();
    }
}
