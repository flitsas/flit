using Flit.Tramites.Application.Tests.ConsolidadoLotes;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// HU #13371 — guarda aditiva <c>soloSiNoExiste</c> en los generadores compartidos (en PDN):
/// <list type="bullet">
///   <item>con <c>true</c> y adjunto del tipo: se devuelve ese con <c>Regenerado=false</c>, sin tocar storage,
///   FUR, banderas ni eventos (AC6, carrera);</item>
///   <item>sin el parámetro (todas las sobrecargas existentes): comportamiento idéntico al actual (AC5).</item>
/// </list>
/// <para>Uso de ejemplo:
/// <c>await wizard.HandleAsync(id, tenantId, userId: null, force: false, bypassSourceUserProtection: false, soloSiNoExiste: true, ct);</c></para>
/// </summary>
public sealed class ConsolidadoSoloSiNoExisteTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly LoteFakeStorage _storage = new();
    private readonly IExpedienteHotDocumentsRegenerator _hotDocs = Substitute.For<IExpedienteHotDocumentsRegenerator>();
    private readonly GenerarConsolidadoHandler _wizard;
    private readonly GenerarConsolidadoMaestroHandler _maestro;

    public ConsolidadoSoloSiNoExisteTests()
    {
        var merger = new LoteFakeMerger();
        _wizard = new GenerarConsolidadoHandler(_repo, merger, _storage, hotDocsRegenerator: _hotDocs);
        _maestro = new GenerarConsolidadoMaestroHandler(_repo, merger, _storage, hotDocsRegenerator: _hotDocs);
    }

    // ── AC6 — con adjunto, la guarda devuelve el existente ──────────────────────────────────────

    [Theory]
    [InlineData("system")]
    [InlineData("user")]
    public async Task AC6_Wizard_SoloSiNoExiste_ConConsolidado_DevuelveElExistente_SinTocarNada(string source)
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instancia(TramiteEstado.Entregado);
        var existente = AddAttachment(instance, "consolidado", source);
        instance.ConsolidadoWizardVigente = false;
        instance.ExpedienteActualizadoEn = DateTimeOffset.UtcNow.AddHours(1); // FUR desactualizado
        Wire(instance);

        var (result, error) = await _wizard.HandleAsync(
            instance.Id, instance.TenantId, userId: null, force: false, bypassSourceUserProtection: false, soloSiNoExiste: true, ct);

        error.Should().BeNull();
        result!.Regenerado.Should().BeFalse();
        result.Document.AttachmentId.Should().Be(existente.Id);
        result.Document.Sha256.Should().Be(existente.Sha256);
        instance.ConsolidadoWizardVigente.Should().BeFalse("la guarda no toca la bandera");
        instance.Events.Should().BeEmpty();
        _storage.Saved.Should().BeEmpty();
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _hotDocs.DidNotReceive().RegenerateHotDocumentsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC6_Maestro_SoloSiNoExiste_ConMaestro_DevuelveElExistente_SinRegenerarFur()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instancia(TramiteEstado.Entregado);
        var existente = AddAttachment(instance, "consolidado_maestro", "system");
        instance.ConsolidadoMaestroVigente = false;
        instance.ExpedienteActualizadoEn = DateTimeOffset.UtcNow.AddHours(1);
        Wire(instance);

        var (result, error) = await _maestro.HandleAsync(instance.Id, instance.TenantId, null, force: false, soloSiNoExiste: true, ct);

        error.Should().BeNull();
        result!.Regenerado.Should().BeFalse();
        result.Document.AttachmentId.Should().Be(existente.Id);
        _storage.Saved.Should().BeEmpty();
        instance.ConsolidadoMaestroVigente.Should().BeFalse();
        await _hotDocs.DidNotReceive().RegenerateHotDocumentsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC6_Maestro_RespetandoRadicacion_PropagaLaGuarda()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instancia(TramiteEstado.Entregado);
        var existente = AddAttachment(instance, "consolidado_maestro", "system");
        instance.ConsolidadoMaestroVigente = false;
        Wire(instance);

        var (result, _) = await _maestro.HandleRespetandoRadicacionAsync(
            instance.Id, instance.TenantId, null, force: false, soloSiNoExiste: true, ct);

        result!.Regenerado.Should().BeFalse();
        result.Document.AttachmentId.Should().Be(existente.Id);
        _storage.Saved.Should().BeEmpty();
    }

    [Fact]
    public async Task AC6_Edge_SoloSiNoExiste_SinAdjunto_GeneraIgualQueSinLaGuarda()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instancia(TramiteEstado.Entregado);
        Wire(instance);

        var (wizard, _) = await _wizard.HandleAsync(
            instance.Id, instance.TenantId, userId: null, force: false, bypassSourceUserProtection: false, soloSiNoExiste: true, ct);
        var (maestro, _) = await _maestro.HandleAsync(instance.Id, instance.TenantId, null, force: false, soloSiNoExiste: true, ct);

        wizard!.Regenerado.Should().BeTrue();
        maestro!.Regenerado.Should().BeTrue();
        _storage.Saved.Should().HaveCount(2);
    }

    [Fact]
    public async Task AC6_Edge_Wizard_MigradoFinal_LaGuardaDeMigradoSigueDelante()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instancia(TramiteEstado.Aprobado);
        instance.IsMigrated = true;
        Wire(instance);

        var (_, error) = await _wizard.HandleAsync(
            instance.Id, instance.TenantId, userId: null, force: false, bypassSourceUserProtection: false, soloSiNoExiste: true, ct);

        error.Should().Be("migrado_solo_lectura");
    }

    // ── AC5 — los llamadores actuales no cambian ────────────────────────────────────────────────

    [Fact]
    public async Task AC5_Wizard_SinElParametro_ConsolidadoNoVigente_RegeneraComoSiempre()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instancia(TramiteEstado.Entregado);
        var previo = AddAttachment(instance, "consolidado", "system");
        instance.ConsolidadoWizardVigente = false;
        Wire(instance);

        var (r1, _) = await _wizard.HandleAsync(instance.Id, instance.TenantId, ct);

        r1!.Regenerado.Should().BeTrue("sin soloSiNoExiste el consolidado no vigente se reconstruye, como hoy");
        r1.Document.AttachmentId.Should().NotBe(previo.Id);
    }

    [Fact]
    public async Task AC5_Wizard_SobrecargaCompletaSinElParametro_ConForce_RegeneraComoSiempre()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instancia(TramiteEstado.Entregado);
        AddAttachment(instance, "consolidado", "system");
        instance.ConsolidadoWizardVigente = true;
        Wire(instance);

        var (result, _) = await _wizard.HandleAsync(
            instance.Id, instance.TenantId, userId: null, force: true, bypassSourceUserProtection: false, ct);

        result!.Regenerado.Should().BeTrue();
        _storage.Saved.Should().ContainSingle();
    }

    [Fact]
    public async Task AC5_Maestro_SinElParametro_MaestroNoVigente_RegeneraComoSiempre()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instancia(TramiteEstado.Entregado);
        var previo = AddAttachment(instance, "consolidado_maestro", "system");
        instance.ConsolidadoMaestroVigente = false;
        Wire(instance);

        var (viaHandle, _) = await _maestro.HandleAsync(instance.Id, instance.TenantId, null, false, ct);

        viaHandle!.Regenerado.Should().BeTrue();
        viaHandle.Document.AttachmentId.Should().NotBe(previo.Id);
    }

    [Fact]
    public async Task AC5_Maestro_RespetandoRadicacionSinElParametro_RegeneraComoSiempre()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instancia(TramiteEstado.Entregado);
        AddAttachment(instance, "consolidado_maestro", "system");
        instance.ConsolidadoMaestroVigente = false;
        Wire(instance);

        var (result, _) = await _maestro.HandleRespetandoRadicacionAsync(instance.Id, instance.TenantId, null, false, ct);

        result!.Regenerado.Should().BeTrue();
    }

    [Fact]
    public async Task AC5_Contrato_ConMaestroVigente_ElAtajoDeCacheNoCambia()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instancia(TramiteEstado.Entregado);
        var vigente = AddAttachment(instance, "consolidado_maestro", "system");
        instance.ConsolidadoMaestroVigente = true;
        Wire(instance);

        var (result, _) = await _maestro.HandleAsync(instance.Id, instance.TenantId, ct: ct);

        result!.Regenerado.Should().BeFalse();
        result.Document.AttachmentId.Should().Be(vigente.Id);
    }

    // ── Infraestructura del test ────────────────────────────────────────────────────────────────

    private void Wire(ProcedureInstance instance)
    {
        _repo.GetByIdWithAttachmentsAsync(instance.Id, instance.TenantId, Arg.Any<CancellationToken>()).Returns(instance);
        _repo.GetByIdWithChecklistGraphAsync(instance.Id, instance.TenantId, Arg.Any<CancellationToken>()).Returns(instance);
    }

    private ProcedureInstance Instancia(string estado)
    {
        var instance = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.For("matricula_inicial"),
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-013371",
            Status = estado,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        AddAttachment(instance, "fur", "system").UploadedAt = DateTimeOffset.UtcNow.AddDays(-3);
        AddAttachment(instance, "factura", "user");
        return instance;
    }

    private ProcedureInstanceAttachment AddAttachment(ProcedureInstance instance, string tipo, string source)
    {
        var path = $"{instance.Id:D}/{tipo}-{Guid.NewGuid():N}";
        var content = System.Text.Encoding.UTF8.GetBytes($"%PDF-{tipo}");
        _storage.Files[path] = content;
        var attachment = new ProcedureInstanceAttachment
        {
            Id = Guid.NewGuid(),
            TenantId = instance.TenantId,
            ProcedureInstanceId = instance.Id,
            Tipo = tipo,
            Filename = $"{tipo}.pdf",
            Mimetype = "application/pdf",
            SizeBytes = content.Length,
            Sha256 = $"sha-{tipo}-{Guid.NewGuid():N}",
            StoragePath = path,
            Source = source,
            UploadedAt = DateTimeOffset.UtcNow,
        };
        instance.Attachments.Add(attachment);
        return attachment;
    }
}
