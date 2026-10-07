using System.Net;
using System.Text.Json;
using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Application.UseCases.Persons;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Admin.Tests.Tramites;

/// <summary>
/// HU #13298 (Feature #13282 C3, Épica #13202) — <c>POST .../biometric-validations/{id}/manual-approve</c> con el pipeline HTTP
/// real y PostgreSQL real: solo Super Admin (200; AdminCompany 403; anónimo 401), cross-tenant, 404, 409 fuera de
/// <c>pendiente_revision_manual</c>, aprobación por el camino de Kyverum (30 días, evento de completado en la outbox, origen
/// <c>manual</c>, revisor), reuso por tenant+documento (lo que lee el gate de radicación), imágenes y firma intactas y legibles para
/// los documentos, y auditoría <c>manual_aprobado</c> sin PII ni rutas.
/// <para>Uso: <c>host.AuthenticateSuperAdmin(); await host.PostAsync($"{Base}/{id}/manual-approve")</c>.</para>
/// </summary>
public sealed class ManualApproveEndpointTests : IClassFixture<ManualReviewFactory>, IDisposable
{
    private const string Base = "/api/v1/tramites/biometric-validations";

    private readonly ManualReviewHost _host;

    public ManualApproveEndpointTests(ManualReviewFactory factory) => _host = new ManualReviewHost(factory);

    public void Dispose() => _host.Dispose();

    private static CancellationToken Ct => ManualReviewHost.Ct;

    [Fact]
    public async Task AC1_SuperAdmin_aprueba_una_validacion_de_otra_compania_con_30_dias_origen_manual_y_revisor()
    {
        var seeded = await _host.SeedAsync();
        _host.AuthenticateSuperAdmin();
        var antes = DateTimeOffset.UtcNow;

        var response = await _host.PostAsync($"{Base}/{seeded.Id}/manual-approve");

        var body = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        using var json = JsonDocument.Parse(body);
        json.RootElement.GetProperty("status").GetString().Should().Be("aprobado");
        json.RootElement.GetProperty("approvalOrigin").GetString().Should().Be("manual");

        var v = await _host.ReloadAsync(seeded.Id);
        v.Status.Should().Be(BiometricEstados.Aprobado);
        v.ApprovalOrigin.Should().Be("manual");
        v.ReviewedBy.Should().Be(_host.SuperAdmin);
        v.ReviewedAt.Should().BeOnOrAfter(antes.AddSeconds(-5));
        v.ValidatedAt.Should().BeOnOrAfter(antes.AddSeconds(-5));
        v.ValidUntil.Should().Be(BiometricRules.FechaFinVigencia(v.ValidatedAt!.Value));
        BiometricRules.EsAprobadaVigente(v, DateTimeOffset.UtcNow).Should().BeTrue();
        v.TenantId.Should().Be(_host.Dueno, "el tenant dueño sale de la fila, no del Super Admin");
        v.Provider.Should().Be("manual");
    }

    [Fact]
    public async Task AC1_Encola_el_evento_de_completado_por_el_mismo_camino_que_Kyverum_y_audita_sin_PII()
    {
        var seeded = await _host.SeedAsync();
        _host.AuthenticateSuperAdmin();

        var response = await _host.PostAsync($"{Base}/{seeded.Id}/manual-approve");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await using var db = _host.NewDb();
        var evento = await db.IdentityValidationOutbox.AsNoTracking()
            .SingleAsync(o => o.ValidationId == seeded.Id && o.EventType == IdentityValidationEventTypes.Completed, Ct);
        evento.TenantId.Should().Be(_host.Dueno);
        using var payload = JsonDocument.Parse(evento.Payload);
        payload.RootElement.GetProperty("estado").GetString().Should().Be("aprobado");

        var auditoria = (await _host.AuditAsync(seeded.Id)).Where(a => a.Stage == IdentityValidationAuditStages.ManualAprobado).ToList();
        var e = auditoria.Should().ContainSingle().Subject;
        e.TenantId.Should().Be(_host.Dueno);
        e.Detail.Should().Contain($"usuario={_host.SuperAdmin}");
        var texto = e.Message + e.Detail;
        texto.Should().NotContain("Persona de prueba").And.NotContain(seeded.Documento).And.NotContain("titular13297@example.test");
        foreach (var path in seeded.Paths.Values)
            texto.Should().NotContain(path, "la auditoría nunca lleva rutas de storage");
    }

    [Fact]
    public async Task AC3_La_identidad_aprobada_manualmente_se_reutiliza_por_tenant_y_documento_igual_que_la_de_Kyverum()
    {
        var seeded = await _host.SeedAsync();
        using var scope = _host.Factory.Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IdentityVigenciaPorDocumentoResolver>();
        (await resolver.ResolveAsync(_host.Dueno, "CC", seeded.Documento, DateTimeOffset.UtcNow, Ct)).Status
            .Should().Be(IdentityVigenciaEstados.EnCurso, "antes de aprobar no vale para ningún trámite");

        _host.AuthenticateSuperAdmin();
        (await _host.PostAsync($"{Base}/{seeded.Id}/manual-approve")).StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope2 = _host.Factory.Services.CreateScope();
        var vigencia = await scope2.ServiceProvider.GetRequiredService<IdentityVigenciaPorDocumentoResolver>()
            .ResolveAsync(_host.Dueno, "CC", seeded.Documento, DateTimeOffset.UtcNow, Ct);
        vigencia.Status.Should().Be(IdentityVigenciaEstados.AprobadaVigente);
        vigencia.ValidUntil.Should().NotBeNull();

        // Lo que consulta el gate de radicación (identidad referenciada de la persona en el tenant) la encuentra.
        var repo = scope2.ServiceProvider.GetRequiredService<IProcedureInstanceRepository>();
        var referenciada = await repo.FindVigenteApprovedByDocumentAsync(_host.Dueno, "CC", seeded.Documento, DateTimeOffset.UtcNow, Ct);
        referenciada.Should().NotBeNull();
        referenciada!.Id.Should().Be(seeded.Id);
        referenciada.ApprovalOrigin.Should().Be("manual");

        // Otro tenant con el mismo documento no la ve: el reuso es por tenant.
        (await repo.FindVigenteApprovedByDocumentAsync(_host.TenantSuperAdmin, "CC", seeded.Documento, DateTimeOffset.UtcNow, Ct))
            .Should().BeNull();
    }

    [Fact]
    public async Task AC5_Las_fotos_y_la_firma_quedan_en_la_fila_y_la_firma_es_un_PNG_legible_como_la_de_Kyverum()
    {
        var seeded = await _host.SeedAsync();
        _host.AuthenticateSuperAdmin();

        (await _host.PostAsync($"{Base}/{seeded.Id}/manual-approve")).StatusCode.Should().Be(HttpStatusCode.OK);

        var v = await _host.ReloadAsync(seeded.Id);
        v.FacePhotoPath.Should().Be(seeded.Paths["rostro"]);
        v.IdFrontPhotoPath.Should().Be(seeded.Paths["anverso"]);
        v.IdBackPhotoPath.Should().Be(seeded.Paths["reverso"]);
        v.SignatureImagePath.Should().Be(seeded.Paths["firma"]);

        // Así lee la firma el FUR (IAttachmentStorage.OpenReadAsync sobre SignatureImagePath + formato soportado).
        var storage = _host.Factory.Services.GetRequiredService<IAttachmentStorage>();
        await using var stream = await storage.OpenReadAsync(v.SignatureImagePath!, Ct);
        stream.Should().NotBeNull();
        using var ms = new MemoryStream();
        await stream!.CopyToAsync(ms, Ct);
        IdentitySignatureImageFormat.IsSupported(ms.ToArray()).Should().BeTrue();
    }

    [Fact]
    public async Task AC2_AdminCompany_recibe_403_y_no_cambia_nada()
    {
        var seeded = await _host.SeedAsync();
        _host.AuthenticateAdminCompany();

        var response = await _host.PostAsync($"{Base}/{seeded.Id}/manual-approve");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var v = await _host.ReloadAsync(seeded.Id);
        v.Status.Should().Be(BiometricEstados.PendienteRevisionManual);
        v.ApprovalOrigin.Should().BeNull();
        (await _host.AuditAsync(seeded.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task Anonimo_recibe_401_y_no_cambia_nada()
    {
        var seeded = await _host.SeedAsync();
        _host.Anonymous();

        var response = await _host.PostAsync($"{Base}/{seeded.Id}/manual-approve");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await _host.ReloadAsync(seeded.Id)).Status.Should().Be(BiometricEstados.PendienteRevisionManual);
    }

    [Fact]
    public async Task Validacion_inexistente_responde_404()
    {
        _host.AuthenticateSuperAdmin();

        var response = await _host.PostAsync($"{Base}/{Guid.NewGuid()}/manual-approve");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData(BiometricProviders.Manual, BiometricEstados.ManualActivo)]
    [InlineData(BiometricProviders.Manual, BiometricEstados.Aprobado)]
    [InlineData(BiometricProviders.Kyverum, BiometricEstados.EnProceso)]
    public async Task AC4_Fuera_de_pendiente_de_revision_responde_409_estado_invalido_sin_cambios(string provider, string status)
    {
        var seeded = await _host.SeedAsync(status, provider, conImagenes: false);
        _host.AuthenticateSuperAdmin();

        var response = await _host.PostAsync($"{Base}/{seeded.Id}/manual-approve");

        var body = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict, body);
        using var json = JsonDocument.Parse(body);
        json.RootElement.GetProperty("code").GetString().Should().Be("estado_invalido");
        var v = await _host.ReloadAsync(seeded.Id);
        v.Status.Should().Be(status);
        v.ReviewedBy.Should().BeNull();
        v.ApprovalOrigin.Should().BeNull();
        await using var db = _host.NewDb();
        (await db.IdentityValidationOutbox.AnyAsync(o => o.ValidationId == seeded.Id, Ct)).Should().BeFalse();
    }

    [Fact]
    public async Task Aprobar_dos_veces_la_segunda_responde_409_y_no_duplica_el_evento()
    {
        var seeded = await _host.SeedAsync();
        _host.AuthenticateSuperAdmin();

        (await _host.PostAsync($"{Base}/{seeded.Id}/manual-approve")).StatusCode.Should().Be(HttpStatusCode.OK);
        var segunda = await _host.PostAsync($"{Base}/{seeded.Id}/manual-approve");

        segunda.StatusCode.Should().Be(HttpStatusCode.Conflict);
        await using var db = _host.NewDb();
        (await db.IdentityValidationOutbox.CountAsync(o => o.ValidationId == seeded.Id, Ct)).Should().Be(1);
    }
}
