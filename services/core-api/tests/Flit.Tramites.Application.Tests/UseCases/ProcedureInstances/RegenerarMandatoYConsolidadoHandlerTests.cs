using System.Text;
using Flit.Tramites.Application.Documents;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Documents;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Catalog;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Application.Identity;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// HU #14547 — «Regenerar mandato y consolidado» (solo SuperAdmin). El mandato se vuelve a emitir con el
/// mandatario que indica HOY la prelación de ADR-0066 (en cualquier estado no final), sin tocar el FUR ni el
/// resto del expediente, y se rearman el consolidado del wizard (sin cascada del FUR) y el maestro del OT
/// (respetando el radicado ante Quipux).
/// <para>Uso de ejemplo:
/// <c>var (r, e) = await new RegenerarMandatoYConsolidadoHandler(repo, furHandler, wizard, maestro, precedencia)
/// .HandleAsync(id, tenantId, userId, ct);</c></para>
/// </summary>
public sealed class RegenerarMandatoYConsolidadoHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid InstanceId = Guid.NewGuid();
    private static readonly Guid Ot = Guid.NewGuid();
    private static readonly Guid TypeId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid Ana = Guid.Parse("cccccccc-3333-4000-8000-000000000001");
    private static readonly Guid Carlos = Guid.Parse("cccccccc-3333-4000-8000-000000000002");

    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly FakeStorage _storage = new();
    private readonly FakeMerger _merger = new();
    private readonly CapturingMandatoGenerator _mandatoGenerator = new();
    private readonly IMandateRequirementPolicy _policy = Substitute.For<IMandateRequirementPolicy>();
    private readonly IExpedienteHotDocumentsRegenerator _hotDocs = Substitute.For<IExpedienteHotDocumentsRegenerator>();
    private readonly IMaestroRadicadoLookup _radicado = Substitute.For<IMaestroRadicadoLookup>();
    private readonly IConsolidadoMaestroPrecedenciaProvider _precedencia = Substitute.For<IConsolidadoMaestroPrecedenciaProvider>();
    private static readonly string[] OtrosDocumentos =
        ["compraventa", "tramite_virtual", "certificado_soat_rtm", "certificado_identidad", "escritura"];

    private readonly List<ProcedureInstanceEvent> _eventos = [];
    private readonly IPersonalizedDocumentResolver _personalizados = Substitute.For<IPersonalizedDocumentResolver>();

    /// <summary>Por cada SaveChanges: ¿ya estaba el mandato nuevo? ¿y el evento mandato_regenerado_admin?</summary>
    private readonly List<(bool MandatoNuevo, bool Evento)> _guardados = [];
    private ProcedureInstance? _instancia;

    private Directorio _directorio = new()
    {
        SoloPorId =
        [
            new MandateSignerCandidate(Ana, "Ana Restrepo", "111000111", null),
            new MandateSignerCandidate(Carlos, "Carlos Pérez Demo", "222000222", null),
        ],
    };

    public RegenerarMandatoYConsolidadoHandlerTests()
    {
        ConfigurarDefaultDelOt(Carlos);
        _radicado.AttachmentRadicadoAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((Guid?)null);
        _radicado.AttachmentsProtegidosAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlySet<Guid>)new HashSet<Guid>());
        _precedencia.ResolverAsync(Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<string>)["fur", "mandato"]);
        _repo.When(r => r.AddEventAsync(Arg.Any<ProcedureInstanceEvent>(), Arg.Any<CancellationToken>()))
            .Do(c => _eventos.Add(c.Arg<ProcedureInstanceEvent>()));
        _personalizados.ResolveAsync(Arg.Any<Guid>(), Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(PersonalizedDocumentResolution.Empty);
        _repo.When(r => r.SaveChangesAsync(Arg.Any<CancellationToken>())).Do(_ =>
        {
            if (_instancia is null)
                return;
            _guardados.Add((
                _instancia.Attachments.Any(a => a.Tipo == "mandato" && a.StoragePath.Contains("_nuevo_", StringComparison.Ordinal)),
                _instancia.Events.Any(e => e.Tipo == RegenerarMandatoYConsolidadoHandler.EventoMandatoRegenerado)));
        });
    }

    // ── AC1 — el mandatario se recalcula y sobrescribe MandateSignerId ─────────────────────────────

    [Fact]
    public async Task AC1_FueraDeBorrador_RecalculaElMandatarioConLaConfigVigente_YSobrescribeMandateSignerId()
    {
        var instance = Seed(TramiteEstado.Entregado, guardado: Ana);

        var (result, error) = await Handler().HandleAsync(InstanceId, TenantId, UserId, TestContext.Current.CancellationToken);

        error.Should().BeNull();
        _mandatoGenerator.Captured!.Mandatario!.Documento.Should().Be(
            "222000222", "la acción del SuperAdmin aplica la prelación contra la configuración vigente (R1)");
        instance.MandateSignerId.Should().Be(Carlos);
        result!.Mandato.FirmanteAnteriorId.Should().Be(Ana);
        result.Mandato.FirmanteNuevoId.Should().Be(Carlos);
        result.Mandato.SinFirmar.Should().BeFalse();
        result.Mandato.AttachmentId.Should().NotBeNull();
        instance.Attachments.Should().ContainSingle(a => a.Tipo == "mandato")
            .Which.Id.Should().Be(result.Mandato.AttachmentId!.Value);
    }

    [Fact]
    public async Task AC1_RegistraElEventoMandatoRegeneradoAdmin_ConFirmanteAnteriorYNuevo_SinPii()
    {
        var instance = Seed(TramiteEstado.Preparado, guardado: Ana);

        var (_, error) = await Handler().HandleAsync(InstanceId, TenantId, UserId, TestContext.Current.CancellationToken);

        error.Should().BeNull();
        var evento = _eventos.Concat(instance.Events).Should()
            .ContainSingle(e => e.Tipo == RegenerarMandatoYConsolidadoHandler.EventoMandatoRegenerado).Which;
        evento.CreatedBy.Should().Be(UserId);
        evento.Payload.Should().Contain(Ana.ToString()).And.Contain(Carlos.ToString());
        evento.Payload.Should().NotContain("Ana Restrepo").And.NotContain("111000111")
            .And.NotContain("Carlos Pérez").And.NotContain("222000222");
    }

    [Fact]
    public async Task AC1_ElEventoViajaEnElMismoGuardadoQueElNuevoMandato()
    {
        Seed(TramiteEstado.Entregado, guardado: Ana);

        var (_, error) = await Handler().HandleAsync(InstanceId, TenantId, UserId, TestContext.Current.CancellationToken);

        error.Should().BeNull();
        _guardados.Should().Contain(g => g.MandatoNuevo, "el mandato nuevo se confirma en un SaveChanges");
        _guardados.First(g => g.MandatoNuevo).Evento.Should().BeTrue(
            "la bitácora del cambio de firmante se confirma en el MISMO guardado que el documento (atómico)");
        _eventos.Should().NotContain(e => e.Tipo == RegenerarMandatoYConsolidadoHandler.EventoMandatoRegenerado,
            "no se registra en un SaveChanges posterior");
    }

    [Theory]
    [InlineData(TramiteEstado.Borrador)]
    [InlineData(TramiteEstado.Preparado)]
    [InlineData(TramiteEstado.Preasignacion)]
    [InlineData(TramiteEstado.Asignado)]
    [InlineData(TramiteEstado.Entregado)]
    [InlineData(TramiteEstado.Rechazado)]
    [InlineData(TramiteEstado.Subsanacion)]
    public async Task AC1_EnLosSieteEstadosNoFinales_RegeneraElMandatoYLosConsolidados(string estado)
    {
        var instance = Seed(estado, guardado: Ana);

        var (result, error) = await Handler().HandleAsync(InstanceId, TenantId, UserId, TestContext.Current.CancellationToken);

        error.Should().BeNull($"en {estado} la acción está habilitada (R4)");
        instance.MandateSignerId.Should().Be(Carlos);
        result!.Consolidado.AttachmentId.Should().NotBeEmpty();
        result.Maestro.Estado.Should().Be(RegenerarMandatoYConsolidadoHandler.MaestroRegenerado);
    }

    [Fact]
    public async Task AC1_SinMandatarioResuelto_ElMandatoSaleSinFirmar_YLaRespuestaLoAdvierte()
    {
        ConfigurarDefaultDelOt(null);
        _directorio = new Directorio();
        var instance = Seed(TramiteEstado.Entregado, guardado: Ana);

        var (result, error) = await Handler().HandleAsync(InstanceId, TenantId, UserId, TestContext.Current.CancellationToken);

        error.Should().BeNull();
        result!.Mandato.SinFirmar.Should().BeTrue();
        result.Mandato.FirmanteNuevoId.Should().BeNull();
        instance.MandateSignerId.Should().BeNull("se recalculó y hoy la configuración no resuelve a nadie");
        result.Avisos.Should().Contain(a => a.Codigo == RegenerarMandatoYConsolidadoHandler.AvisoMandatoSinFirmar);
    }

    // ── AC2 — el resto del expediente no se regenera ──────────────────────────────────────────────

    [Fact]
    public async Task AC2_NoRegeneraElFurNiLosDemasDocumentos_ConservaLosMismosAdjuntos()
    {
        var instance = Seed(TramiteEstado.Entregado, guardado: Ana);
        var otros = OtrosDocumentos
            .Select(t => AddAttachment(instance, t, "system"))
            .Append(instance.Attachments.Single(a => a.Tipo == "fur"))
            .Select(a => a.Id)
            .ToList();

        var (_, error) = await Handler().HandleAsync(InstanceId, TenantId, UserId, TestContext.Current.CancellationToken);

        error.Should().BeNull();
        instance.Attachments.Select(a => a.Id).Should().Contain(otros, "R2: solo se regenera el mandato");
        instance.Events.Should().NotContain(e => e.Tipo == "fur_generado");
        await _hotDocs.DidNotReceive().RegenerateHotDocumentsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC2_MandatoPersonalizadoDeLaCompania_SeConserva_ConAviso()
    {
        var instance = Seed(TramiteEstado.Entregado, guardado: Ana);
        instance.Attachments.Remove(instance.Attachments.Single(a => a.Tipo == "mandato"));
        var personalizado = AddAttachment(instance, "mandato", "company");

        var (result, error) = await Handler().HandleAsync(InstanceId, TenantId, UserId, TestContext.Current.CancellationToken);

        error.Should().BeNull();
        _mandatoGenerator.Captured.Should().BeNull("el mandato personalizado de la compañía no se reemplaza (HU #11313)");
        instance.Attachments.Should().ContainSingle(a => a.Tipo == "mandato").Which.Id.Should().Be(personalizado.Id);
        result!.Mandato.AttachmentId.Should().Be(personalizado.Id);
        result.Avisos.Should().Contain(a => a.Codigo == RegenerarMandatoYConsolidadoHandler.AvisoMandatoPersonalizadoConservado);
        instance.MandateSignerId.Should().Be(Ana, "el personalizado no nombra mandatario: la referencia no se toca");
        result.Mandato.FirmanteNuevoId.Should().Be(Ana);
        _eventos.Concat(instance.Events).Should().NotContain(e => e.Tipo == RegenerarMandatoYConsolidadoHandler.EventoMandatoRegenerado);
    }

    [Fact]
    public async Task AC2_PersonalizadoVigenteDeLaCompania_SeAplica_SinTocarElFirmante_ConAvisoDeAplicado()
    {
        var instance = Seed(TramiteEstado.Entregado, guardado: Ana);
        var personalizadoId = Guid.NewGuid();
        _personalizados.ResolveAsync(TenantId, Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(new PersonalizedDocumentResolution(
                [new ResolvedPersonalizedDocument("mandato", "mandato_compania.pdf", Encoding.UTF8.GetBytes("%PDF C"), personalizadoId, 3, "sha-c", 1)],
                []));

        var (result, error) = await Handler().HandleAsync(InstanceId, TenantId, UserId, TestContext.Current.CancellationToken);

        error.Should().BeNull();
        var mandato = instance.Attachments.Should().ContainSingle(a => a.Tipo == "mandato").Which;
        mandato.Source.Should().Be("company");
        mandato.SourcePersonalizedDocumentId.Should().Be(personalizadoId);
        result!.Mandato.AttachmentId.Should().Be(mandato.Id);
        instance.MandateSignerId.Should().Be(Ana);
        _mandatoGenerator.Captured.Should().BeNull("con personalizado no se emite (ni se capturan rúbricas para) el del sistema");
        result.Avisos.Should().Contain(a => a.Codigo == RegenerarMandatoYConsolidadoHandler.AvisoMandatoPersonalizadoAplicado
            && a.Mensaje.Contains("Se aplicó el mandato personalizado de la compañía", StringComparison.Ordinal));
        result.Avisos.Should().NotContain(a => a.Codigo == RegenerarMandatoYConsolidadoHandler.AvisoMandatoPersonalizadoConservado);
        _eventos.Concat(instance.Events).Should().NotContain(e => e.Tipo == RegenerarMandatoYConsolidadoHandler.EventoMandatoRegenerado);
    }

    // ── AC3 — consolidados: wizard sin cascada, maestro respetando la radicación ─────────────────────

    [Fact]
    public async Task AC3_ElWizardSeRearmaSinCascadaDelFur_AunqueElFurEsteDesactualizado()
    {
        var instance = Seed(TramiteEstado.Preparado, guardado: Ana);
        instance.ExpedienteActualizadoEn = DateTimeOffset.UtcNow.AddMinutes(-1); // FUR de hace una hora ⇒ desactualizado

        var (result, error) = await Handler().HandleAsync(InstanceId, TenantId, UserId, TestContext.Current.CancellationToken);

        error.Should().BeNull();
        await _hotDocs.DidNotReceive().RegenerateHotDocumentsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        result!.Consolidado.Estado.Should().Be(RegenerarMandatoYConsolidadoHandler.ConsolidadoRegenerado);
        instance.Attachments.Should().ContainSingle(a => a.Tipo == "consolidado")
            .Which.Id.Should().Be(result.Consolidado.AttachmentId!.Value);
        instance.ConsolidadoWizardVigente.Should().BeTrue();
    }

    [Fact]
    public async Task AC3_ElConsolidadoCargadoPorElUsuario_SeReemplaza()
    {
        var instance = Seed(TramiteEstado.Entregado, guardado: Ana);
        var delUsuario = AddAttachment(instance, "consolidado", "user");

        var (result, error) = await Handler().HandleAsync(InstanceId, TenantId, UserId, TestContext.Current.CancellationToken);

        error.Should().BeNull();
        instance.Attachments.Should().NotContain(a => a.Id == delUsuario.Id, "la acción salta la protección de Source='user'");
        instance.Attachments.Should().ContainSingle(a => a.Tipo == "consolidado").Which.Source.Should().Be("system");
        result!.Consolidado.AttachmentId.Should().NotBe(delUsuario.Id);
    }

    [Fact]
    public async Task AC3_ElMaestroSeRearma_ConLaMatrizDePrecedenciaDelOt()
    {
        var instance = Seed(TramiteEstado.Entregado, guardado: Ana);

        var (result, error) = await Handler().HandleAsync(InstanceId, TenantId, UserId, TestContext.Current.CancellationToken);

        error.Should().BeNull();
        await _precedencia.Received(1).ResolverAsync(TypeId, Ot, Arg.Any<CancellationToken>());
        result!.Maestro.Estado.Should().Be(RegenerarMandatoYConsolidadoHandler.MaestroRegenerado);
        instance.Attachments.Should().ContainSingle(a => a.Tipo == "consolidado_maestro")
            .Which.Id.Should().Be(result.Maestro.AttachmentId!.Value);
        result.Maestro.GeneradoEn.Should().NotBeNull();
        instance.ConsolidadoMaestroVigente.Should().BeTrue();
        await _hotDocs.DidNotReceive().RegenerateHotDocumentsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC3_ElMaestroRadicadoEnQuipux_SeConserva_ConAviso()
    {
        var instance = Seed(TramiteEstado.Entregado, guardado: Ana);
        var radicado = AddAttachment(instance, "consolidado_maestro", "system");
        _radicado.AttachmentRadicadoAsync(TenantId, InstanceId, Arg.Any<CancellationToken>()).Returns(radicado.Id);

        var (result, error) = await Handler().HandleAsync(InstanceId, TenantId, UserId, TestContext.Current.CancellationToken);

        error.Should().BeNull();
        result!.Maestro.Estado.Should().Be(RegenerarMandatoYConsolidadoHandler.MaestroRadicadoConservado);
        result.Maestro.AttachmentId.Should().Be(radicado.Id);
        instance.Attachments.Should().Contain(a => a.Id == radicado.Id);
        result.Avisos.Should().Contain(a => a.Codigo == RegenerarMandatoYConsolidadoHandler.AvisoMaestroRadicadoConservado);
    }

    [Fact]
    public async Task AC3_SiFallaElMaestro_EsExitoParcial_YElMaestroQuedaInvalidado()
    {
        var instance = Seed(TramiteEstado.Entregado, guardado: Ana);
        instance.ConsolidadoMaestroVigente = true;

        var (result, error) = await Handler(maestroMerger: new ThrowingMerger())
            .HandleAsync(InstanceId, TenantId, UserId, TestContext.Current.CancellationToken);

        error.Should().BeNull("el mandato y el wizard ya quedaron regenerados: éxito parcial (R3)");
        result!.Maestro.Estado.Should().Be(RegenerarMandatoYConsolidadoHandler.MaestroFallo);
        result.Maestro.AttachmentId.Should().BeNull();
        result.Avisos.Should().Contain(a => a.Codigo == RegenerarMandatoYConsolidadoHandler.AvisoMaestroFallo);
        instance.ConsolidadoMaestroVigente.Should().BeFalse("se rearma de forma perezosa en la próxima vista");
        result.Mandato.FirmanteNuevoId.Should().Be(Carlos);
        result.Consolidado.AttachmentId.Should().NotBeEmpty();
    }

    [Fact]
    public async Task AC3_SiFallaElWizard_EsExitoParcial_ConAviso_YAunAsiSeRearmaElMaestro()
    {
        var instance = Seed(TramiteEstado.Entregado, guardado: Ana);
        instance.ConsolidadoWizardVigente = true;

        var (result, error) = await Handler(wizardMerger: new ThrowingMerger())
            .HandleAsync(InstanceId, TenantId, UserId, TestContext.Current.CancellationToken);

        error.Should().BeNull("el mandato ya cambió: responder error escondería un cambio confirmado");
        result!.Consolidado.Estado.Should().Be(RegenerarMandatoYConsolidadoHandler.ConsolidadoFallo);
        result.Consolidado.AttachmentId.Should().BeNull();
        result.Consolidado.GeneradoEn.Should().BeNull();
        result.Avisos.Should().Contain(a => a.Codigo == RegenerarMandatoYConsolidadoHandler.AvisoConsolidadoFallo
            && a.Mensaje == "El mandato se regeneró, pero no se pudo armar el consolidado; se rearmará al abrirlo.");
        instance.ConsolidadoWizardVigente.Should().BeFalse();
        result.Mandato.FirmanteNuevoId.Should().Be(Carlos);
        result.Maestro.Estado.Should().Be(RegenerarMandatoYConsolidadoHandler.MaestroRegenerado, "el maestro se intenta igual");
    }

    // ── AC4 — estados finales y migrados ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(TramiteEstado.Aprobado)]
    [InlineData(TramiteEstado.Anulado)]
    [InlineData(TramiteEstado.Revocado)]
    public async Task AC4_EnEstadoFinal_Responde409_SinCambios(string estado)
    {
        var instance = Seed(estado, guardado: Ana);
        var adjuntosAntes = instance.Attachments.Select(a => a.Id).ToList();

        var (result, error) = await Handler().HandleAsync(InstanceId, TenantId, UserId, TestContext.Current.CancellationToken);

        result.Should().BeNull();
        error.Should().Be(RegenerarMandatoYConsolidadoHandler.EstadoNoPermite);
        _mandatoGenerator.Captured.Should().BeNull();
        _storage.Saved.Should().BeEmpty();
        instance.MandateSignerId.Should().Be(Ana);
        instance.Attachments.Select(a => a.Id).Should().Equal(adjuntosAntes);
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC4_MigradoDeV1EnEstadoFinal_Responde409MigradoSoloLectura()
    {
        var instance = Seed(TramiteEstado.Aprobado, guardado: Ana);
        instance.IsMigrated = true;

        var (_, error) = await Handler().HandleAsync(InstanceId, TenantId, UserId, TestContext.Current.CancellationToken);

        error.Should().Be("migrado_solo_lectura");
        _storage.Saved.Should().BeEmpty();
    }

    [Fact]
    public async Task Contrato_TramiteInexistente_NotFound()
    {
        var (_, error) = await Handler().HandleAsync(InstanceId, TenantId, UserId, TestContext.Current.CancellationToken);

        error.Should().Be("not_found");
    }

    [Fact]
    public async Task Contrato_SinFur_NoTocaNada_YRespondeFurRequerido()
    {
        var instance = Seed(TramiteEstado.Preparado, guardado: Ana);
        instance.Attachments.Remove(instance.Attachments.Single(a => a.Tipo == "fur"));

        var (_, error) = await Handler().HandleAsync(InstanceId, TenantId, UserId, TestContext.Current.CancellationToken);

        error.Should().Be(SubmitGate.FurRequerido);
        _mandatoGenerator.Captured.Should().BeNull();
        instance.MandateSignerId.Should().Be(Ana);
    }

    // ── AC7 — sin regresión del camino del FUR ──────────────────────────────────────────────────────

    [Fact]
    public async Task AC7_GenerarFur_FueraDeBorrador_ConservaElFirmanteGuardado()
    {
        var instance = Seed(TramiteEstado.Entregado, guardado: Ana);

        var (_, error) = await FurHandler().HandleAsync(InstanceId, TenantId, TestContext.Current.CancellationToken);

        error.Should().BeNull();
        _mandatoGenerator.Captured!.Mandatario!.Documento.Should().Be("111000111", "HU #13142 AC4 sigue vigente para el FUR");
        instance.MandateSignerId.Should().Be(Ana);
    }

    [Fact]
    public async Task AC7_GenerarFur_EnBorrador_SigueRecalculandoConLaPrelacion()
    {
        var instance = Seed(TramiteEstado.Borrador, guardado: Ana);

        var (_, error) = await FurHandler().HandleAsync(InstanceId, TenantId, TestContext.Current.CancellationToken);

        error.Should().BeNull();
        instance.MandateSignerId.Should().Be(Carlos);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────────

    private GenerarFurHandler FurHandler() => new(
        _repo,
        new MockFurDocumentGenerator(),
        Substitute.For<IKyverumCertificateClient>(),
        Substitute.For<IRuesCertificateGenerator>(),
        Substitute.For<IRnmcCertificateGenerator>(),
        Substitute.For<IProcedureInstancePrendaRepository>(),
        _storage,
        NullLogger<GenerarFurHandler>.Instance,
        mandatoGenerator: _mandatoGenerator,
        mandatePolicy: _policy,
        mandateDirectory: _directorio,
        personalizedDocumentResolver: _personalizados);

    private RegenerarMandatoYConsolidadoHandler Handler(
        IExpedienteConsolidadoMerger? maestroMerger = null, IExpedienteConsolidadoMerger? wizardMerger = null)
    {
        var wizard = new GenerarConsolidadoHandler(_repo, wizardMerger ?? _merger, _storage, hotDocsRegenerator: _hotDocs);
        var maestro = new GenerarConsolidadoMaestroHandler(
            _repo, maestroMerger ?? _merger, _storage, maestroRadicado: _radicado, hotDocsRegenerator: _hotDocs);
        return new RegenerarMandatoYConsolidadoHandler(_repo, FurHandler(), wizard, maestro, _precedencia);
    }

    private void ConfigurarDefaultDelOt(Guid? otDefault)
    {
        var config = new MandateOtConfig(
            Ot, "generico", RequiresForNaturalPerson: false, InstitutionalMandataryName: null,
            InstitutionalMandataryNit: null, AssignmentMode: "signer", OtDefaultMandateSignerId: otDefault);
        _policy.ResolveAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(config);
        _policy.ResolveByOfficeIdAsync(Ot, Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(config);
    }

    private ProcedureInstance Seed(string status, Guid? guardado)
    {
        var instance = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.For(TramiteTipologiaCatalog.CodigoMatriculaInicial ?? "matricula_inicial"),
            Id = InstanceId,
            TenantId = TenantId,
            ProcedureTypeId = TypeId,
            ReferenceNumber = "TRM-2026-014547",
            Status = status,
            TransitOfficeId = Ot,
            MandateSignerId = guardado,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-2),
            FieldValues =
            {
                new ProcedureInstanceFieldValue
                {
                    Id = Guid.NewGuid(),
                    TenantId = TenantId,
                    ProcedureInstanceId = InstanceId,
                    FieldKey = "transit_office_code",
                    ValueText = "11001000",
                    Source = "user",
                },
            },
        };
        AddAttachment(instance, "fur", "system", DateTimeOffset.UtcNow.AddHours(-1));
        AddAttachment(instance, "mandato", "system", DateTimeOffset.UtcNow.AddHours(-1));

        _repo.GetByIdAsync(InstanceId, TenantId, Arg.Any<CancellationToken>()).Returns(instance);
        _repo.GetByIdWithAttachmentsAsync(InstanceId, TenantId, Arg.Any<CancellationToken>()).Returns(instance);
        _repo.GetByIdWithChecklistGraphAsync(InstanceId, TenantId, Arg.Any<CancellationToken>()).Returns(instance);
        _repo.GetByIdWithFurGraphAsync(InstanceId, TenantId, Arg.Any<CancellationToken>()).Returns(instance);
        _instancia = instance;
        return instance;
    }

    private ProcedureInstanceAttachment AddAttachment(
        ProcedureInstance instance, string tipo, string source, DateTimeOffset? uploadedAt = null)
    {
        var path = $"{instance.Id:D}/{tipo}_{source}_{Guid.NewGuid():N}";
        var attachment = new ProcedureInstanceAttachment
        {
            Id = Guid.NewGuid(),
            TenantId = instance.TenantId,
            ProcedureInstanceId = instance.Id,
            Tipo = tipo,
            Filename = $"{tipo}.pdf",
            Mimetype = "application/pdf",
            SizeBytes = 10,
            Sha256 = $"sha-{tipo}-{source}",
            StoragePath = path,
            Source = source,
            UploadedAt = uploadedAt ?? DateTimeOffset.UtcNow.AddMinutes(-30),
        };
        instance.Attachments.Add(attachment);
        _storage.Files[path] = Encoding.UTF8.GetBytes($"%PDF {tipo}");
        return attachment;
    }

    private sealed class Directorio : IMandateSignerDirectory
    {
        public IReadOnlyList<MandateSignerCandidate> SoloPorId { get; init; } = [];

        public Task<IReadOnlyList<MandateSignerCandidate>> GetCandidatesAsync(
            Guid transitOfficeId, Guid companyTenantId, string? nitMandante = null, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<MandateSignerCandidate>>([]);

        public Task<MandateSignerCandidate?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(SoloPorId.FirstOrDefault(c => c.Id == id));
    }

    private sealed class CapturingMandatoGenerator : IMandatoGenerator
    {
        public MandatoData? Captured { get; private set; }

        public GeneratedDocument GenerateMandato(MandatoData data)
        {
            Captured = data;
            return new GeneratedDocument("mandato", "mandato.pdf", "application/pdf", Encoding.UTF8.GetBytes("%PDF MANDATO"));
        }
    }

    private class FakeMerger : IExpedienteConsolidadoMerger
    {
        public byte[] NormalizeToPdf(byte[] content, string mimetype) => content;

        public byte[] Merge(IReadOnlyList<byte[]> pdfParts) => pdfParts.SelectMany(x => x).ToArray();

        public virtual byte[] Compose(MergeRequest request) => Merge(request.Parts.Select(p => p.Pdf).ToList());
    }

    private sealed class ThrowingMerger : FakeMerger
    {
        public override byte[] Compose(MergeRequest request) => throw new InvalidOperationException("compositor caído");
    }

    private sealed class FakeStorage : IAttachmentStorage
    {
        public List<string> Saved { get; } = [];
        public Dictionary<string, byte[]> Files { get; } = new();

        public async Task<StoredFile> SaveAsync(
            Guid procedureInstanceId, string tipo, string originalFilename, Stream content, CancellationToken ct = default)
        {
            using var ms = new MemoryStream();
            await content.CopyToAsync(ms, ct);
            var path = $"{procedureInstanceId:D}/{tipo}_nuevo_{Saved.Count}";
            Files[path] = ms.ToArray();
            Saved.Add(path);
            return new StoredFile(path, $"sha-{tipo}-{Saved.Count}", ms.Length);
        }

        public Task<PresignedUpload> CreatePresignedUploadAsync(
            Guid procedureInstanceId, string tipo, string originalFilename, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public void Delete(string storagePath) => Files.Remove(storagePath);

        public Task<Stream?> OpenReadAsync(string storagePath, CancellationToken ct = default) =>
            Task.FromResult<Stream?>(Files.TryGetValue(storagePath, out var b) ? new MemoryStream(b) : null);

        public Task<(string Url, DateTimeOffset ExpiresAt)?> GetPresignedViewUrlAsync(
            string storagePath, CancellationToken ct = default) =>
            Task.FromResult<(string Url, DateTimeOffset ExpiresAt)?>(null);
    }
}
