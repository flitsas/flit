using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Tramites.Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence.Repositories;

public sealed class VehicleSignatureImprintRepositoryTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid InstanceId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public async Task FindByDocumentHashAsync_IgnoresSoftDeletedRow()
    {
        var dbName = Guid.NewGuid().ToString();
        const string hash = "sha-soft-deleted";

        await using (var seed = NewContext(dbName))
        {
            seed.VehicleSignatureImprints.Add(Row(hash, deletedAt: DateTimeOffset.UtcNow));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(dbName);
        var repo = new VehicleSignatureImprintRepository(ctx);

        var found = await repo.FindByDocumentHashAsync(hash, TestContext.Current.CancellationToken);

        found.Should().BeNull("soft-deleted no cuenta para idempotencia activa");
    }

    [Fact]
    public async Task FindByDocumentHashAsync_ReturnsActiveRow()
    {
        var dbName = Guid.NewGuid().ToString();
        const string hash = "sha-active";
        var activeId = Guid.NewGuid();

        await using (var seed = NewContext(dbName))
        {
            seed.VehicleSignatureImprints.Add(Row(hash, id: activeId));
            seed.VehicleSignatureImprints.Add(Row(hash, deletedAt: DateTimeOffset.UtcNow));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(dbName);
        var repo = new VehicleSignatureImprintRepository(ctx);

        var found = await repo.FindByDocumentHashAsync(hash, TestContext.Current.CancellationToken);

        found.Should().NotBeNull();
        found!.Id.Should().Be(activeId);
    }

    private static VehicleSignatureImprint Row(
        string documentHash,
        Guid? id = null,
        DateTimeOffset? deletedAt = null) =>
        new()
        {
            Id = id ?? Guid.NewGuid(),
            TenantId = TenantId,
            ProcedureInstanceId = InstanceId,
            ModuleCode = "tramites",
            PrivateKey = "pk",
            PublicKey = "pub",
            DocumentHash = documentHash,
            Signature = "sig",
            SignedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
            DeletedAt = deletedAt,
        };

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>().UseInMemoryDatabase(dbName).Options);
}
