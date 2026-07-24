using Flit.Tramites.Application.UseCases.Consultations;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.Consultations;

/// <summary>
/// HU #10878 (Feature #10862, CF-04, ADR-0030/ADR-0031) — cache-aside en <see cref="RuntPersonLookupHandler"/>.
/// </summary>
public sealed class RuntPersonLookupHandlerCacheTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly IConsultationProviderRegistry _registry = Substitute.For<IConsultationProviderRegistry>();
    private readonly ICatalogRepository _catalogRepo = Substitute.For<ICatalogRepository>();
    private readonly IExternalQueryCacheRepository _cacheRepo = Substitute.For<IExternalQueryCacheRepository>();
    private readonly IPersonDataConsentRepository _consentRepo = Substitute.For<IPersonDataConsentRepository>();
    private readonly RuntPersonLookupHandler _sut;

    private static readonly Guid RuntSourceId = Guid.NewGuid();

    private sealed class NullOverrideProvider : IConsultationTenantOverrideProvider
    {
        public Task<ConsultationTenantOverride?> GetAsync(Guid tenantId, CancellationToken ct) =>
            Task.FromResult<ConsultationTenantOverride?>(null);
    }

    private sealed class FakeProvider(ConsultationResult result) : IConsultationProvider
    {
        public string Key => "verifik_conductor";
        public bool Called { get; private set; }

        public Task<ConsultationResult> ConsultAsync(ConsultationContext ctx, CancellationToken ct)
        {
            Called = true;
            return Task.FromResult(result);
        }
    }

    public RuntPersonLookupHandlerCacheTests()
    {
        _registry.Resolve("kyverum_runt_conductor").Returns((IConsultationProvider?)null);
        var resolver = new ConsultationProviderChainResolver(_registry, new ConsultationChainOptions());
        var cacheService = new ExternalQueryCacheService(_cacheRepo, _consentRepo, _catalogRepo);
        _sut = new RuntPersonLookupHandler(_repo, resolver, new NullOverrideProvider(), cacheService, _registry);

        _catalogRepo.GetExternalDataSourceByCodeAsync("RUNT", Arg.Any<CancellationToken>())
            .Returns(new ExternalDataSource { Id = RuntSourceId, Code = "RUNT", CacheTtlHours = 24 });
    }

    private static ProcedureInstance Instance(Guid id, Guid tenantId) => new()
    {
        Id = id,
        TenantId = tenantId,
        ProcedureTypeId = Guid.NewGuid(),
        ReferenceNumber = "TRM-2026-000001",
        Status = TramiteEstado.Borrador,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task HandleAsync_ConConsentimientoYCacheVigente_NoLlamaProveedor()
    {
        // AC1
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        _repo.GetByIdAsync(id, tenantId, ct).Returns(Instance(id, tenantId));

        _consentRepo.GetAsync(tenantId, "CC", "123456789", ct)
            .Returns(new PersonDataConsent
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                DocumentType = "CC",
                DocumentNumber = "123456789",
                Status = PersonDataConsentStatus.Granted,
                GrantedAt = DateTimeOffset.UtcNow.AddDays(-1),
            });

        var cachedEntry = new ExternalQueryCacheEntry
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ExternalDataSourceId = RuntSourceId,
            SubjectKind = ExternalQueryCacheRules.SubjectKindPerson,
            DocumentType = "CC",
            DocumentNumber = "123456789",
            Payload = """[{"fieldKey":"person_full_name","valueText":"JUAN PEREZ","valueJson":null}]""",
            QueriedAt = DateTimeOffset.UtcNow.AddHours(-2),
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(22),
        };
        _cacheRepo.FindPersonAsync(tenantId, RuntSourceId, "CC", "123456789", ct).Returns(cachedEntry);

        var provider = new FakeProvider(new ConsultationResult("verifik_conductor", "green", [], []));
        _registry.Resolve("verifik_conductor").Returns(provider);

        var (result, error) = await _sut.HandleAsync(id, tenantId, "CC", "123456789", ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
        result!.Found.Should().BeTrue();
        result.FullName.Should().Be("JUAN PEREZ");
        provider.Called.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_CacheVencida_Reconsulta_Y_Recachea()
    {
        // AC2
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        _repo.GetByIdAsync(id, tenantId, ct).Returns(Instance(id, tenantId));

        _consentRepo.GetAsync(tenantId, "CC", "123456789", ct)
            .Returns(new PersonDataConsent
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                DocumentType = "CC",
                DocumentNumber = "123456789",
                Status = PersonDataConsentStatus.Granted,
                GrantedAt = DateTimeOffset.UtcNow.AddDays(-1),
            });

        var expiredEntry = new ExternalQueryCacheEntry
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ExternalDataSourceId = RuntSourceId,
            SubjectKind = ExternalQueryCacheRules.SubjectKindPerson,
            DocumentType = "CC",
            DocumentNumber = "123456789",
            Payload = """[{"fieldKey":"person_full_name","valueText":"JUAN VIEJO","valueJson":null}]""",
            QueriedAt = DateTimeOffset.UtcNow.AddDays(-2),
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(-1),
        };
        _cacheRepo.FindPersonAsync(tenantId, RuntSourceId, "CC", "123456789", ct).Returns(expiredEntry);

        var freshResult = new ConsultationResult("verifik_conductor", "green",
            [], [new HydratedField("person_full_name", "JUAN NUEVO", null)]);
        var provider = new FakeProvider(freshResult);
        _registry.Resolve("verifik_conductor").Returns(provider);

        var (result, error) = await _sut.HandleAsync(id, tenantId, "CC", "123456789", ct);

        error.Should().BeNull();
        provider.Called.Should().BeTrue();
        result!.FullName.Should().Be("JUAN NUEVO");
        expiredEntry.Payload.Should().Contain("JUAN NUEVO");
        expiredEntry.ExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task HandleAsync_SinConsentimiento_NuncaPrecarga_ConsultaFresca()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        _repo.GetByIdAsync(id, tenantId, ct).Returns(Instance(id, tenantId));
        // Sin consentimiento (GetAsync sin stub => null).

        var vigenteEntry = new ExternalQueryCacheEntry
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ExternalDataSourceId = RuntSourceId,
            SubjectKind = ExternalQueryCacheRules.SubjectKindPerson,
            DocumentType = "CC",
            DocumentNumber = "123456789",
            Payload = """[{"fieldKey":"person_full_name","valueText":"NO DEBERIA VERSE","valueJson":null}]""",
            QueriedAt = DateTimeOffset.UtcNow.AddHours(-1),
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(23),
        };
        _cacheRepo.FindPersonAsync(tenantId, RuntSourceId, "CC", "123456789", ct).Returns(vigenteEntry);

        var freshResult = new ConsultationResult("verifik_conductor", "green",
            [], [new HydratedField("person_full_name", "CONSULTA FRESCA", null)]);
        var provider = new FakeProvider(freshResult);
        _registry.Resolve("verifik_conductor").Returns(provider);

        var (result, error) = await _sut.HandleAsync(id, tenantId, "CC", "123456789", ct);

        error.Should().BeNull();
        provider.Called.Should().BeTrue();
        result!.FullName.Should().Be("CONSULTA FRESCA");
        vigenteEntry.ReuseCount.Should().Be(0);
    }

    /// <summary>
    /// Regresión — la caché guarda el FLAG de multas pero NO el detalle de cada comparendo (el
    /// detalle es una sub-consulta SIMIT best-effort que no viaja en el payload). Al reusar la
    /// persona, la ficha del actor mostraba la alerta "Comparendos/Multas pendientes" pero SIN la
    /// lista: se perdía información que el operador sí veía en la primera consulta. El detalle debe
    /// recomponerse también en el HIT.
    /// </summary>
    [Fact]
    public async Task HandleAsync_HitDeCacheConMultas_DevuelveElDetalleDeComparendos()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        _repo.GetByIdAsync(id, tenantId, ct).Returns(Instance(id, tenantId));

        _consentRepo.GetAsync(tenantId, "CC", "1193552679", ct)
            .Returns(new PersonDataConsent
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                DocumentType = "CC",
                DocumentNumber = "1193552679",
                Status = PersonDataConsentStatus.Granted,
                GrantedAt = DateTimeOffset.UtcNow.AddDays(-1),
            });

        _cacheRepo.FindPersonAsync(tenantId, RuntSourceId, "CC", "1193552679", ct)
            .Returns(new ExternalQueryCacheEntry
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ExternalDataSourceId = RuntSourceId,
                SubjectKind = ExternalQueryCacheRules.SubjectKindPerson,
                DocumentType = "CC",
                DocumentNumber = "1193552679",
                // Lo que realmente guarda la caché: nombre + FLAG de multas, sin detalle.
                Payload = """
                [{"fieldKey":"person_full_name","valueText":"DANIEL AMADO GARCIA","valueJson":null},
                 {"fieldKey":"person_has_pending_fines","valueText":"true","valueJson":null}]
                """,
                QueriedAt = DateTimeOffset.UtcNow.AddHours(-2),
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(22),
            });

        var comparendo = new FineDetail(
            "05001000000044805008", "26/01/2025", 651906m, "Medellín", "Pendiente de pago",
            "Conducir un vehículo a velocidad superior a la máxima permitida.");
        var simit = new FakeFinesProvider([comparendo]);
        _registry.Resolve("verifik_simit").Returns(simit);

        var (result, error) = await _sut.HandleAsync(id, tenantId, "CC", "1193552679", ct);

        error.Should().BeNull();
        result!.HasPendingFines.Should().BeTrue();
        simit.Called.Should().BeTrue("el detalle no está en la caché, hay que volver a pedirlo");
        result.Fines.Should().ContainSingle()
            .Which.Numero.Should().Be("05001000000044805008");
    }

    /// <summary>Sin multas pendientes, el HIT no gasta una consulta de comparendos.</summary>
    [Fact]
    public async Task HandleAsync_HitDeCacheSinMultas_NoConsultaComparendos()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        _repo.GetByIdAsync(id, tenantId, ct).Returns(Instance(id, tenantId));

        _consentRepo.GetAsync(tenantId, "CC", "123456789", ct)
            .Returns(new PersonDataConsent
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                DocumentType = "CC",
                DocumentNumber = "123456789",
                Status = PersonDataConsentStatus.Granted,
                GrantedAt = DateTimeOffset.UtcNow.AddDays(-1),
            });

        _cacheRepo.FindPersonAsync(tenantId, RuntSourceId, "CC", "123456789", ct)
            .Returns(new ExternalQueryCacheEntry
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ExternalDataSourceId = RuntSourceId,
                SubjectKind = ExternalQueryCacheRules.SubjectKindPerson,
                DocumentType = "CC",
                DocumentNumber = "123456789",
                Payload = """[{"fieldKey":"person_full_name","valueText":"JUAN PEREZ","valueJson":null}]""",
                QueriedAt = DateTimeOffset.UtcNow.AddHours(-2),
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(22),
            });

        var simit = new FakeFinesProvider([]);
        _registry.Resolve("verifik_simit").Returns(simit);

        var (result, error) = await _sut.HandleAsync(id, tenantId, "CC", "123456789", ct);

        error.Should().BeNull();
        result!.HasPendingFines.Should().BeFalse();
        simit.Called.Should().BeFalse();
        result.Fines.Should().BeNull();
    }

    /// <summary>Proveedor de comparendos que devuelve un detalle fijo, contando invocaciones.</summary>
    private sealed class FakeFinesProvider(IReadOnlyList<FineDetail> fines) : IConsultationProvider
    {
        public string Key => "verifik_simit";
        public bool Called { get; private set; }

        public Task<ConsultationResult> ConsultAsync(ConsultationContext ctx, CancellationToken ct)
        {
            Called = true;
            return Task.FromResult(new ConsultationResult(
                Key,
                "yellow",
                [new ConsultationCheck(FinesCheckFactory.KeyMultas, "Multas", "warn", Key, "1 comparendo", fines)],
                []));
        }
    }
}
