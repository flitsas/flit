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

    [Fact]
    public async Task ListByPlacaAsync_ReturnsActiveAndSoftDeleted_IncludingMatchAfterTrimUpper()
    {
        var dbName = Guid.NewGuid().ToString();
        var activeId = Guid.NewGuid();
        var deletedId = Guid.NewGuid();
        var otherId = Guid.NewGuid();

        await using (var seed = NewContext(dbName))
        {
            seed.ProcedureInstances.Add(InstanceWithPlate("POV420"));
            seed.ProcedureInstances.Add(InstanceWithPlate("ZZZ999", otherId));
            seed.VehicleSignatureImprints.Add(Row("h1", id: activeId));
            seed.VehicleSignatureImprints.Add(Row("h2", id: deletedId, deletedAt: DateTimeOffset.UtcNow));
            seed.VehicleSignatureImprints.Add(new VehicleSignatureImprint
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                ProcedureInstanceId = otherId,
                ModuleCode = "tramites",
                PrivateKey = "pk",
                PublicKey = "pub",
                DocumentHash = "other",
                Signature = "sig",
                SignedAt = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(dbName);
        var repo = new VehicleSignatureImprintRepository(ctx);

        var rows = await repo.ListByPlacaAsync(TenantId, "  pov420 ", TestContext.Current.CancellationToken);

        rows.Should().HaveCount(2);
        rows.Select(x => x.Id).Should().Contain(new[] { activeId, deletedId });
        foreach (var row in rows)
        {
            row.Placa.Trim().ToUpperInvariant().Should().Be("POV420");
            row.PublicKey.Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public async Task GetByIdAsync_IncludesSoftDeletedRow()
    {
        var dbName = Guid.NewGuid().ToString();
        var deletedId = Guid.NewGuid();
        var deletedAt = DateTimeOffset.UtcNow;

        await using (var seed = NewContext(dbName))
        {
            seed.ProcedureInstances.Add(InstanceWithPlate("ABC123"));
            seed.VehicleSignatureImprints.Add(Row("deleted-hash", id: deletedId, deletedAt: deletedAt));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(dbName);
        var repo = new VehicleSignatureImprintRepository(ctx);

        var found = await repo.GetByIdAsync(deletedId, TestContext.Current.CancellationToken);

        found.Should().NotBeNull();
        found!.DeletedAt.Should().Be(deletedAt);
    }

    private static ProcedureInstance InstanceWithPlate(string plate, Guid? id = null) =>
        new()
        {
            Id = id ?? InstanceId,
            TenantId = TenantId,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "FLIT-TEST",
            Plate = plate,
            CreatedAt = DateTimeOffset.UtcNow,
        };

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
