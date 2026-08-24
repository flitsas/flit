using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.Identity.Events;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.Identity;

/// <summary>
/// HU #11196 (AC2&ndash;AC6) — el lote de firma a posteriori. Cuando el representante aprueba su
/// validación de identidad, todos los trámites del tenant que quedaron marcados esperándola se firman de
/// una. Se ejercita el consumidor REAL contra el repositorio de marcas real (in-memory) para poder
/// afirmar sobre el estado en que queda cada marca, que es donde vive la traza del AC6.
/// </summary>
public sealed class FirmaPosteriorLoteTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private const string DocType = "CC";
    private const string Doc = "1090123456";

    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly FakeMarkRepository _marks = new();
    private readonly IRepresentanteLegalIdentityUpdater _directorio =
        Substitute.For<IRepresentanteLegalIdentityUpdater>();
    private readonly ITramiteFirmaAplicador _firma = Substitute.For<ITramiteFirmaAplicador>();

    public FirmaPosteriorLoteTests() =>
        _firma.AplicarAsync(
                Arg.Any<ProcedureInstance>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(("fur", (string?)null));

    [Fact]
    public async Task AC2_VariosTramitesMarcados_SeFirmanTodosConEsaValidacion()
    {
        var ct = TestContext.Current.CancellationToken;
        var validationId = SeedValidacionAprobada();
        var uno = SeedTramiteMarcado("TRM-1");
        var dos = SeedTramiteMarcado("TRM-2");
        var tres = SeedTramiteMarcado("TRM-3");

        var result = await Consumer().HandleAsync(Evento(validationId), ct);

        result.Firmados.Should().Be(3);
        _marks.Todas.Should().OnlyContain(m => m.Estado == DeferredSignatureEstados.Aplicada);
        _marks.Todas.Should().OnlyContain(m => m.AppliedValidationId == validationId);
        new[] { uno, dos, tres }.Should().OnlyContain(i => i != Guid.Empty);
    }

    [Fact]
    public async Task AC3_SoloSeFirmanLosQueSiguenEnBorradorOSubsanacion()
    {
        // El estado se revalida AL APLICAR: entre la marca y la aprobación pueden pasar días y el
        // trámite pudo radicarse. Marcar no puede ser una autorización a firmar un expediente ya salido.
        var ct = TestContext.Current.CancellationToken;
        var validationId = SeedValidacionAprobada();
        SeedTramiteMarcado("TRM-BORRADOR");
        SeedTramiteMarcado("TRM-SUBSANACION", status: TramiteEstado.Rechazado, subsanacion: true);
        SeedTramiteMarcado("TRM-ENTREGADO", status: TramiteEstado.Entregado);

        var result = await Consumer().HandleAsync(Evento(validationId), ct);

        result.Firmados.Should().Be(2);
        result.Descartados.Should().Be(1);
        _marks.Todas.Should().ContainSingle(m => m.Estado == DeferredSignatureEstados.Descartada)
            .Which.DiscardedReason.Should().Contain(TramiteEstado.Entregado);
    }

    [Fact]
    public async Task AC4_TrasFirmarElLote_SeActualizaLaFirmaDelRepresentanteEnLaCompania()
    {
        var ct = TestContext.Current.CancellationToken;
        var validationId = SeedValidacionAprobada();
        SeedTramiteMarcado("TRM-1");

        await Consumer().HandleAsync(Evento(validationId), ct);

        await _directorio.Received(1).ActualizarIdentidadVigenteAsync(
            Tenant, DocType, Doc, validationId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SinNadaQueFirmar_NoTocaElDirectorio()
    {
        // Anclar la identidad sin haber firmado nada dejaría al directorio diciendo que el representante
        // ya está resuelto por una corrida que no hizo nada.
        var ct = TestContext.Current.CancellationToken;
        var validationId = SeedValidacionAprobada();
        SeedTramiteMarcado("TRM-ENTREGADO", status: TramiteEstado.Entregado);

        await Consumer().HandleAsync(Evento(validationId), ct);

        await _directorio.DidNotReceive().ActualizarIdentidadVigenteAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC5_LosTramitesDeOtroRepresentante_SiguenSinFirmar()
    {
        var ct = TestContext.Current.CancellationToken;
        var validationId = SeedValidacionAprobada();
        SeedTramiteMarcado("TRM-MIO");
        var ajena = SeedMarca(SeedTramite("TRM-AJENO"), documento: "9999999");

        var result = await Consumer().HandleAsync(Evento(validationId), ct);

        result.Firmados.Should().Be(1);
        ajena.Estado.Should().Be(DeferredSignatureEstados.Pendiente);
        ajena.AppliedValidationId.Should().BeNull();
    }

    [Fact]
    public async Task AC5_LosTramitesDeOtraEmpresaGestora_SiguenSinFirmar()
    {
        // Otro tenant: el lote se consulta tenant-scoped, así que ni siquiera entra a la lista.
        var ct = TestContext.Current.CancellationToken;
        var validationId = SeedValidacionAprobada();
        SeedTramiteMarcado("TRM-MIO");
        var otroTenant = SeedMarca(SeedTramite("TRM-OTRO-TENANT"), tenantId: Guid.NewGuid());

        var result = await Consumer().HandleAsync(Evento(validationId), ct);

        result.Firmados.Should().Be(1);
        otroTenant.Estado.Should().Be(DeferredSignatureEstados.Pendiente);
    }

    [Fact]
    public async Task AC6_CadaTramiteDejaTrazaDeLaAplicacionDiferidaYDeSuValidacion()
    {
        var ct = TestContext.Current.CancellationToken;
        var validationId = SeedValidacionAprobada();
        SeedTramiteMarcado("TRM-1");
        var eventos = new List<ProcedureInstanceEvent>();
        await _repo.AddEventAsync(
            Arg.Do<ProcedureInstanceEvent>(eventos.Add), Arg.Any<CancellationToken>());

        await Consumer().HandleAsync(Evento(validationId), ct);

        var evento = eventos.Should().ContainSingle().Subject;
        evento.Tipo.Should().Be("firma_diferida_aplicada");
        evento.Payload.Should().Contain(validationId.ToString());
        var marca = _marks.Todas.Should().ContainSingle().Subject;
        marca.AppliedAt.Should().NotBeNull();
        marca.AppliedValidationId.Should().Be(validationId);
    }

    [Fact]
    public async Task ValidacionRechazada_NoFirmaNada()
    {
        var ct = TestContext.Current.CancellationToken;
        var validationId = SeedValidacionAprobada();
        SeedTramiteMarcado("TRM-1");

        var result = await Consumer().HandleAsync(
            new IdentityValidationCompleted
            {
                TenantId = Tenant,
                ValidationId = validationId,
                Estado = BiometricEstados.Rechazado,
                Parte = "comprador",
            Provider = BiometricProviders.Kyverum,
            },
            ct);

        result.Firmados.Should().Be(0);
        _marks.Todas.Should().OnlyContain(m => m.Estado == DeferredSignatureEstados.Pendiente);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private DeferredSignatureBatchConsumer Consumer() =>
        new(_repo, _marks, _firma, _directorio);

    private static IdentityValidationCompleted Evento(Guid validationId) =>
        new()
        {
            TenantId = Tenant,
            ValidationId = validationId,
            Estado = BiometricEstados.Aprobado,
            Parte = "comprador",
            Provider = BiometricProviders.Kyverum,
        };

    private Guid SeedValidacionAprobada()
    {
        var id = Guid.NewGuid();
        _repo.GetBiometricByIdAsync(id, Arg.Any<CancellationToken>()).Returns(
            new ProcedureInstanceBiometricValidation
            {
                Id = id,
                TenantId = Tenant,
                PartyRole = "comprador",
                Name = "Ana Representante",
                DocumentType = DocType,
                DocumentNumber = Doc,
                Email = "rep@empresa.com",
                Status = BiometricEstados.Aprobado,
                TokenHash = "hash",
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(24),
                MaxAttempts = 3,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        return id;
    }

    private Guid SeedTramite(
        string referencia, string status = TramiteEstado.Borrador, bool subsanacion = false)
    {
        var id = Guid.NewGuid();
        _repo.GetByIdAsync(id, Tenant, Arg.Any<CancellationToken>()).Returns(new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.For("matricula_inicial"),
            Id = id,
            TenantId = Tenant,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = referencia,
            Status = status,
            SubsanacionActiva = subsanacion,
            // Matrícula: el camino de firma pasa por el FUR, no por la compraventa.
            ModalidadEntrada = "matricula_inicial",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        return id;
    }

    private Guid SeedTramiteMarcado(
        string referencia, string status = TramiteEstado.Borrador, bool subsanacion = false)
    {
        var id = SeedTramite(referencia, status, subsanacion);
        SeedMarca(id);
        return id;
    }

    private DeferredSignatureMark SeedMarca(Guid instanceId, string? documento = null, Guid? tenantId = null)
    {
        var mark = new DeferredSignatureMark
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId ?? Tenant,
            ProcedureInstanceId = instanceId,
            PartyRole = "comprador",
            CompanyDocumentNumber = "900123456",
            RepresentativeDocumentType = DocType,
            RepresentativeDocumentNumber = documento ?? Doc,
            Estado = DeferredSignatureEstados.Pendiente,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _marks.Add(mark);
        return mark;
    }

    /// <summary>
    /// Repositorio de marcas en memoria: se necesita el objeto REAL (no un doble) porque las
    /// afirmaciones son sobre el estado en que queda cada marca tras la corrida.
    /// </summary>
    private sealed class FakeMarkRepository : IDeferredSignatureMarkRepository
    {
        private readonly List<DeferredSignatureMark> _items = [];

        public IReadOnlyList<DeferredSignatureMark> Todas => _items;

        public Task<DeferredSignatureMark?> FindPendienteAsync(
            Guid tenantId, Guid procedureInstanceId, string partyRole, CancellationToken cancellationToken = default) =>
            Task.FromResult(_items.FirstOrDefault(m =>
                m.TenantId == tenantId
                && m.ProcedureInstanceId == procedureInstanceId
                && m.PartyRole == partyRole
                && m.EstaPendiente));

        public Task<IReadOnlyList<DeferredSignatureMark>> ListPendientesByRepresentativeAsync(
            Guid tenantId,
            string representativeDocumentType,
            string representativeDocumentNumber,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DeferredSignatureMark>>(_items
                .Where(m => m.TenantId == tenantId
                    && m.EstaPendiente
                    && m.RepresentativeDocumentType == representativeDocumentType
                    && m.RepresentativeDocumentNumber == representativeDocumentNumber)
                .OrderBy(m => m.CreatedAt)
                .ToList());

        public void Add(DeferredSignatureMark mark) => _items.Add(mark);

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
