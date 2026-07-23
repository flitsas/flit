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
}
