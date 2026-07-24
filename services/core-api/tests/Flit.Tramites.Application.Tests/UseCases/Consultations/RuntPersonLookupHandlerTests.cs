using Flit.Tramites.Application.UseCases.Consultations;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using Xunit;
using Flit.Tramites.Domain.Tramites.Estados;

namespace Flit.Tramites.Application.Tests.UseCases.Consultations;

public sealed class RuntPersonLookupHandlerTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly IConsultationProviderRegistry _registry = Substitute.For<IConsultationProviderRegistry>();
    // HU #10878: dependencia nueva (cache-aside). Sin stub de _consentRepo/_cacheRepo, NSubstitute
    // devuelve null => TryReusePersonAsync siempre resuelve "no_consent"/MISS => el resto de la suite
    // ejercita el flujo EXACTO de antes (ver RuntPersonLookupHandlerCacheTests para el HIT/AC2/gate).
    private readonly ICatalogRepository _catalogRepo = Substitute.For<ICatalogRepository>();
    private readonly IExternalQueryCacheRepository _cacheRepo = Substitute.For<IExternalQueryCacheRepository>();
    private readonly IPersonDataConsentRepository _consentRepo = Substitute.For<IPersonDataConsentRepository>();
    private readonly RuntPersonLookupHandler _sut;

    public RuntPersonLookupHandlerTests()
    {
        // Chain resolver con defaults embebidos (conductor → [kyverum_runt_conductor, verifik_conductor])
        // sobre el registry mockeado; sin override de tenant. kyverum_runt_conductor NO está registrado
        // (Resolve → null explícito, si no NSubstitute auto-mockea un provider fantasma) → la cadena cae
        // a verifik_conductor, que es lo que estos tests stubbean.
        _registry.Resolve("kyverum_runt_conductor").Returns((IConsultationProvider?)null);
        var resolver = new ConsultationProviderChainResolver(_registry, new ConsultationChainOptions());
        var cacheService = new ExternalQueryCacheService(_cacheRepo, _consentRepo, _catalogRepo);
        // El mismo registry alimenta la consulta SIMIT best-effort del detalle de comparendos.
        _sut = new RuntPersonLookupHandler(_repo, resolver, new NullOverrideProvider(), cacheService, _registry);
    }

    private sealed class NullOverrideProvider : IConsultationTenantOverrideProvider
    {
        public Task<ConsultationTenantOverride?> GetAsync(Guid tenantId, CancellationToken ct) =>
            Task.FromResult<ConsultationTenantOverride?>(null);
    }

    private sealed class FakeProvider(ConsultationResult result) : IConsultationProvider
    {
        public string Key => "verifik_conductor";
        public ConsultationContext? CapturedContext { get; private set; }

        public Task<ConsultationResult> ConsultAsync(ConsultationContext ctx, CancellationToken ct)
        {
            CapturedContext = ctx;
            return Task.FromResult(result);
        }
    }

    private static ProcedureInstance Instance(Guid id, Guid tenantId) =>
        new()
        {
            Id = id,
            TenantId = tenantId,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000001",
            Status = TramiteEstado.Borrador,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private static ConsultationResult FoundResult() =>
        new("verifik_conductor", "green",
            [new ConsultationCheck("conductor_identidad", "Persona en RUNT", "ok", "verifik_conductor", null)],
            [
                new HydratedField("person_full_name", "JUAN CARLOS PEREZ GOMEZ", null),
                new HydratedField("person_first_name", "JUAN CARLOS", null),
                new HydratedField("person_last_name", "PEREZ GOMEZ", null),
                new HydratedField("person_license_status", "ACTIVO", null),
                new HydratedField("person_citizen_status", "ACTIVA", null),
                new HydratedField("person_has_pending_fines", "false", null),
                new HydratedField("person_paz_y_salvo", "PAZ-Y-SALVO-001", null),
                new HydratedField("person_has_active_license", "true", null),
                new HydratedField("person_license_categories", "B1", null),
            ]);

    private static ConsultationResult NotFoundResult() =>
        new("verifik_conductor", "yellow",
            [new ConsultationCheck("conductor", "Persona en RUNT", "unknown", "verifik_conductor", "Persona no encontrada en RUNT")],
            []);

    // Igual que FoundResult pero con el flag de multas en "true": dispara la consulta SIMIT del detalle.
    private static ConsultationResult FoundConFinesResult() =>
        new("verifik_conductor", "yellow",
            [new ConsultationCheck("conductor_identidad", "Persona en RUNT", "ok", "verifik_conductor", null)],
            [
                new HydratedField("person_full_name", "DANIEL AMADO GARCIA", null),
                new HydratedField("person_has_pending_fines", "true", null),
                new HydratedField("person_has_active_license", "true", null),
            ]);

    // Proveedor de multas (verifik_simit) que devuelve un comparendo con detalle.
    private sealed class FinesProviderStub : IConsultationProvider
    {
        public string Key => "verifik_simit";
        public ConsultationContext? CapturedContext { get; private set; }

        public Task<ConsultationResult> ConsultAsync(ConsultationContext ctx, CancellationToken ct)
        {
            CapturedContext = ctx;
            var multas = FinesCheckFactory.Multas("verifik_simit", 1, 344_730m,
                [new FineDetail("25612001000012662173", "2024-05-01", 344_730m, "STRIA SABANETA", "Pendiente", "Semáforo en rojo")]);
            return Task.FromResult(new ConsultationResult("verifik_simit", "yellow", [multas], []));
        }
    }

    [Fact]
    public async Task HandleAsync_InvalidRequest_WhenBlankDocument()
    {
        var ct = TestContext.Current.CancellationToken;

        var (result, error) = await _sut.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), "CC", "   ", ct);

        error.Should().Be("invalid_request");
        result.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_InstanceNotFound_ReturnsError()
    {
        var ct = TestContext.Current.CancellationToken;
        _repo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns((ProcedureInstance?)null);

        var (result, error) = await _sut.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), "CC", "123456789", ct);

        error.Should().Be("instance_not_found");
        result.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_NingunProviderDeLaCadenaRegistrado_DevuelveFoundFalseGraceful()
    {
        // HU #10478: si ningún provider de la cadena (kyverum_runt_conductor, verifik_conductor) está
        // registrado, el chain resolver degrada a un resultado con check error → el lookup no explota,
        // devuelve Found=false (el frontend cae al ingreso manual).
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        _repo.GetByIdAsync(id, tenantId, ct).Returns(Instance(id, tenantId));
        _registry.Resolve("verifik_conductor").Returns((IConsultationProvider?)null);

        var (result, error) = await _sut.HandleAsync(id, tenantId, "CC", "123456789", ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
        result!.Found.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_Found_ReturnsDtoWithName()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        _repo.GetByIdAsync(id, tenantId, ct).Returns(Instance(id, tenantId));
        var provider = new FakeProvider(FoundResult());
        _registry.Resolve("verifik_conductor").Returns(provider);

        var (result, error) = await _sut.HandleAsync(id, tenantId, "CC", "123456789", ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
        result!.Found.Should().BeTrue();
        result.FullName.Should().Be("JUAN CARLOS PEREZ GOMEZ");
        result.FirstName.Should().Be("JUAN CARLOS");
        result.LastName.Should().Be("PEREZ GOMEZ");
        result.LicenseStatus.Should().Be("ACTIVO");
        result.DocumentType.Should().Be("CC");
        result.DocumentNumber.Should().Be("123456789");
        result.Source.Should().Be("RUNT");

        // El lookup NO persiste y NO lee los field_values de la instancia: solo arma un
        // contexto en memoria con el documento consultado.
        provider.CapturedContext.Should().NotBeNull();
        provider.CapturedContext!.FieldValues.Should().ContainKey("document_type");
        provider.CapturedContext.FieldValues["document_number"].Should().Be("123456789");
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _repo.DidNotReceive().GetByIdWithDetailsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_NotFound_ReturnsDtoFoundFalse_Still200Shape()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        _repo.GetByIdAsync(id, tenantId, ct).Returns(Instance(id, tenantId));
        _registry.Resolve("verifik_conductor").Returns(new FakeProvider(NotFoundResult()));

        var (result, error) = await _sut.HandleAsync(id, tenantId, "CC", "999999999", ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
        result!.Found.Should().BeFalse();
        result.FullName.Should().BeNull();
        result.FirstName.Should().BeNull();
        result.LastName.Should().BeNull();
        result.LicenseStatus.Should().BeNull();
        result.DocumentNumber.Should().Be("999999999");
    }

    [Fact]
    public async Task HandleAsync_Found_MapeaCamposEnriquecidos()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        _repo.GetByIdAsync(id, tenantId, ct).Returns(Instance(id, tenantId));
        var provider = new FakeProvider(FoundResult());
        _registry.Resolve("verifik_conductor").Returns(provider);

        var (result, error) = await _sut.HandleAsync(id, tenantId, "CC", "123456789", ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
        result!.CitizenStatus.Should().Be("ACTIVA");
        result.HasPendingFines.Should().BeFalse();
        result.HasActiveLicense.Should().BeTrue();
        result.LicenseCategories.Should().Be("B1");
        result.NroPazYSalvo.Should().Be("PAZ-Y-SALVO-001");
    }

    [Fact]
    public async Task HandleAsync_ConMultas_AdjuntaDetalleDeComparendosDesdeSimit()
    {
        // El RUNT marca multas (flag) pero no trae el detalle: se consulta el SIMIT del mismo documento
        // y su detalle de comparendos viaja en el DTO para pintarlo junto a la alerta en la ficha.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        _repo.GetByIdAsync(id, tenantId, ct).Returns(Instance(id, tenantId));
        _registry.Resolve("verifik_conductor").Returns(new FakeProvider(FoundConFinesResult()));
        var fines = new FinesProviderStub();
        _registry.Resolve("verifik_simit").Returns(fines);

        var (result, error) = await _sut.HandleAsync(id, tenantId, "CC", "1193552679", ct);

        error.Should().BeNull();
        result!.HasPendingFines.Should().BeTrue();
        result.Fines.Should().ContainSingle();
        result.Fines![0].Numero.Should().Be("25612001000012662173");
        result.Fines[0].Valor.Should().Be(344_730m);
        result.Fines[0].Infraccion.Should().Be("Semáforo en rojo");
        // El SIMIT se consultó con el documento del actor.
        fines.CapturedContext!.FieldValues["owner_document_number"].Should().Be("1193552679");
    }

    [Fact]
    public async Task HandleAsync_SinMultas_NoConsultaSimit_NiAdjuntaDetalle()
    {
        // Sin flag de multas no se gasta una consulta SIMIT: el detalle queda en null.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        _repo.GetByIdAsync(id, tenantId, ct).Returns(Instance(id, tenantId));
        _registry.Resolve("verifik_conductor").Returns(new FakeProvider(FoundResult()));
        var fines = new FinesProviderStub();
        _registry.Resolve("verifik_simit").Returns(fines);

        var (result, error) = await _sut.HandleAsync(id, tenantId, "CC", "123456789", ct);

        error.Should().BeNull();
        result!.HasPendingFines.Should().BeFalse();
        result.Fines.Should().BeNull();
        fines.CapturedContext.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_NIT_RetornaUnsupportedDocumentType()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        _repo.GetByIdAsync(id, tenantId, ct).Returns(Instance(id, tenantId));
        _registry.Resolve("verifik_conductor").Returns(new FakeProvider(FoundResult()));

        var (result, error) = await _sut.HandleAsync(id, tenantId, "NIT", "900123456", ct);

        error.Should().Be("unsupported_document_type");
        result.Should().BeNull();
    }
}
