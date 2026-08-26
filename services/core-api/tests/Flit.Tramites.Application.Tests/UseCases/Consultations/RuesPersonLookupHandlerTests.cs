using Flit.Tramites.Application.UseCases.Consultations;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.Consultations;

/// <summary>
/// HU #10878 (Feature #10862, CF-04, ADR-0030/ADR-0031) — <see cref="RuesPersonLookupHandler"/>,
/// incluyendo el cache-aside cross-trámite.
/// </summary>
public sealed class RuesPersonLookupHandlerTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly IConsultationProviderRegistry _registry = Substitute.For<IConsultationProviderRegistry>();
    private readonly ICatalogRepository _catalogRepo = Substitute.For<ICatalogRepository>();
    private readonly IExternalQueryCacheRepository _cacheRepo = Substitute.For<IExternalQueryCacheRepository>();
    private readonly IPersonDataConsentRepository _consentRepo = Substitute.For<IPersonDataConsentRepository>();
    private readonly RuesPersonLookupHandler _sut;

    private static readonly Guid RuesSourceId = Guid.NewGuid();

    private sealed class FakeProvider(ConsultationResult result) : IConsultationProvider
    {
        public string Key => "verifik_rues";
        public bool Called { get; private set; }

        public Task<ConsultationResult> ConsultAsync(ConsultationContext ctx, CancellationToken ct)
        {
            Called = true;
            return Task.FromResult(result);
        }
    }

    public RuesPersonLookupHandlerTests()
    {
        var cacheService = new ExternalQueryCacheService(_cacheRepo, _consentRepo, _catalogRepo);
        _sut = new RuesPersonLookupHandler(_repo, _registry, cacheService);

        _catalogRepo.GetExternalDataSourceByCodeAsync("RUES", Arg.Any<CancellationToken>())
            .Returns(new ExternalDataSource { Id = RuesSourceId, Code = "RUES", CacheTtlHours = 720 });
    }

    private static ProcedureInstance Instance(Guid id, Guid tenantId) => new()
    {
        ProcedureType = ProcedureTypeFixture.Matricula,
        Id = id,
        TenantId = tenantId,
        ProcedureTypeId = Guid.NewGuid(),
        ReferenceNumber = "TRM-2026-000001",
        Status = TramiteEstado.Borrador,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task HandleAsync_InvalidRequest_WhenBlankDocument()
    {
        var ct = TestContext.Current.CancellationToken;

        var (result, error) = await _sut.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), "   ", ct);

        error.Should().Be("invalid_request");
        result.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_InstanceNotFound_ReturnsError()
    {
        var ct = TestContext.Current.CancellationToken;
        _repo.GetByIdWithDetailsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns((ProcedureInstance?)null);

        var (result, error) = await _sut.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), "900123456", ct);

        error.Should().Be("instance_not_found");
        result.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_ProviderNotFound_WhenRegistryReturnsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        _repo.GetByIdWithDetailsAsync(id, tenantId, ct).Returns(Instance(id, tenantId));
        _registry.Resolve("verifik_rues").Returns((IConsultationProvider?)null);

        var (result, error) = await _sut.HandleAsync(id, tenantId, "900123456", ct);

        error.Should().Be("provider_not_found");
        result.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_Found_ReturnsDtoWithRazonSocial()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        _repo.GetByIdWithDetailsAsync(id, tenantId, ct).Returns(Instance(id, tenantId));
        var providerResult = new ConsultationResult("verifik_rues", "green", [],
            [
                new HydratedField("rues_razon_social", "ACME S.A.S.", null),
                new HydratedField("rues_estado", "ACTIVA", null),
            ]);
        var provider = new FakeProvider(providerResult);
        _registry.Resolve("verifik_rues").Returns(provider);

        var (result, error) = await _sut.HandleAsync(id, tenantId, "900123456", ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
        result!.Found.Should().BeTrue();
        result.RazonSocial.Should().Be("ACME S.A.S.");
        result.DocumentNumber.Should().Be("900123456");
        provider.Called.Should().BeTrue();
    }

    /// <summary>
    /// HU #10955 (AC1, extendido a RUES). ANTES este test verificaba lo contrario: con consentimiento
    /// `granted` y caché vigente, el handler servía el HIT y NO llamaba al proveedor. La decisión de
    /// producto del 2026-07-27 revierte eso: la identidad se consulta SIEMPRE en vivo, así que ni el
    /// consentimiento ni una caché vigente evitan la llamada al RUES, y el resultado devuelto es el
    /// FRESCO del proveedor, no el cacheado.
    /// </summary>
    [Fact]
    public async Task HandleAsync_ConConsentimientoYCacheVigente_IgualConsultaElProveedor()
    {
        // AC1
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        _repo.GetByIdWithDetailsAsync(id, tenantId, ct).Returns(Instance(id, tenantId));

        _consentRepo.GetAsync(tenantId, "NIT", "900123456", ct)
            .Returns(new PersonDataConsent
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                DocumentType = "NIT",
                DocumentNumber = "900123456",
                Status = PersonDataConsentStatus.Granted,
                GrantedAt = DateTimeOffset.UtcNow.AddDays(-1),
            });

        var cachedEntry = new ExternalQueryCacheEntry
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ExternalDataSourceId = RuesSourceId,
            SubjectKind = ExternalQueryCacheRules.SubjectKindPerson,
            DocumentType = "NIT",
            DocumentNumber = "900123456",
            Payload = """[{"fieldKey":"rues_razon_social","valueText":"ACME CACHEADA","valueJson":null}]""",
            QueriedAt = DateTimeOffset.UtcNow.AddDays(-1),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(29),
        };
        _cacheRepo.FindPersonAsync(tenantId, RuesSourceId, "NIT", "900123456", ct).Returns(cachedEntry);

        var provider = new FakeProvider(new ConsultationResult(
            "verifik_rues",
            "green",
            [],
            [new HydratedField("rues_razon_social", "ACME FRESCA", null)]));
        _registry.Resolve("verifik_rues").Returns(provider);

        var (result, error) = await _sut.HandleAsync(id, tenantId, "900123456", ct);

        error.Should().BeNull();
        result!.Found.Should().BeTrue();
        // El dato viene del proveedor en vivo, NO del payload cacheado ("ACME CACHEADA").
        result.RazonSocial.Should().Be("ACME FRESCA");
        provider.Called.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_SinConsentimiento_ConsultaFresca_AunConCacheVigente()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        _repo.GetByIdWithDetailsAsync(id, tenantId, ct).Returns(Instance(id, tenantId));

        var vigenteEntry = new ExternalQueryCacheEntry
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ExternalDataSourceId = RuesSourceId,
            SubjectKind = ExternalQueryCacheRules.SubjectKindPerson,
            DocumentType = "NIT",
            DocumentNumber = "900123456",
            Payload = """[{"fieldKey":"rues_razon_social","valueText":"NO DEBERIA VERSE","valueJson":null}]""",
            QueriedAt = DateTimeOffset.UtcNow.AddDays(-1),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(29),
        };
        _cacheRepo.FindPersonAsync(tenantId, RuesSourceId, "NIT", "900123456", ct).Returns(vigenteEntry);

        var providerResult = new ConsultationResult("verifik_rues", "green", [],
            [new HydratedField("rues_razon_social", "ACME FRESCA", null)]);
        var provider = new FakeProvider(providerResult);
        _registry.Resolve("verifik_rues").Returns(provider);

        var (result, error) = await _sut.HandleAsync(id, tenantId, "900123456", ct);

        error.Should().BeNull();
        provider.Called.Should().BeTrue();
        result!.RazonSocial.Should().Be("ACME FRESCA");
    }
}
