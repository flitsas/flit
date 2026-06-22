using Flit.Admin.Application.OtProfile;
using Flit.Admin.Application.OtProfile.GetOtProfile;
using Flit.Admin.Application.OtProfile.UpdateOtFeatureFlag;
using Flit.Admin.Application.OtProfile.UpdateOtProfile;
using Flit.Admin.Domain.OtProfile;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Admin.Tests.OtProfile;

/// <summary>
/// Tests del perfil OT y feature flags (HU #10215) — AC1–AC5 sobre handlers reales
/// con repositorio EF InMemory.
/// </summary>
public sealed class OtProfileHandlerTests
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid ChangedBy = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task AC1_GetProfile_ReturnsOperationModeQuipuxReadOnlyAndFeatureFlags()
    {
        var db = NewDbName();
        var flagId = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            SeedProfile(seed, TenantA, OtOperationModes.Dashboard, quipuxReadOnly: false);
            SeedFlag(seed, TenantA, flagId, "enable_special_queue", isEnabled: false);
        }

        await using var ctx = NewContext(db);
        var handler = new GetOtProfileHandler(new OtProfileRepository(ctx));
        var response = await handler.HandleAsync(new GetOtProfileQuery { TenantId = TenantA });

        response.OperationMode.Should().Be(OtOperationModes.Dashboard);
        response.QuipuxReadOnly.Should().BeFalse();
        response.FeatureFlags.Should().ContainSingle(f => f.FlagKey == "enable_special_queue" && !f.IsEnabled);
    }

    [Fact]
    public async Task AC2_PatchToQuipuxMode_SetsReadOnlyTrueAndPersists()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            SeedProfile(seed, TenantA, OtOperationModes.Dashboard, quipuxReadOnly: false);
        }

        await using (var act = NewContext(db))
        {
            var handler = new UpdateOtProfileHandler(new OtProfileRepository(act));
            var result = await handler.HandleAsync(new UpdateOtProfileCommand
            {
                TenantId = TenantA,
                ChangedBy = ChangedBy,
                Request = new UpdateOtProfileRequest { OperationMode = OtOperationModes.Quipux },
            });

            result.IsValid.Should().BeTrue();
            result.Profile!.OperationMode.Should().Be(OtOperationModes.Quipux);
            result.Profile.QuipuxReadOnly.Should().BeTrue();
        }

        await using var verify = NewContext(db);
        var entity = await verify.TransitOfficeProfiles.SingleAsync(p => p.TenantId == TenantA);
        entity.OperationMode.Should().Be(OtOperationModes.Quipux);
        entity.QuipuxReadOnly.Should().BeTrue();
    }

    [Fact]
    public async Task AC3_UpdateFeatureFlag_EnablesFlagInDatabase()
    {
        var db = NewDbName();
        var flagId = Guid.NewGuid();

        await using (var seed = NewContext(db))
        {
            SeedFlag(seed, TenantA, flagId, "enable_special_queue", isEnabled: false);
        }

        await using (var act = NewContext(db))
        {
            var handler = new UpdateOtFeatureFlagHandler(new OtFeatureFlagRepository(act));
            var result = await handler.HandleAsync(new UpdateOtFeatureFlagCommand
            {
                TenantId = TenantA,
                FlagId = flagId,
                ChangedBy = ChangedBy,
                Request = new UpdateOtFeatureFlagRequest { IsEnabled = true },
            });

            result.Status.Should().Be(UpdateOtFeatureFlagStatus.Updated);
            result.Flag!.IsEnabled.Should().BeTrue();
        }

        await using var verify = NewContext(db);
        var flag = await verify.OtFeatureFlags.SingleAsync(f => f.Id == flagId);
        flag.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task AC5_IgnoresBodyTenantId_UsesCommandTenantFromToken()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            SeedProfile(seed, TenantA, OtOperationModes.Dashboard, quipuxReadOnly: false);
            SeedProfile(seed, TenantB, OtOperationModes.Dashboard, quipuxReadOnly: false);
        }

        await using (var act = NewContext(db))
        {
            var handler = new UpdateOtProfileHandler(new OtProfileRepository(act));
            var result = await handler.HandleAsync(new UpdateOtProfileCommand
            {
                TenantId = TenantA,
                ChangedBy = ChangedBy,
                Request = new UpdateOtProfileRequest
                {
                    OperationMode = OtOperationModes.Quipux,
                    TenantId = TenantB,
                },
            });

            result.IsValid.Should().BeTrue();
        }

        await using var verify = NewContext(db);
        (await verify.TransitOfficeProfiles.SingleAsync(p => p.TenantId == TenantA))
            .OperationMode.Should().Be(OtOperationModes.Quipux);
        (await verify.TransitOfficeProfiles.SingleAsync(p => p.TenantId == TenantB))
            .OperationMode.Should().Be(OtOperationModes.Dashboard);
    }

    [Theory]
    [InlineData("aprobar")]
    [InlineData("rechazar")]
    public async Task AC4_QuipuxReadOnlyGuard_BlocksApproveAndReject(string action)
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            SeedProfile(seed, TenantA, OtOperationModes.Quipux, quipuxReadOnly: true);
        }

        await using var ctx = NewContext(db);
        var guard = new QuipuxReadOnlyGuard(new OtProfileRepository(ctx));
        var result = await guard.ValidateActionAsync(TenantA, action);

        result.IsAllowed.Should().BeFalse();
        result.ErrorCode.Should().Be("QUIPUX_READONLY");
    }

    [Fact]
    public async Task AC4_QuipuxReadOnlyGuard_AllowsOtherActions()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            SeedProfile(seed, TenantA, OtOperationModes.Quipux, quipuxReadOnly: true);
        }

        await using var ctx = NewContext(db);
        var guard = new QuipuxReadOnlyGuard(new OtProfileRepository(ctx));
        var result = await guard.ValidateActionAsync(TenantA, "consultar");

        result.IsAllowed.Should().BeTrue();
    }

    private static string NewDbName() => Guid.NewGuid().ToString();

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);

    private static void SeedProfile(
        FlitDbContext ctx,
        Guid tenantId,
        string operationMode,
        bool quipuxReadOnly)
    {
        ctx.TransitOfficeProfiles.Add(new TransitOfficeProfile
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            TransitOfficeId = OtProfileRepository.DefaultTransitOfficeId,
            OperationMode = operationMode,
            QuipuxReadOnly = quipuxReadOnly,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        ctx.SaveChanges();
    }

    private static void SeedFlag(
        FlitDbContext ctx,
        Guid tenantId,
        Guid flagId,
        string flagKey,
        bool isEnabled)
    {
        ctx.OtFeatureFlags.Add(new OtFeatureFlagEntity
        {
            Id = flagId,
            TenantId = tenantId,
            FlagKey = flagKey,
            IsEnabled = isEnabled,
            Config = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        ctx.SaveChanges();
    }
}
