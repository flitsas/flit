using Flit.Admin.Application.OtRequirements.GetOtRequirements;
using Flit.Admin.Application.OtRequirements.UpdateOtRequirements;
using Flit.Admin.Domain.OtRequirements;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Admin.Tests.OtRequirements;

/// <summary>
/// Tests del cimiento de requisitos por OT (HU #10545) y su API de administración (HU #10546).
/// Feature #10535 — R21/R22/R23.
/// </summary>
public sealed class OtRequirementsTests
{
    private static readonly Guid OtTenant = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TransitOffice = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    private static readonly Guid Admin = Guid.Parse("44444444-4444-4444-4444-444444444444");

    [Fact] // HU #10545 AC1 — sin fila, defaults seguros.
    public async Task HU10545_AC1_Provider_SinFila_DevuelveDefaultsSeguros()
    {
        var db = NewDbName();
        await using var ctx = NewContext(db);
        var provider = NewProvider(ctx);

        var requirements = await provider.ResolveByTenantAsync(OtTenant, TestContext.Current.CancellationToken);

        requirements.RequiresRnmc.Should().BeFalse();
        requirements.AllowPlatePreassign.Should().BeFalse();
        requirements.IdentityValidationEnabled.Should().BeTrue();
    }

    [Fact] // HU #10545 AC2 — con fila, devuelve lo persistido.
    public async Task HU10545_AC2_Provider_ConFila_DevuelvePersistido()
    {
        var db = NewDbName();
        await using (var seed = NewContext(db))
        {
            SeedOt(seed, OtTenant, TransitOffice);
            await new OtRequirementsRepository(seed).SaveAsync(
                OtTenant,
                requiresRnmc: true,
                allowPlatePreassign: true,
                identityValidationEnabled: false,
                changedBy: Admin,
                cancellationToken: TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var requirements = await NewProvider(ctx)
            .ResolveByTenantAsync(OtTenant, TestContext.Current.CancellationToken);

        requirements.RequiresRnmc.Should().BeTrue();
        requirements.AllowPlatePreassign.Should().BeTrue();
        requirements.IdentityValidationEnabled.Should().BeFalse();
        requirements.TransitOfficeId.Should().Be(TransitOffice);
    }

    [Fact] // HU #10546 AC1 — PUT requires_rnmc = true queda persistido.
    public async Task HU10546_AC1_Put_RequiresRnmc_Persiste()
    {
        var db = NewDbName();
        await using (var seed = NewContext(db))
        {
            SeedOt(seed, OtTenant, TransitOffice);
        }

        await using (var ctx = NewContext(db))
        {
            var result = await NewUpdateHandler(ctx).HandleAsync(new UpdateOtRequirementsCommand
            {
                TenantId = OtTenant,
                ChangedBy = Admin,
                Request = new UpdateOtRequirementsRequest { RequiresRnmc = true },
            }, TestContext.Current.CancellationToken);

            result.IsValid.Should().BeTrue();
            result.Requirements!.RequiresRnmc.Should().BeTrue();
        }

        await using var verify = NewContext(db);
        var reread = await NewGetHandler(verify).HandleAsync(
            new GetOtRequirementsQuery { TenantId = OtTenant }, TestContext.Current.CancellationToken);
        reread.RequiresRnmc.Should().BeTrue();
    }

    [Fact] // HU #10546 AC2 — PUT allow_plate_preassign = true queda habilitado.
    public async Task HU10546_AC2_Put_AllowPlatePreassign_Persiste()
    {
        var db = NewDbName();
        await using (var seed = NewContext(db))
        {
            SeedOt(seed, OtTenant, TransitOffice);
        }

        await using var ctx = NewContext(db);
        var result = await NewUpdateHandler(ctx).HandleAsync(new UpdateOtRequirementsCommand
        {
            TenantId = OtTenant,
            ChangedBy = Admin,
            Request = new UpdateOtRequirementsRequest { AllowPlatePreassign = true },
        }, TestContext.Current.CancellationToken);

        result.Requirements!.AllowPlatePreassign.Should().BeTrue();
    }

    [Fact] // HU #10546 — merge: un campo nulo conserva el valor actual (toggle independiente en UI).
    public async Task HU10546_Merge_CampoNulo_ConservaActual()
    {
        var db = NewDbName();
        await using (var seed = NewContext(db))
        {
            SeedOt(seed, OtTenant, TransitOffice);
        }

        await using (var ctx = NewContext(db))
        {
            await NewUpdateHandler(ctx).HandleAsync(new UpdateOtRequirementsCommand
            {
                TenantId = OtTenant,
                ChangedBy = Admin,
                Request = new UpdateOtRequirementsRequest { RequiresRnmc = true },
            }, TestContext.Current.CancellationToken);
        }

        await using (var ctx = NewContext(db))
        {
            // Solo conmuta identidad; requires_rnmc no viaja y debe conservarse.
            var result = await NewUpdateHandler(ctx).HandleAsync(new UpdateOtRequirementsCommand
            {
                TenantId = OtTenant,
                ChangedBy = Admin,
                Request = new UpdateOtRequirementsRequest { IdentityValidationEnabled = false },
            }, TestContext.Current.CancellationToken);

            result.Requirements!.RequiresRnmc.Should().BeTrue();
            result.Requirements!.IdentityValidationEnabled.Should().BeFalse();
        }
    }

    [Fact] // Guarda anti-squat — un tenant sin perfil ni grant no puede escribir requisitos de OT.
    public async Task SaveAsync_TenantSinPerfilNiGrant_LanzaScopeException()
    {
        var db = NewDbName();
        await using var ctx = NewContext(db);

        var act = async () => await new OtRequirementsRepository(ctx).SaveAsync(
            OtTenant,
            requiresRnmc: true,
            allowPlatePreassign: true,
            identityValidationEnabled: true,
            changedBy: Admin,
            cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<OtRequirementsScopeException>();
    }

    [Fact] // Guarda anti-colisión — la oficina ya la configuró otro tenant (unique global).
    public async Task SaveAsync_OficinaOcupadaPorOtroTenant_LanzaScopeException()
    {
        var db = NewDbName();
        var otherTenant = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

        await using (var seed = NewContext(db))
        {
            // Ambos OT apuntan (por perfil) a la MISMA oficina: el segundo choca con el primero.
            SeedOt(seed, OtTenant, TransitOffice);
            SeedOt(seed, otherTenant, TransitOffice);
            await new OtRequirementsRepository(seed).SaveAsync(
                OtTenant,
                requiresRnmc: true,
                allowPlatePreassign: false,
                identityValidationEnabled: true,
                changedBy: Admin,
                cancellationToken: TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var act = async () => await new OtRequirementsRepository(ctx).SaveAsync(
            otherTenant,
            requiresRnmc: false,
            allowPlatePreassign: true,
            identityValidationEnabled: true,
            changedBy: Admin,
            cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<OtRequirementsScopeException>();
    }

    private static void SeedOt(FlitDbContext ctx, Guid otTenantId, Guid transitOfficeId)
    {
        ctx.TransitOfficeProfiles.Add(new TransitOfficeProfile
        {
            Id = Guid.NewGuid(),
            TenantId = otTenantId,
            TransitOfficeId = transitOfficeId,
            OperationMode = "dashboard",
            QuipuxReadOnly = false,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        ctx.SaveChanges();
    }

    private static OtRequirementsProvider NewProvider(FlitDbContext ctx) =>
        new(new OtRequirementsRepository(ctx));

    private static GetOtRequirementsHandler NewGetHandler(FlitDbContext ctx) =>
        new(NewProvider(ctx));

    private static UpdateOtRequirementsHandler NewUpdateHandler(FlitDbContext ctx)
    {
        var repo = new OtRequirementsRepository(ctx);
        return new UpdateOtRequirementsHandler(new OtRequirementsProvider(repo), repo);
    }

    private static string NewDbName() => Guid.NewGuid().ToString();

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);
}
