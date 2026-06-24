using Flit.Admin.Application.OtClientProcedures.ApproveOtClientProcedure;
using Flit.Admin.Application.OtProfile;
using Flit.Admin.Domain.OtProfile;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Admin.Tests.OtClientProcedures;

/// <summary>Tests QuipuxReadOnly en handlers approve/reject (HU #10215 AC4 + Fase 1).</summary>
public sealed class OtClientProcedureQuipuxReadOnlyTests
{
    private static readonly Guid OtTenant = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ClientTenant = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid TransitOffice = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    private static readonly Guid ProcedureType = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task Approve_ReturnsQuipuxReadOnly_WhenProfileIsQxReadOnly()
    {
        var db = NewDbName();
        var procedureId = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            SeedQxProfile(seed);
            SeedGrant(seed);
            SeedProcedure(seed, procedureId);
        }

        await using var ctx = NewContext(db);
        var handler = new ApproveOtClientProcedureHandler(
            new OtClientProcedureRepository(ctx),
            new QuipuxReadOnlyGuard(new OtProfileRepository(ctx)));

        var result = await handler.HandleAsync(new ApproveOtClientProcedureCommand
        {
            OtTenantId = OtTenant,
            ProcedureInstanceId = procedureId,
            ApprovedBy = Guid.NewGuid(),
        }, TestContext.Current.CancellationToken);

        result.Status.Should().Be(ApproveOtClientProcedureStatus.QuipuxReadOnly);
    }

    private static void SeedQxProfile(FlitDbContext ctx)
    {
        ctx.TransitOfficeProfiles.Add(new TransitOfficeProfile
        {
            Id = Guid.NewGuid(),
            TenantId = OtTenant,
            TransitOfficeId = TransitOffice,
            OperationMode = OtOperationModes.Quipux,
            QuipuxReadOnly = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        ctx.SaveChanges();
    }

    private static void SeedGrant(FlitDbContext ctx)
    {
        ctx.TenantTransitOfficeGrants.Add(new TenantTransitOfficeGrant
        {
            Id = Guid.NewGuid(),
            TenantId = ClientTenant,
            TransitOfficeId = TransitOffice,
            IsEnabled = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        ctx.SaveChanges();
    }

    private static void SeedProcedure(FlitDbContext ctx, Guid id)
    {
        ctx.ProcedureInstances.Add(new ProcedureInstance
        {
            Id = id,
            TenantId = ClientTenant,
            ProcedureTypeId = ProcedureType,
            ReferenceNumber = "REF-QX",
            Status = ProcedureInstanceStatus.PendingOt,
            TransitOfficeId = TransitOffice,
            CreatedByUserId = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        ctx.SaveChanges();
    }

    private static string NewDbName() => Guid.NewGuid().ToString();

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);
}
