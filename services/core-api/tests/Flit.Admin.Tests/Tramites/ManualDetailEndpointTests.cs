using System.Net;
using System.Text.Json;
using Flit.Tramites.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace Flit.Admin.Tests.Tramites;

/// <summary>
/// HU #13297 (Feature #13282 C2, Épica #13202) — <c>GET .../biometric-validations/{id}/manual-detail</c> y
/// <c>GET .../{id}/manual-images/{kind}</c> con el pipeline HTTP real y PostgreSQL real: solo Super Admin (200; AdminCompany
/// 403; anónimo 401), cross-tenant (la validación es de otra compañía), 404 (inexistente, kind inválido, sin imagen), cabeceras
/// <c>Cache-Control: no-store</c> y Content-Type real, ciclo actual de imágenes y auditoría <c>manual_imagenes_consultadas</c>
/// sin PII ni rutas de storage.
/// <para>Uso: <c>host.AuthenticateSuperAdmin(); await host.GetAsync($"{Base}/{id}/manual-detail")</c>.</para>
/// </summary>
public sealed class ManualDetailEndpointTests : IClassFixture<ManualReviewFactory>, IDisposable
{
    private const string Base = "/api/v1/tramites/biometric-validations";

    private readonly ManualReviewHost _host;

    public ManualDetailEndpointTests(ManualReviewFactory factory) => _host = new ManualReviewHost(factory);

    public void Dispose() => _host.Dispose();

    private static CancellationToken Ct => ManualReviewHost.Ct;

    [Fact]
    public async Task AC1_SuperAdmin_ve_el_detalle_de_otra_compania_con_las_4_imagenes_y_el_consentimiento()
    {
        var seeded = await _host.SeedAsync();
        _host.AuthenticateSuperAdmin();

        var response = await _host.GetAsync($"{Base}/{seeded.Id}/manual-detail");

        var body = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        root.GetProperty("id").GetGuid().Should().Be(seeded.Id);
        root.GetProperty("fullName").GetString().Should().Be("Persona de prueba");
        root.GetProperty("documentNumber").GetString().Should().Be(seeded.Documento);
        root.GetProperty("tenantName").GetString().Should().Be("Compania duena C");
        root.GetProperty("origin").GetString().Should().Be("prevalidacion");
        root.GetProperty("status").GetString().Should().Be("pendiente_revision_manual");
        root.GetProperty("waitingMinutes").GetInt32().Should().BeGreaterThanOrEqualTo(179);
        root.GetProperty("consentAt").ValueKind.Should().Be(JsonValueKind.String);
        root.GetProperty("consentTextVersion").GetString().Should().Be("v1");
        root.GetProperty("images").EnumerateArray()
            .Select(i => (i.GetProperty("kind").GetString(), i.GetProperty("available").GetBoolean()))
            .Should().Equal(("rostro", true), ("anverso", true), ("reverso", true), ("firma", true));
        root.GetProperty("rejectionReasonCode").ValueKind.Should().Be(JsonValueKind.Null);
        root.GetProperty("linkExpiresAt").ValueKind.Should().Be(JsonValueKind.Null);

        foreach (var path in seeded.Paths.Values)
            body.Should().NotContain(path, "el detalle nunca expone rutas de storage");
        body.Should().NotContain("203.0.113.7", "la IP del consentimiento no sale en el contrato");
    }

    [Fact]
    public async Task AC4_Sin_captura_aun_el_detalle_no_trae_imagenes_y_si_el_vencimiento_del_enlace()
    {
        var seeded = await _host.SeedAsync(BiometricEstados.ManualActivo, conImagenes: false, consentimientoVigente: false);
        _host.AuthenticateSuperAdmin();

        var response = await _host.GetAsync($"{Base}/{seeded.Id}/manual-detail");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        var root = json.RootElement;
        root.GetProperty("images").EnumerateArray().Should().OnlyContain(i => !i.GetProperty("available").GetBoolean());
        root.GetProperty("consentAt").ValueKind.Should().Be(JsonValueKind.Null);
        root.GetProperty("linkExpiresAt").ValueKind.Should().Be(JsonValueKind.String);
    }

    [Fact]
    public async Task Tras_un_rechazo_las_imagenes_del_ciclo_anterior_no_se_muestran_aunque_sigan_en_la_fila()
    {
        // manual_activo con rutas del ciclo anterior (se conservan) y el motivo del último rechazo.
        var seeded = await _host.SeedAsync(
            BiometricEstados.ManualActivo, conImagenes: true, consentimientoVigente: false,
            rejectionReasonCode: "imagen_borrosa", reviewedBy: _host.SuperAdmin);
        _host.AuthenticateSuperAdmin();

        var detail = await _host.GetAsync($"{Base}/{seeded.Id}/manual-detail");
        var image = await _host.GetAsync($"{Base}/{seeded.Id}/manual-images/rostro");

        using var json = JsonDocument.Parse(await detail.Content.ReadAsStringAsync(Ct));
        json.RootElement.GetProperty("images").EnumerateArray().Should().OnlyContain(i => !i.GetProperty("available").GetBoolean());
        json.RootElement.GetProperty("rejectionReasonCode").GetString().Should().Be("imagen_borrosa");
        json.RootElement.GetProperty("reviewedAt").ValueKind.Should().Be(JsonValueKind.String);
        json.RootElement.GetProperty("reviewedBy").GetString().Should().Be(_host.SuperAdmin.ToString(), "sin usuario resoluble cae al id");
        image.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("rostro", "image/jpeg")]
    [InlineData("anverso", "image/jpeg")]
    [InlineData("reverso", "image/jpeg")]
    [InlineData("firma", "image/png")]
    public async Task AC3_Cada_imagen_se_entrega_con_su_content_type_real_y_no_store_y_queda_auditada(string kind, string contentType)
    {
        var seeded = await _host.SeedAsync();
        _host.AuthenticateSuperAdmin();

        var response = await _host.GetAsync($"{Base}/{seeded.Id}/manual-images/{kind}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be(contentType);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        response.Headers.Location.Should().BeNull("sin redirección ni URL firmada");
        (await response.Content.ReadAsByteArrayAsync(Ct)).Should().Equal(kind == "firma" ? ManualReviewHost.PngBytes : ManualReviewHost.JpegBytes);

        var audit = await _host.AuditAsync(seeded.Id);
        var consulta = audit.Should().ContainSingle(a => a.Stage == IdentityValidationAuditStages.ManualImagenesConsultadas).Subject;
        consulta.Outcome.Should().Be("ok");
        consulta.TenantId.Should().Be(_host.Dueno);
        consulta.Detail.Should().Contain($"usuario={_host.SuperAdmin}").And.Contain($"recurso=imagen:{kind}");
        foreach (var path in seeded.Paths.Values)
            (consulta.Message + consulta.Detail).Should().NotContain(path);
        (consulta.Message + consulta.Detail).Should().NotContain("titular13297@example.test").And.NotContain("Persona de prueba");
    }

    [Fact]
    public async Task AC3_Cada_consulta_de_detalle_queda_auditada_sin_pii()
    {
        var seeded = await _host.SeedAsync();
        _host.AuthenticateSuperAdmin();

        await _host.GetAsync($"{Base}/{seeded.Id}/manual-detail");
        await _host.GetAsync($"{Base}/{seeded.Id}/manual-detail");

        var audit = (await _host.AuditAsync(seeded.Id)).Where(a => a.Stage == IdentityValidationAuditStages.ManualImagenesConsultadas).ToList();
        audit.Should().HaveCount(2);
        audit.Should().OnlyContain(a => a.Detail!.Contains("recurso=detalle") && a.Detail.Contains($"usuario={_host.SuperAdmin}"));
        audit.Should().OnlyContain(a => !(a.Message + a.Detail).Contains("Persona de prueba") && !(a.Message + a.Detail).Contains(seeded.Documento));
    }

    [Theory]
    [InlineData("detalle")]
    [InlineData("imagen")]
    public async Task AC2_AdminCompany_recibe_403_y_no_se_audita_ni_se_entrega_nada(string recurso)
    {
        var seeded = await _host.SeedAsync();
        _host.AuthenticateAdminCompany();
        var url = recurso == "detalle" ? $"{Base}/{seeded.Id}/manual-detail" : $"{Base}/{seeded.Id}/manual-images/rostro";

        var response = await _host.GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync(Ct)).Should().NotContain("fm-");
        response.Content.Headers.ContentType!.MediaType.Should().NotStartWith("image/");
        (await _host.AuditAsync(seeded.Id)).Should().BeEmpty();
    }

    [Theory]
    [InlineData("manual-detail")]
    [InlineData("manual-images/rostro")]
    public async Task Anonimo_recibe_401(string sufijo)
    {
        var seeded = await _host.SeedAsync();
        _host.Anonymous();

        var response = await _host.GetAsync($"{Base}/{seeded.Id}/{sufijo}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await _host.AuditAsync(seeded.Id)).Should().BeEmpty();
    }

    [Theory]
    [InlineData("manual-detail")]
    [InlineData("manual-images/rostro")]
    public async Task Validacion_inexistente_responde_404(string sufijo)
    {
        _host.AuthenticateSuperAdmin();

        var response = await _host.GetAsync($"{Base}/{Guid.NewGuid()}/{sufijo}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("selfie")]
    [InlineData("ROSTRO2")]
    [InlineData("..%2Frostro")]
    public async Task Kind_fuera_de_los_4_responde_404_sin_auditar(string kind)
    {
        var seeded = await _host.SeedAsync();
        _host.AuthenticateSuperAdmin();

        var response = await _host.GetAsync($"{Base}/{seeded.Id}/manual-images/{kind}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _host.AuditAsync(seeded.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task Imagen_cuyo_binario_se_perdio_en_el_storage_responde_404_sin_auditar()
    {
        var seeded = await _host.SeedAsync();
        _host.Factory.Storage.Delete(seeded.Paths["firma"]);
        _host.AuthenticateSuperAdmin();

        var response = await _host.GetAsync($"{Base}/{seeded.Id}/manual-images/firma");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _host.AuditAsync(seeded.Id)).Should().BeEmpty();
    }

    [Theory]
    [InlineData(BiometricProviders.Kyverum, BiometricEstados.EnProceso)]
    [InlineData(BiometricProviders.Mock, BiometricEstados.Aprobado)]
    public async Task Una_validacion_que_no_es_del_flujo_manual_responde_404(string provider, string status)
    {
        var seeded = await _host.SeedAsync(status, provider);
        _host.AuthenticateSuperAdmin();

        var detail = await _host.GetAsync($"{Base}/{seeded.Id}/manual-detail");

        detail.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Aprobada_manualmente_conserva_las_imagenes_visibles()
    {
        var seeded = await _host.SeedAsync(
            BiometricEstados.Aprobado, approvalOrigin: BiometricApprovalOrigins.Manual, reviewedBy: _host.SuperAdmin);
        _host.AuthenticateSuperAdmin();

        var detail = await _host.GetAsync($"{Base}/{seeded.Id}/manual-detail");

        using var json = JsonDocument.Parse(await detail.Content.ReadAsStringAsync(Ct));
        json.RootElement.GetProperty("images").EnumerateArray().Should().OnlyContain(i => i.GetProperty("available").GetBoolean());
        json.RootElement.GetProperty("waitingMinutes").GetInt32().Should().Be(0);
    }
}
