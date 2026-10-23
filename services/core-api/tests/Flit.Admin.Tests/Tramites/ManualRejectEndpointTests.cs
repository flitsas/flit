using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Identity;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Admin.Tests.Tramites;

/// <summary>
/// HU #13299 (Feature #13282 C4, Épica #13202) — <c>POST .../biometric-validations/{id}/manual-reject</c> y
/// <c>GET .../manual-rejection-reasons</c> con el pipeline HTTP real y PostgreSQL real (correo simulado): solo Super Admin (200;
/// AdminCompany 403; anónimo 401), cross-tenant, 404, 400 <c>motivo_invalido</c>, 409, rechazo que deja la fila en <c>rechazado</c>
/// con motivo y un enlace nuevo (solo hash; correo con el motivo y el enlace), captura repetida con ese enlace estando rechazada
/// (la captura pública real), conservación del motivo hasta esa captura, reenvío del enlace, rechazos consecutivos sin tope, fallo
/// de correo sin revertir y auditoría sin PII, rutas ni token.
/// <para>Uso: <c>host.AuthenticateSuperAdmin(); await host.Client.PostAsJsonAsync($"{Base}/{id}/manual-reject", new { reasonCode })</c>.</para>
/// </summary>
public sealed class ManualRejectEndpointTests : IClassFixture<ManualReviewFactory>, IDisposable
{
    private const string Base = "/api/v1/tramites/biometric-validations";

    private readonly ManualReviewHost _host;

    public ManualRejectEndpointTests(ManualReviewFactory factory) => _host = new ManualReviewHost(factory);

    public void Dispose() => _host.Dispose();

    private static CancellationToken Ct => ManualReviewHost.Ct;

    private Task<HttpResponseMessage> RejectAsync(Guid id, string? reasonCode) =>
        _host.Client.PostAsJsonAsync($"{Base}/{id}/manual-reject", new { reasonCode }, Ct);

    [Fact]
    public async Task AC1_AC4_SuperAdmin_rechaza_una_validacion_de_otra_compania_y_queda_rechazada_con_enlace_nuevo()
    {
        var seeded = await _host.SeedAsync();
        _host.AuthenticateSuperAdmin();
        var antes = DateTimeOffset.UtcNow;

        var response = await RejectAsync(seeded.Id, "rostro_no_coincide");

        var body = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        using var json = JsonDocument.Parse(body);
        json.RootElement.GetProperty("status").GetString().Should().Be("rechazado");
        json.RootElement.GetProperty("rejectionReasonCode").GetString().Should().Be("rostro_no_coincide");
        json.RootElement.GetProperty("emailEnviado").GetBoolean().Should().BeTrue();
        json.RootElement.GetProperty("linkExpiresAt").GetDateTimeOffset()
            .Should().BeCloseTo(antes.AddHours(BiometricRules.TokenTtlHoras), TimeSpan.FromMinutes(2));

        var v = await _host.ReloadAsync(seeded.Id);
        v.Status.Should().Be(BiometricEstados.Rechazado);
        v.Provider.Should().Be("manual");
        v.RejectionReasonCode.Should().Be("rostro_no_coincide");
        v.ReviewedBy.Should().Be(_host.SuperAdmin);
        v.ReviewedAt.Should().BeOnOrAfter(antes.AddSeconds(-5));
        v.ExpiresAt.Should().BeCloseTo(antes.AddHours(BiometricRules.TokenTtlHoras), TimeSpan.FromMinutes(2));
        v.TokenHash.Should().NotBe(seeded.TokenHash).And.HaveLength(64);
        v.TenantId.Should().Be(_host.Dueno);
        v.ApprovalOrigin.Should().BeNull();
    }

    [Fact]
    public async Task AC4_El_cliente_recibe_el_motivo_legible_y_un_token_nuevo_que_solo_existe_como_hash()
    {
        var seeded = await _host.SeedAsync();
        _host.AuthenticateSuperAdmin();

        (await RejectAsync(seeded.Id, "documento_no_corresponde")).StatusCode.Should().Be(HttpStatusCode.OK);

        var enviado = _host.Factory.Notifier.Enviados.Should().ContainSingle(l => l.ValidationId == seeded.Id).Subject;
        enviado.RejectionReasonLabel.Should().Be("El documento no corresponde");
        enviado.RecipientEmail.Should().Be("titular13297@example.test");
        enviado.TenantId.Should().Be(_host.Dueno);
        var v = await _host.ReloadAsync(seeded.Id);
        v.TokenHash.Should().Be(BiometricToken.Hash(enviado.Token)).And.NotBe(enviado.Token);
        enviado.ExpiresAt.Should().BeCloseTo(v.ExpiresAt, TimeSpan.FromMilliseconds(5));

        // El token viejo ya no corresponde a la fila: el público lo recibe como enlace inválido.
        using var scope = _host.Factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IProcedureInstanceRepository>();
        (await repo.GetBiometricByTokenHashAsync(seeded.TokenHash, Ct)).Should().BeNull();
        (await repo.GetBiometricByTokenHashAsync(BiometricToken.Hash(enviado.Token), Ct)).Should().NotBeNull();
    }

    [Fact]
    public async Task El_detalle_tras_rechazar_muestra_rechazado_con_el_motivo_las_imagenes_rechazadas_y_el_vencimiento_del_enlace()
    {
        var seeded = await _host.SeedAsync();
        _host.AuthenticateSuperAdmin();
        (await RejectAsync(seeded.Id, "imagen_borrosa")).StatusCode.Should().Be(HttpStatusCode.OK);

        var detail = await _host.GetAsync($"{Base}/{seeded.Id}/manual-detail");

        detail.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await detail.Content.ReadAsStringAsync(Ct));
        var root = json.RootElement;
        root.GetProperty("status").GetString().Should().Be("rechazado");
        root.GetProperty("rejectionReasonCode").GetString().Should().Be("imagen_borrosa");
        root.GetProperty("reviewedAt").ValueKind.Should().Be(JsonValueKind.String);
        root.GetProperty("linkExpiresAt").ValueKind.Should().Be(JsonValueKind.String);
        root.GetProperty("consentAt").ValueKind.Should().Be(JsonValueKind.Null, "el consentimiento del ciclo anterior ya no cuenta");
        root.GetProperty("images").EnumerateArray().Should().OnlyContain(i => i.GetProperty("available").GetBoolean());
    }

    [Fact]
    public async Task El_motivo_se_conserva_hasta_que_la_captura_siguiente_lo_limpia()
    {
        var seeded = await _host.SeedAsync();
        _host.AuthenticateSuperAdmin();
        (await RejectAsync(seeded.Id, "captura_fuera_de_encuadre")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _host.ReloadAsync(seeded.Id)).RejectionReasonCode.Should().Be("captura_fuera_de_encuadre");

        // El cliente repite la captura (mismo camino de persistencia atómica que la captura pública, HU #13290).
        using (var scope = _host.Factory.Services.CreateScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IProcedureInstanceRepository>();
            var v = (await repo.GetBiometricByIdAsync(seeded.Id, Ct))!;
            var ahora = DateTimeOffset.UtcNow;
            v.RegistrarConsentimientoManual(ManualCaptureConsent.TextVersion, "203.0.113.8", ahora);
            v.RegistrarCapturaManual("fm-r2", "fm-a2", "fm-v2", "fm-f2", new string('7', 64), ahora);
            (await repo.TryPersistManualCaptureAsync(v, Ct)).Should().BeTrue();
        }

        var recargada = await _host.ReloadAsync(seeded.Id);
        recargada.Status.Should().Be(BiometricEstados.PendienteRevisionManual);
        recargada.RejectionReasonCode.Should().BeNull();
        recargada.ReviewedBy.Should().BeNull();
        recargada.ReviewedAt.Should().BeNull();
        recargada.FacePhotoPath.Should().Be("fm-r2");
    }

    [Fact]
    public async Task AC5_Rechazos_consecutivos_emiten_cada_vez_un_enlace_nuevo_sin_tope()
    {
        var seeded = await _host.SeedAsync();
        _host.AuthenticateSuperAdmin();
        var motivos = ManualRejectionReasons.Todos.Select(m => m.Code).ToList();

        for (var i = 0; i < 4; i++)
        {
            (await RejectAsync(seeded.Id, motivos[i])).StatusCode.Should().Be(HttpStatusCode.OK);
            (await _host.ReloadAsync(seeded.Id)).RejectionReasonCode.Should().Be(motivos[i]);

            using var scope = _host.Factory.Services.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IProcedureInstanceRepository>();
            var v = (await repo.GetBiometricByIdAsync(seeded.Id, Ct))!;
            var ahora = DateTimeOffset.UtcNow;
            v.RegistrarConsentimientoManual(ManualCaptureConsent.TextVersion, null, ahora);
            v.RegistrarCapturaManual($"fm-r{i}", $"fm-a{i}", $"fm-v{i}", $"fm-f{i}", new string('7', 64), ahora);
            (await repo.TryPersistManualCaptureAsync(v, Ct)).Should().BeTrue();
        }

        var enviados = _host.Factory.Notifier.Enviados.Where(l => l.ValidationId == seeded.Id).ToList();
        enviados.Should().HaveCount(4);
        enviados.Select(l => l.Token).Should().OnlyHaveUniqueItems();
        (await _host.AuditAsync(seeded.Id)).Count(a => a.Stage == IdentityValidationAuditStages.ManualRechazado).Should().Be(4);
    }

    [Fact]
    public async Task El_Super_Admin_puede_reenviar_el_enlace_de_una_rechazada_y_sigue_rechazada()
    {
        var seeded = await _host.SeedAsync();
        _host.AuthenticateSuperAdmin();
        (await RejectAsync(seeded.Id, "imagen_borrosa")).StatusCode.Should().Be(HttpStatusCode.OK);
        var primero = _host.Factory.Notifier.Enviados.Single(l => l.ValidationId == seeded.Id).Token;

        var response = await _host.PostAsync($"{Base}/{seeded.Id}/regenerate-manual-link");

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        var v = await _host.ReloadAsync(seeded.Id);
        v.Status.Should().Be(BiometricEstados.Rechazado);
        v.RejectionReasonCode.Should().Be("imagen_borrosa");
        var segundo = _host.Factory.Notifier.Enviados.Last(l => l.ValidationId == seeded.Id).Token;
        segundo.Should().NotBe(primero);
        v.TokenHash.Should().Be(BiometricToken.Hash(segundo));
    }

    [Fact]
    public async Task La_captura_publica_con_el_enlace_nuevo_funciona_estando_rechazada()
    {
        var seeded = await _host.SeedAsync();
        _host.AuthenticateSuperAdmin();
        (await RejectAsync(seeded.Id, "imagen_borrosa")).StatusCode.Should().Be(HttpStatusCode.OK);
        var token = _host.Factory.Notifier.Enviados.Single(l => l.ValidationId == seeded.Id).Token;
        using var publico = _host.Factory.CreateClient();

        var vista = await publico.GetAsync($"/api/v1/public/manual-capture/{token}", Ct);
        vista.StatusCode.Should().Be(HttpStatusCode.OK, await vista.Content.ReadAsStringAsync(Ct));

        // El enlace viejo ya no vale.
        (await publico.GetAsync($"/api/v1/public/manual-capture/{seeded.TokenHash}", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Audita_manual_rechazado_con_el_codigo_sin_PII_rutas_ni_token()
    {
        var seeded = await _host.SeedAsync();
        _host.AuthenticateSuperAdmin();

        (await RejectAsync(seeded.Id, "firma_ilegible_o_no_corresponde")).StatusCode.Should().Be(HttpStatusCode.OK);

        var e = (await _host.AuditAsync(seeded.Id)).Should()
            .ContainSingle(a => a.Stage == IdentityValidationAuditStages.ManualRechazado).Subject;
        e.TenantId.Should().Be(_host.Dueno);
        e.Detail.Should().Contain($"usuario={_host.SuperAdmin}").And.Contain("motivo=firma_ilegible_o_no_corresponde");
        var token = _host.Factory.Notifier.Enviados.Single(l => l.ValidationId == seeded.Id).Token;
        var texto = e.Message + e.Detail;
        texto.Should().NotContain("Persona de prueba").And.NotContain(seeded.Documento)
            .And.NotContain("titular13297@example.test").And.NotContain(token).And.NotContain(seeded.TokenHash);
        foreach (var path in seeded.Paths.Values)
            texto.Should().NotContain(path);
    }

    [Fact]
    public async Task El_correo_no_sale_no_revierte_el_rechazo_audita_manual_correo_fallido_y_responde_emailEnviado_false()
    {
        var seeded = await _host.SeedAsync();
        _host.Factory.Notifier.Resultado = false;
        _host.AuthenticateSuperAdmin();

        var response = await RejectAsync(seeded.Id, "imagen_borrosa");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        json.RootElement.GetProperty("emailEnviado").GetBoolean().Should().BeFalse();
        (await _host.ReloadAsync(seeded.Id)).Status.Should().Be(BiometricEstados.Rechazado);
        (await _host.AuditAsync(seeded.Id)).Select(a => a.Stage).Should()
            .Contain(IdentityValidationAuditStages.ManualRechazado).And.Contain(IdentityValidationAuditStages.ManualCorreoFallido);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"reasonCode\":null}")]
    [InlineData("{\"reasonCode\":\"\"}")]
    [InlineData("{\"reasonCode\":\"no_existe\"}")]
    [InlineData("{\"reasonCode\":\"IMAGEN_BORROSA\"}")]
    public async Task AC2_AC3_Motivo_ausente_o_fuera_de_la_lista_responde_400_motivo_invalido_y_no_cambia_nada(string cuerpo)
    {
        var seeded = await _host.SeedAsync();
        _host.AuthenticateSuperAdmin();

        var response = await _host.PostAsync(
            $"{Base}/{seeded.Id}/manual-reject", new StringContent(cuerpo, Encoding.UTF8, "application/json"));

        var body = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, body);
        using var json = JsonDocument.Parse(body);
        json.RootElement.GetProperty("code").GetString().Should().Be("motivo_invalido");
        var v = await _host.ReloadAsync(seeded.Id);
        v.Status.Should().Be(BiometricEstados.PendienteRevisionManual);
        v.TokenHash.Should().Be(seeded.TokenHash);
        v.RejectionReasonCode.Should().BeNull();
        _host.Factory.Notifier.Enviados.Should().BeEmpty();
    }

    [Fact]
    public async Task Sin_cuerpo_responde_400_motivo_invalido()
    {
        var seeded = await _host.SeedAsync();
        _host.AuthenticateSuperAdmin();

        var response = await _host.PostAsync($"{Base}/{seeded.Id}/manual-reject");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await _host.ReloadAsync(seeded.Id)).Status.Should().Be(BiometricEstados.PendienteRevisionManual);
    }

    [Fact]
    public async Task AdminCompany_recibe_403_y_no_cambia_nada()
    {
        var seeded = await _host.SeedAsync();
        _host.AuthenticateAdminCompany();

        var response = await RejectAsync(seeded.Id, "imagen_borrosa");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await _host.ReloadAsync(seeded.Id)).Status.Should().Be(BiometricEstados.PendienteRevisionManual);
        (await _host.AuditAsync(seeded.Id)).Should().BeEmpty();
        _host.Factory.Notifier.Enviados.Should().BeEmpty();
    }

    [Fact]
    public async Task Anonimo_recibe_401_y_no_cambia_nada()
    {
        var seeded = await _host.SeedAsync();
        _host.Anonymous();

        var response = await RejectAsync(seeded.Id, "imagen_borrosa");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await _host.ReloadAsync(seeded.Id)).Status.Should().Be(BiometricEstados.PendienteRevisionManual);
    }

    [Fact]
    public async Task Validacion_inexistente_responde_404()
    {
        _host.AuthenticateSuperAdmin();

        var response = await RejectAsync(Guid.NewGuid(), "imagen_borrosa");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData(BiometricProviders.Manual, BiometricEstados.ManualActivo)]
    [InlineData(BiometricProviders.Manual, BiometricEstados.Aprobado)]
    [InlineData(BiometricProviders.Kyverum, BiometricEstados.EnProceso)]
    public async Task Fuera_de_pendiente_de_revision_responde_409_estado_invalido_sin_cambios_ni_correo(string provider, string status)
    {
        var seeded = await _host.SeedAsync(status, provider, conImagenes: false);
        _host.AuthenticateSuperAdmin();

        var response = await RejectAsync(seeded.Id, "imagen_borrosa");

        var body = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict, body);
        using var json = JsonDocument.Parse(body);
        json.RootElement.GetProperty("code").GetString().Should().Be("estado_invalido");
        var v = await _host.ReloadAsync(seeded.Id);
        v.Status.Should().Be(status);
        v.TokenHash.Should().Be(seeded.TokenHash);
        v.RejectionReasonCode.Should().BeNull();
        _host.Factory.Notifier.Enviados.Should().BeEmpty();
    }

    [Fact]
    public async Task AC6_El_catalogo_de_motivos_lo_ve_solo_el_Super_Admin_con_codigo_y_texto()
    {
        _host.AuthenticateSuperAdmin();

        var response = await _host.GetAsync($"{Base}/manual-rejection-reasons");

        var body = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        using var json = JsonDocument.Parse(body);
        json.RootElement.EnumerateArray()
            .Select(i => (i.GetProperty("code").GetString(), i.GetProperty("label").GetString()))
            .Should().Equal(ManualRejectionReasons.Todos.Select(r => (r.Code, (string?)r.Label)));
        json.RootElement.GetArrayLength().Should().Be(6);
    }

    [Fact]
    public async Task AC6_El_catalogo_responde_403_a_AdminCompany_y_401_al_anonimo()
    {
        _host.AuthenticateAdminCompany();
        (await _host.GetAsync($"{Base}/manual-rejection-reasons")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        _host.Anonymous();
        (await _host.GetAsync($"{Base}/manual-rejection-reasons")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
