using Flit.Tramites.Application.UseCases.Consultations;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.Consultations;

/// <summary>
/// HU #10878 (Feature #10862, CF-04, ADR-0030/ADR-0031) — unidad de <see cref="ExternalQueryCacheService"/>
/// aislada de los 3 handlers consumidores.
/// </summary>
public sealed class ExternalQueryCacheServiceTests
{
    private readonly IExternalQueryCacheRepository _cacheRepo = Substitute.For<IExternalQueryCacheRepository>();
    private readonly IPersonDataConsentRepository _consentRepo = Substitute.For<IPersonDataConsentRepository>();
    private readonly ICatalogRepository _catalogRepo = Substitute.For<ICatalogRepository>();
    private readonly ExternalQueryCacheService _sut;

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid SourceId = Guid.NewGuid();

    public ExternalQueryCacheServiceTests()
    {
        _sut = new ExternalQueryCacheService(_cacheRepo, _consentRepo, _catalogRepo);
    }

    [Fact]
    public async Task TryReusePersonAsync_SinConsentimiento_DevuelveMissNoConsent_SinTocarCache()
    {
        var ct = TestContext.Current.CancellationToken;

        var result = await _sut.TryReusePersonAsync(TenantId, "RUNT", "CC", "123", DateTimeOffset.UtcNow, ct);

        result.Hit.Should().BeFalse();
        result.MissReason.Should().Be("no_consent");
        await _cacheRepo.DidNotReceive().FindPersonAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TryReusePersonAsync_ConsentimientoRevocado_DevuelveMissNoConsent()
    {
        var ct = TestContext.Current.CancellationToken;
        _consentRepo.GetAsync(TenantId, "CC", "123", ct).Returns(new PersonDataConsent
        {
            TenantId = TenantId,
            DocumentType = "CC",
            DocumentNumber = "123",
            Status = PersonDataConsentStatus.Revoked,
            RevokedAt = DateTimeOffset.UtcNow,
        });

        var result = await _sut.TryReusePersonAsync(TenantId, "RUNT", "CC", "123", DateTimeOffset.UtcNow, ct);

        result.Hit.Should().BeFalse();
        result.MissReason.Should().Be("no_consent");
    }

    [Fact]
    public async Task TryReuseVehicleAsync_SinEntradaCacheada_DevuelveMissNotFound()
    {
        var ct = TestContext.Current.CancellationToken;
        _catalogRepo.GetExternalDataSourceByCodeAsync("RUNT", ct)
            .Returns(new ExternalDataSource { Id = SourceId, Code = "RUNT", CacheTtlHours = 24 });

        var result = await _sut.TryReuseVehicleAsync(TenantId, "RUNT", "abc123", DateTimeOffset.UtcNow, ct);

        result.Hit.Should().BeFalse();
        result.MissReason.Should().Be("not_found_or_expired");
        // Normaliza a mayúsculas antes de consultar.
        await _cacheRepo.Received(1).FindVehicleAsync(TenantId, SourceId, "ABC123", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SavePersonResultAsync_FuenteNoCatalogada_NoCachea()
    {
        var ct = TestContext.Current.CancellationToken;
        _catalogRepo.GetExternalDataSourceByCodeAsync("DESCONOCIDA", ct).Returns((ExternalDataSource?)null);

        await _sut.SavePersonResultAsync(
            TenantId, "DESCONOCIDA", "CC", "123", null, [], DateTimeOffset.UtcNow, ct);

        await _cacheRepo.DidNotReceive().AddAsync(Arg.Any<ExternalQueryCacheEntry>(), Arg.Any<CancellationToken>());
        await _cacheRepo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SavePersonResultAsync_TtlCeroOMenor_NoCachea()
    {
        var ct = TestContext.Current.CancellationToken;
        _catalogRepo.GetExternalDataSourceByCodeAsync("RUNT", ct)
            .Returns(new ExternalDataSource { Id = SourceId, Code = "RUNT", CacheTtlHours = 0 });

        await _sut.SavePersonResultAsync(
            TenantId, "RUNT", "CC", "123", null, [], DateTimeOffset.UtcNow, ct);

        await _cacheRepo.DidNotReceive().AddAsync(Arg.Any<ExternalQueryCacheEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SavePersonResultAsync_TtlNulo_UsaDefaultGlobal()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;
        _catalogRepo.GetExternalDataSourceByCodeAsync("RUNT", ct)
            .Returns(new ExternalDataSource { Id = SourceId, Code = "RUNT", CacheTtlHours = null });

        ExternalQueryCacheEntry? captured = null;
        await _cacheRepo.AddAsync(Arg.Do<ExternalQueryCacheEntry>(e => captured = e), Arg.Any<CancellationToken>());

        await _sut.SavePersonResultAsync(
            TenantId, "RUNT", "CC", "123", null, [new HydratedField("k", "v", null)], now, ct);

        captured.Should().NotBeNull();
        captured!.ExpiresAt.Should().Be(now.AddHours(ExternalQueryCacheRules.DefaultTtlHours));
        captured.SubjectKind.Should().Be(ExternalQueryCacheRules.SubjectKindPerson);
        captured.DocumentType.Should().Be("CC");
    }

    [Fact]
    public async Task SavePersonResultAsync_ConEntradaExistente_Actualiza_NoDuplica()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;
        _catalogRepo.GetExternalDataSourceByCodeAsync("RUNT", ct)
            .Returns(new ExternalDataSource { Id = SourceId, Code = "RUNT", CacheTtlHours = 24 });

        var existing = new ExternalQueryCacheEntry
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            ExternalDataSourceId = SourceId,
            SubjectKind = ExternalQueryCacheRules.SubjectKindPerson,
            DocumentType = "CC",
            DocumentNumber = "123",
            Payload = "[]",
            QueriedAt = now.AddDays(-1),
            ExpiresAt = now.AddHours(-1),
        };
        _cacheRepo.FindPersonAsync(TenantId, SourceId, "CC", "123", ct).Returns(existing);

        await _sut.SavePersonResultAsync(
            TenantId, "RUNT", "CC", "123", null, [new HydratedField("person_full_name", "X", null)], now, ct);

        existing.Payload.Should().Contain("person_full_name");
        existing.ExpiresAt.Should().Be(now.AddHours(24));
        await _cacheRepo.DidNotReceive().AddAsync(Arg.Any<ExternalQueryCacheEntry>(), Arg.Any<CancellationToken>());
        await _cacheRepo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
