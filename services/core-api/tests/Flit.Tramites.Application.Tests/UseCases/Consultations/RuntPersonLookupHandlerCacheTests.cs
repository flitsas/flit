using Flit.Tramites.Application.UseCases.Consultations;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.Consultations;

/// <summary>
/// HU #10955 (AC1) — revierte parcialmente HU #10878/ADR-0031: la identidad de un actor SIEMPRE se
/// consulta en vivo contra el RUNT. <see cref="RuntPersonLookupHandler"/> ya NO intenta un HIT de
/// <see cref="ExternalQueryCacheService.TryReusePersonAsync"/>, exista o no un
/// <see cref="PersonDataConsent"/> vigente, ni aunque haya una entrada de caché fresca. El resultado
/// SÍ se sigue cacheando al final (para otros consumidores del cache-aside, p. ej.
/// <c>RunConsultationHandler</c> con templates <c>entity_scope=actor</c>).
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
        ProcedureType = ProcedureTypeFixture.Matricula,
        Id = id,
        TenantId = tenantId,
        ProcedureTypeId = Guid.NewGuid(),
        ReferenceNumber = "TRM-2026-000001",
        Status = TramiteEstado.Borrador,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    /// <summary>
    /// AC1 — aunque exista consentimiento `granted` Y una entrada de caché vigente (el escenario que
    /// ANTES servía un HIT sin llamar al proveedor), el handler SIEMPRE consulta el RUNT en vivo y el
    /// nombre devuelto es el de la consulta fresca, no el del payload cacheado.
    /// </summary>
    [Fact]
    public async Task HandleAsync_ConConsentimientoYCacheVigente_SiempreConsultaEnVivo()
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
                Payload = """[{"fieldKey":"person_full_name","valueText":"NOMBRE CACHEADO","valueJson":null}]""",
                QueriedAt = DateTimeOffset.UtcNow.AddHours(-2),
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(22),
            });

        var provider = new FakeProvider(new ConsultationResult("verifik_conductor", "green",
            [], [new HydratedField("person_full_name", "NOMBRE EN VIVO", null)]));
        _registry.Resolve("verifik_conductor").Returns(provider);

        var (result, error) = await _sut.HandleAsync(id, tenantId, "CC", "123456789", ct);

        error.Should().BeNull();
        provider.Called.Should().BeTrue("AC1: la identidad SIEMPRE se consulta en vivo, con o sin consentimiento/caché");
        result!.FullName.Should().Be("NOMBRE EN VIVO");
        result.Mode.Should().NotBe("cache");
    }

    /// <summary>AC1 — el handler no lee el gate de consentimiento en absoluto: nunca invoca GetAsync.</summary>
    [Fact]
    public async Task HandleAsync_NuncaConsultaElConsentimiento()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        _repo.GetByIdAsync(id, tenantId, ct).Returns(Instance(id, tenantId));
        _registry.Resolve("verifik_conductor").Returns(new FakeProvider(
            new ConsultationResult("verifik_conductor", "green", [], [new HydratedField("person_full_name", "JUAN", null)])));

        var (_, error) = await _sut.HandleAsync(id, tenantId, "CC", "123456789", ct);

        error.Should().BeNull();
        await _consentRepo.DidNotReceive().GetAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// El resultado fresco se sigue cacheando (para otros consumidores del mecanismo, p. ej.
    /// RunConsultationHandler) aunque este handler ya no lea la caché.
    /// </summary>
    [Fact]
    public async Task HandleAsync_TrasConsultarEnVivo_SigueEscribiendoLaCache()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        _repo.GetByIdAsync(id, tenantId, ct).Returns(Instance(id, tenantId));
        // Sin entrada previa: SavePersonResultAsync debe hacer un AddAsync.
        _cacheRepo.FindPersonAsync(tenantId, RuntSourceId, "CC", "123456789", ct)
            .Returns((ExternalQueryCacheEntry?)null);

        var freshResult = new ConsultationResult("verifik_conductor", "green",
            [], [new HydratedField("person_full_name", "JUAN NUEVO", null)]);
        _registry.Resolve("verifik_conductor").Returns(new FakeProvider(freshResult));

        var (result, error) = await _sut.HandleAsync(id, tenantId, "CC", "123456789", ct);

        error.Should().BeNull();
        result!.FullName.Should().Be("JUAN NUEVO");
        await _cacheRepo.Received(1).AddAsync(
            Arg.Is<ExternalQueryCacheEntry>(e =>
                e.TenantId == tenantId && e.DocumentType == "CC" && e.DocumentNumber == "123456789"),
            Arg.Any<CancellationToken>());
    }
}
