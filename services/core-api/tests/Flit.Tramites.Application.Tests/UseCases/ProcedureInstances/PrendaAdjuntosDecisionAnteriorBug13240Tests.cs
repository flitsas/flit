using Flit.Tramites.Application.Storage;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// Bug #13240 — al cambiar la decisión de prenda, el PDF que exigía la decisión ANTERIOR
/// (<c>prenda_registro</c>/<c>prenda_levantamiento</c>/<c>prenda_solicitud</c>) seguía en los documentos
/// del trámite y dentro del consolidado, mientras el FUR (que lee las vigentes) ya no lo declaraba.
/// <para>Uso de ejemplo:
/// <c>new RegistrarPrendaHandler(instances, prendas, policy, storage, imprintAudit, maestroRadicado)
/// .HandleAsync(id, tenant, new RegistrarPrendaInput("omitir"))</c> ⇒ retira los adjuntos
/// <c>prenda_*</c> que ninguna decisión vigente exige (mismas reglas que <see cref="DeleteAttachmentHandler"/>).</para>
/// </summary>
public sealed class PrendaAdjuntosDecisionAnteriorBug13240Tests
{
    private readonly IProcedureInstanceRepository _instances = Substitute.For<IProcedureInstanceRepository>();
    private readonly IAttachmentStorage _storage = Substitute.For<IAttachmentStorage>();
    private readonly IMaestroRadicadoLookup _maestro = Substitute.For<IMaestroRadicadoLookup>();
    private readonly FakePrendaRepo _prendas = new();
    private readonly Guid _id = Guid.NewGuid();
    private readonly Guid _tenant = Guid.NewGuid();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public PrendaAdjuntosDecisionAnteriorBug13240Tests()
    {
        _maestro.AttachmentsProtegidosAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlySet<Guid>>(new HashSet<Guid>()));
    }

    private sealed class FakePrendaRepo : IProcedureInstancePrendaRepository
    {
        public List<ProcedureInstancePrenda> Rows { get; } = [];

        public Task<ProcedureInstancePrenda?> GetVigenteAsync(Guid instanceId, Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(Rows.FirstOrDefault(r => r.Estado == PrendaEstado.Vigente));

        public Task<IReadOnlyList<ProcedureInstancePrenda>> GetVigentesAsync(Guid instanceId, Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ProcedureInstancePrenda>>(
                Rows.Where(r => r.Estado == PrendaEstado.Vigente).OrderBy(r => r.CreatedAt).ToList());

        public Task<IReadOnlyList<ProcedureInstancePrenda>> ListByInstanceAsync(Guid instanceId, Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ProcedureInstancePrenda>>(Rows.ToList());

        public Task AddAsync(ProcedureInstancePrenda prenda, CancellationToken ct = default)
        {
            if (prenda.Id == Guid.Empty)
                prenda.Id = Guid.NewGuid();
            Rows.Add(prenda);
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private ProcedureInstance Instancia(ProcedureType tipo, string status = TramiteEstado.Borrador, bool subsanacion = false)
    {
        var instance = new ProcedureInstance
        {
            ProcedureType = tipo,
            Id = _id,
            TenantId = _tenant,
            ProcedureTypeId = tipo.Id,
            ReferenceNumber = "TRM-2026-013240",
            Status = status,
            SubsanacionActiva = subsanacion,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        // El handler lee la instancia por las dos vías; en EF ambas devuelven la MISMA entidad rastreada.
        _instances.GetByIdAsync(_id, _tenant, Arg.Any<CancellationToken>()).Returns(instance);
        _instances.GetByIdWithAttachmentsAsync(_id, _tenant, Arg.Any<CancellationToken>()).Returns(instance);
        return instance;
    }

    private ProcedureInstanceAttachment Adjunto(ProcedureInstance instance, string tipo)
    {
        var a = new ProcedureInstanceAttachment
        {
            Id = Guid.NewGuid(),
            TenantId = _tenant,
            ProcedureInstanceId = _id,
            Tipo = tipo,
            Filename = $"{tipo}.pdf",
            Mimetype = "application/pdf",
            SizeBytes = 1024,
            Sha256 = "deadbeef",
            StoragePath = $"{_id:D}/{tipo}_ficticio.pdf",
            Source = "user",
            UploadedAt = DateTimeOffset.UtcNow,
        };
        instance.Attachments.Add(a);
        return a;
    }

    private void Vigente(string decision, int orden = 0) =>
        _prendas.Rows.Add(new ProcedureInstancePrenda
        {
            Id = Guid.NewGuid(),
            TenantId = _tenant,
            ProcedureInstanceId = _id,
            Decision = decision,
            Estado = PrendaEstado.Vigente,
            AccionFamilia = PrendaDecision.AccionFamiliaFor(decision),
            AcreedorNombre = "Banco Ficticio S.A.",
            AcreedorDocumento = "900000001",
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10 + orden),
        });

    private RegistrarPrendaHandler Handler(IVehicleSignatureImprintRepository? imprint = null) =>
        new(_instances, _prendas, PolicyNoExige(), _storage, imprint, _maestro);

    private static IPrendaDocumentRequirementPolicy PolicyNoExige()
    {
        var policy = Substitute.For<IPrendaDocumentRequirementPolicy>();
        policy.IsRequiredAsync(Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(false);
        return policy;
    }

    private static List<string> Tipos(ProcedureInstance i) => i.Attachments.Select(a => a.Tipo).OrderBy(t => t).ToList();

    // ── Happy path: el PDF de la decisión anterior se retira ─────────────────────────────────────

    [Theory]
    [InlineData(PrendaDecision.Registrar, PrendaDocTipos.Registro, PrendaDecision.Omitir)]
    [InlineData(PrendaDecision.Registrar, PrendaDocTipos.Registro, PrendaDecision.SinPrenda)]
    [InlineData(PrendaDecision.Levantar, PrendaDocTipos.Levantamiento, PrendaDecision.Omitir)]
    [InlineData(PrendaDecision.Solicitar, PrendaDocTipos.Solicitud, PrendaDecision.SinPrenda)]
    public async Task CambiarAOmitirOSinPrenda_RetiraElAdjuntoDeLaDecisionAnterior(
        string anterior, string docAnterior, string nueva)
    {
        var instance = Instancia(ProcedureTypeFixture.Traspaso);
        var prenda = Adjunto(instance, docAnterior);
        var soat = Adjunto(instance, "soat");
        Vigente(anterior);

        var (result, error) = await Handler().HandleAsync(_id, _tenant, new RegistrarPrendaInput(nueva), ct: Ct);

        error.Should().BeNull();
        result!.Decision.Should().Be(nueva);
        instance.Attachments.Should().ContainSingle().Which.Should().BeSameAs(soat);
        _storage.Received(1).Delete(prenda.StoragePath);
        _storage.DidNotReceive().Delete(soat.StoragePath);
        _instances.Received(1).RemoveAttachment(prenda);
        await _instances.Received().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CambiarDeRegistrarALevantar_RetiraRegistroYConservaLevantamiento()
    {
        var instance = Instancia(ProcedureTypeFixture.Traspaso);
        var registro = Adjunto(instance, PrendaDocTipos.Registro);
        Adjunto(instance, PrendaDocTipos.Levantamiento);
        Vigente(PrendaDecision.Registrar);

        var (_, error) = await Handler().HandleAsync(_id, _tenant, new RegistrarPrendaInput(
            PrendaDecision.Levantar, "Banco Ficticio S.A.", "900000001"), ct: Ct);

        error.Should().BeNull();
        Tipos(instance).Should().Equal(PrendaDocTipos.Levantamiento);
        _instances.Received(1).RemoveAttachment(registro);
    }

    // ── Edge cases ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task MismaDecision_ConservaSuAdjunto()
    {
        var instance = Instancia(ProcedureTypeFixture.Traspaso);
        Adjunto(instance, PrendaDocTipos.Registro);
        Vigente(PrendaDecision.Registrar);

        var (_, error) = await Handler().HandleAsync(_id, _tenant, new RegistrarPrendaInput(
            PrendaDecision.Registrar, "Otro Banco Ficticio", "900000002"), ct: Ct);

        error.Should().BeNull();
        Tipos(instance).Should().Equal(PrendaDocTipos.Registro);
        _storage.DidNotReceiveWithAnyArgs().Delete(default!);
        _instances.DidNotReceiveWithAnyArgs().RemoveAttachment(default!);
    }

    [Fact]
    public async Task Complementaria_CambiarConstitucion_NoTocaElAdjuntoDelLevantamientoVigente()
    {
        var instance = Instancia(ProcedureTypeFixture.PrendaInscripcion);
        var registro = Adjunto(instance, PrendaDocTipos.Registro);
        Adjunto(instance, PrendaDocTipos.Levantamiento);
        Adjunto(instance, PrendaDocTipos.Solicitud);
        Vigente(PrendaDecision.Registrar, orden: 0);
        Vigente(PrendaDecision.Levantar, orden: 1);

        var (_, error) = await Handler().HandleAsync(_id, _tenant, new RegistrarPrendaInput(
            PrendaDecision.Solicitar, "Banco Ficticio S.A.", "900000001"), ct: Ct);

        error.Should().BeNull();
        Tipos(instance).Should().Equal(PrendaDocTipos.Levantamiento, PrendaDocTipos.Solicitud);
        _instances.Received(1).RemoveAttachment(registro);
    }

    /// <summary>
    /// Review PR #508 (B1) — primera activación de la complementaria: el front guarda primero la base
    /// (<c>registrar</c>) y luego <c>levantar</c>. El PUT de la base no puede retirar el
    /// <c>prenda_levantamiento</c> recién subido: no pertenece a ninguna decisión reemplazada.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Complementaria_PrimeraActivacion_PutDeLaBaseNoRetiraElDocDeLaComplementaria(bool baseYaVigente)
    {
        var instance = Instancia(ProcedureTypeFixture.PrendaInscripcion);
        Adjunto(instance, PrendaDocTipos.Registro);
        Adjunto(instance, PrendaDocTipos.Levantamiento);
        if (baseYaVigente)
            Vigente(PrendaDecision.Registrar);

        var (_, error) = await Handler().HandleAsync(_id, _tenant, new RegistrarPrendaInput(
            PrendaDecision.Registrar, "Banco Ficticio S.A.", "900000001"), ct: Ct);

        error.Should().BeNull();
        Tipos(instance).Should().Equal(PrendaDocTipos.Levantamiento, PrendaDocTipos.Registro);
        _storage.DidNotReceiveWithAnyArgs().Delete(default!);
        _instances.DidNotReceiveWithAnyArgs().RemoveAttachment(default!);
    }

    /// <summary>
    /// Review PR #508 (B1) — en un tipo complementario solo se retira el soporte de la decisión
    /// REEMPLAZADA en este guardado; el de la otra familia (aún sin decisión vigente) se conserva.
    /// </summary>
    [Fact]
    public async Task Complementaria_ConstitucionReemplazada_RetiraSuDocYConservaElDeLaOtraFamilia()
    {
        var instance = Instancia(ProcedureTypeFixture.PrendaInscripcion);
        var registro = Adjunto(instance, PrendaDocTipos.Registro);
        Adjunto(instance, PrendaDocTipos.Levantamiento);
        Vigente(PrendaDecision.Registrar);

        var (_, error) = await Handler().HandleAsync(_id, _tenant, new RegistrarPrendaInput(
            PrendaDecision.Solicitar, "Banco Ficticio S.A.", "900000001"), ct: Ct);

        error.Should().BeNull();
        Tipos(instance).Should().Equal(PrendaDocTipos.Levantamiento);
        _instances.Received(1).RemoveAttachment(registro);
    }

    [Fact]
    public async Task EstadoNoEditable_NoRetiraAdjuntos_PeroGuardaLaDecision()
    {
        var instance = Instancia(ProcedureTypeFixture.Traspaso, status: TramiteEstado.Preparado);
        Adjunto(instance, PrendaDocTipos.Registro);
        Vigente(PrendaDecision.Registrar);

        var (result, error) = await Handler().HandleAsync(_id, _tenant, new RegistrarPrendaInput(PrendaDecision.SinPrenda), ct: Ct);

        error.Should().BeNull();
        result!.Decision.Should().Be(PrendaDecision.SinPrenda);
        Tipos(instance).Should().Equal(PrendaDocTipos.Registro);
        _storage.DidNotReceiveWithAnyArgs().Delete(default!);
        _instances.DidNotReceiveWithAnyArgs().RemoveAttachment(default!);
    }

    [Fact]
    public async Task Subsanacion_SiRetira()
    {
        var instance = Instancia(ProcedureTypeFixture.Traspaso, status: TramiteEstado.Rechazado, subsanacion: true);
        Adjunto(instance, PrendaDocTipos.Registro);
        Vigente(PrendaDecision.Registrar);

        await Handler().HandleAsync(_id, _tenant, new RegistrarPrendaInput(PrendaDecision.SinPrenda), ct: Ct);

        instance.Attachments.Should().BeEmpty();
    }

    [Fact]
    public async Task AdjuntoReferenciadoPorRadicacion_NoSeRetira()
    {
        var instance = Instancia(ProcedureTypeFixture.Traspaso);
        var protegido = Adjunto(instance, PrendaDocTipos.Registro);
        Vigente(PrendaDecision.Registrar);
        _maestro.AttachmentsProtegidosAsync(_tenant, _id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlySet<Guid>>(new HashSet<Guid> { protegido.Id }));

        await Handler().HandleAsync(_id, _tenant, new RegistrarPrendaInput(PrendaDecision.SinPrenda), ct: Ct);

        Tipos(instance).Should().Equal(PrendaDocTipos.Registro);
        _instances.DidNotReceiveWithAnyArgs().RemoveAttachment(default!);
    }

    [Fact]
    public async Task AdjuntoConImprontaFirmada_SeRetiraLaFilaPeroSeConservaElBlob()
    {
        var instance = Instancia(ProcedureTypeFixture.Traspaso);
        var prenda = Adjunto(instance, PrendaDocTipos.Registro);
        Vigente(PrendaDecision.Registrar);
        var imprint = Substitute.For<IVehicleSignatureImprintRepository>();
        imprint.SoftDeleteByAttachmentIds(Arg.Any<IEnumerable<Guid>>(), Arg.Any<DateTimeOffset>())
            .Returns(new HashSet<string>(StringComparer.Ordinal) { prenda.StoragePath });

        await Handler(imprint).HandleAsync(_id, _tenant, new RegistrarPrendaInput(PrendaDecision.SinPrenda), ct: Ct);

        instance.Attachments.Should().BeEmpty();
        _instances.Received(1).RemoveAttachment(prenda);
        _storage.DidNotReceiveWithAnyArgs().Delete(default!);
    }

    /// <summary>
    /// <c>inscripcion_prenda</c> es requisito del CATÁLOGO del tipo (LEVANTAR_INSCRIBIR_PRENDA,
    /// CAMBIO_ACREEDOR) y documento de la política del OT, no el soporte de una decisión del agregado:
    /// no lo gobierna la decisión, así que no se retira.
    /// </summary>
    [Fact]
    public async Task InscripcionPrendaDelCatalogo_NoSeRetira()
    {
        var instance = Instancia(ProcedureTypeFixture.Traspaso);
        Adjunto(instance, "inscripcion_prenda");
        Adjunto(instance, PrendaDocTipos.Registro);
        Vigente(PrendaDecision.Registrar);

        await Handler().HandleAsync(_id, _tenant, new RegistrarPrendaInput(PrendaDecision.SinPrenda), ct: Ct);

        Tipos(instance).Should().Equal("inscripcion_prenda");
    }

    // ── Contrato: composiciones sin almacenamiento siguen como antes ─────────────────────────────

    [Fact]
    public async Task SinAlmacenamientoCableado_NoTocaAdjuntos_NiCargaLaInstanciaConAdjuntos()
    {
        var instance = Instancia(ProcedureTypeFixture.Traspaso);
        Adjunto(instance, PrendaDocTipos.Registro);
        Vigente(PrendaDecision.Registrar);

        var (result, error) = await new RegistrarPrendaHandler(_instances, _prendas, PolicyNoExige())
            .HandleAsync(_id, _tenant, new RegistrarPrendaInput(PrendaDecision.SinPrenda), ct: Ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
        Tipos(instance).Should().Equal(PrendaDocTipos.Registro);
        await _instances.DidNotReceiveWithAnyArgs().GetByIdWithAttachmentsAsync(default, default, Ct);
    }

    [Fact]
    public async Task DecisionRechazada_NoRetiraNada()
    {
        var instance = Instancia(ProcedureTypeFixture.Traspaso);
        Adjunto(instance, PrendaDocTipos.Registro);
        Vigente(PrendaDecision.Registrar);

        var (result, error) = await Handler().HandleAsync(_id, _tenant, new RegistrarPrendaInput("decision_inexistente"), ct: Ct);

        result.Should().BeNull();
        error.Should().Be("prenda_decision_invalida");
        Tipos(instance).Should().Equal(PrendaDocTipos.Registro);
    }
}
